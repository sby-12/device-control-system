using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MainSoftwareV100
{
    /// <summary>
    /// AI对话核心：负责和大模型API往返、函数调用循环、流式输出。
    /// 流程：用户提问 → 检索知识库垫上下文 → 发给模型 → 模型要么直接回答，要么要求调用工具 →
    ///       执行工具、把结果喂回 → 最多6轮，直到模型给出最终回答（流式吐出到窗口）。
    /// 记忆 = _history 里的最近若干条，窗口关掉即清空（符合"每次开启刷新"）。
    /// </summary>
    public class AiAgent
    {
        private static readonly HttpClient _http = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };

        private readonly List<JObject> _history = new List<JObject>();
        private bool _busy = false;

        //给窗口的事件：状态变化 / 流式字块 / 本轮结束
        public event Action<string> OnStatus;
        public event Action<string> OnToken;
        public event Action<string> OnError;

        private const int MaxToolRounds = 6;

        private static string SystemPrompt()
        {
            return
                "你是器件检测系统的AI助手，帮助操作员使用示波器（RIGOL MHO900，4通道）和直流电源（NGI N36200）完成半导体器件测试。\n" +
                "规则：\n" +
                "1. 始终用简体中文回答，数字带单位。\n" +
                "2. 回答操作步骤、测试方法、故障原因之前，先调用search_knowledge查本地知识库，再结合知识作答；知识库里没有就说明是你的经验判断。\n" +
                "3. 需要看波形时优先调用get_wave_stats（统计摘要）而不是原始点；需要看形状再调get_wave_downsample；对比前后变化（有没有变大、漂移、过冲加剧）用get_wave_history。\n" +
                "4. 每次调用工具前，用一句话告诉操作员你要做什么；工具返回的结果要翻译成普通人话，不要原样贴JSON。\n" +
                "5. 测量类工具返回科学计数字符串（如2.500000E+0），要换算成带单位的直观数值。\n" +
                "6. 涉及打开电源输出、恢复出厂、原始指令时，系统会向操作员二次确认，这是正常的安全保护。\n" +
                "7. 不确定的事直说不确定，不要编造型号、参数和手册页码。\n" +
                "8. 助手运行在实验室电脑上，语气专业、简洁、像有经验的师兄。";
        }

        public AiAgent()
        {
            _history.Add(new JObject { ["role"] = "system", ["content"] = SystemPrompt() });
        }

        /// <summary>处理一条用户消息（窗口发送按钮调用）。</summary>
        public async Task SendAsync(string userText)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                //1. 检索知识库，有命中就垫在用户消息前面
                string kb = KnowledgeBase.Search(userText);
                string content = userText;
                if (kb.Length > 0) content = "【参考资料】\n" + kb + "\n【操作员的问题】\n" + userText;

                _history.Add(new JObject { ["role"] = "user", ["content"] = content });
                TrimMemory();

                //2. 工具调用循环（每轮都是真流式：内容边收边吐，tool_calls分片在流里组装）
                for (int round = 0; round < MaxToolRounds; round++)
                {
                    ReportStatus("正在思考（第" + (round + 1) + "轮）...");
                    JObject message = await CallApiStreamingAsync();
                    if (message == null)   //错误已在流式方法内上报
                    {
                        RemoveLastUser();
                        return;
                    }

                    JToken toolCalls = message["tool_calls"];
                    bool hasTools = toolCalls != null && toolCalls.Any();

                    //记录assistant消息（有工具调用时带上tool_calls）
                    JObject record = new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = message["content"] ?? ""
                    };
                    if (hasTools) record["tool_calls"] = toolCalls;
                    _history.Add(record);

                    if (!hasTools)
                    {
                        //没有工具调用 = 最终回答，内容已在流式过程中实时吐出
                        TrimMemory();
                        ReportStatus("就绪");
                        return;
                    }

                    foreach (JObject tc in toolCalls)
                    {
                        string toolName = (string)tc["function"]?["name"];
                        JObject args = new JObject();
                        try { args = JObject.Parse((string)tc["function"]?["arguments"] ?? "{}"); }
                        catch { /*参数解析失败就传空对象，让工具自己报缺参*/ }

                        ReportStatus("正在调用工具：" + toolName);
                        string result = await AiTools.Execute(toolName, args);

                        _history.Add(new JObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = (string)tc["id"],
                            ["content"] = result
                        });
                    }
                }

                OnError?.Invoke("工具调用轮数过多，已停止。请把问题说得更具体些。");
            }
            catch (Exception ex)
            {
                OnError?.Invoke("请求失败：" + ex.Message + "（检查网络能否访问API地址）");
                RemoveLastUser();
            }
            finally
            {
                _busy = false;
            }
        }

        public bool IsBusy { get { return _busy; } }

        /// <summary>清掉记忆（窗口提供"清空对话"按钮）。</summary>
        public void ClearMemory()
        {
            JObject sys = _history[0];
            _history.Clear();
            _history.Add(sys);
        }

        //============ 内部实现 ============

        private void ReportStatus(string s) { OnStatus?.Invoke(s); }

        private void RemoveLastUser()
        {
            for (int i = _history.Count - 1; i >= 1; i--)
                if ((string)_history[i]["role"] == "user") { _history.RemoveAt(i); return; }
        }

        /// <summary>记忆裁剪：只保留系统消息+最近N条（system不算）。
        /// 注意：裁剪必须停在一条 user 消息上——如果停在 tool/assistant 中间，
        /// 发给API的消息序列会断链（tool结果没有对应的assistant请求），API会直接报错。</summary>
        private void TrimMemory()
        {
            int keep = AiConfig.MemoryTurns;
            if (keep < 4) keep = 4;
            int count = 0;
            for (int i = _history.Count - 1; i >= 1; i--)
            {
                count++;
                if (count >= keep)
                {
                    //从 i 往前找最近一条 user，把它之前的（1~边界）全删掉，保证序列从 user 开始
                    for (int j = i; j >= 1; j--)
                    {
                        if ((string)_history[j]["role"] == "user")
                        {
                            if (j > 1) _history.RemoveRange(1, j - 1);
                            return;
                        }
                    }
                    return;
                }
            }
        }

        /// <summary>请求体：恒定stream=true，内容和tool_calls都在SSE流里组装。</summary>
        private JObject BuildRequestBody()
        {
            return new JObject
            {
                ["model"] = AiConfig.Model,
                ["messages"] = new JArray(_history.ToArray()),
                ["tools"] = AiTools.ApiSchema(),
                ["tool_choice"] = "auto",
                ["stream"] = true,
                ["temperature"] = 0.3
            };
        }

        /// <summary>tool_calls分片累积器：流式响应里一个工具调用的id/name/arguments是分成多块到达的。</summary>
        private class ToolCallAcc
        {
            public string Id = "";
            public string Name = "";
            public StringBuilder Args = new StringBuilder();
        }

        /// <summary>
        /// 真流式调用：用ResponseHeadersRead拿到响应头后逐行读SSE，内容增量即时吐给窗口（真打字机效果），
        /// tool_calls分片按index累积组装。返回组装好的assistant消息（含content和可选tool_calls）；
        /// 出错返回null（错误已通过OnError上报）。
        /// 兜底：若服务端忽略stream=true直接返回整段JSON（极少数兼容层），按非流式解析并一次性吐出内容。
        /// 另一收益：ResponseHeadersRead下60秒超时只约束"等响应头"阶段，长回答不再被掐断。
        /// </summary>
        private async Task<JObject> CallApiStreamingAsync()
        {
            string url = AiConfig.ApiBase.TrimEnd('/') + "/chat/completions";
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AiConfig.ApiKey);
            req.Content = new StringContent(BuildRequestBody().ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex)
            {
                OnError?.Invoke("请求失败：" + ex.Message + "（检查网络能否访问API地址）");
                return null;
            }

            if (!resp.IsSuccessStatusCode)
            {
                string errBody = await resp.Content.ReadAsStringAsync();
                OnError?.Invoke("API返回错误：HTTP " + (int)resp.StatusCode + "：" +
                    errBody.Substring(0, Math.Min(300, errBody.Length)) + "（检查AI Config.txt里的地址、密钥、模型名）");
                resp.Dispose();
                return null;
            }

            StringBuilder content = new StringBuilder();
            SortedDictionary<int, ToolCallAcc> toolParts = new SortedDictionary<int, ToolCallAcc>();
            StringBuilder nonSseLines = new StringBuilder();   //非data行留底，用于整段JSON兜底
            int dataLines = 0;
            bool done = false;

            using (resp)
            using (Stream stream = await resp.Content.ReadAsStreamAsync())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while (!done && (line = await reader.ReadLineAsync()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (!trimmed.StartsWith("data:")) { nonSseLines.AppendLine(trimmed); continue; }
                    dataLines++;
                    string payload = trimmed.Substring(5).Trim();
                    if (payload == "[DONE]") { done = true; break; }

                    JObject chunk;
                    try { chunk = JObject.Parse(payload); }
                    catch { continue; }

                    JToken delta = chunk["choices"]?[0]?["delta"];
                    if (delta == null) continue;

                    string piece = (string)delta["content"];
                    if (!string.IsNullOrEmpty(piece))
                    {
                        content.Append(piece);
                        OnToken?.Invoke(piece);
                    }

                    JToken tcs = delta["tool_calls"];
                    if (tcs != null)
                    {
                        foreach (JObject tc in tcs)
                        {
                            int idx = tc["index"] != null ? (int)tc["index"] : 0;
                            ToolCallAcc acc;
                            if (!toolParts.TryGetValue(idx, out acc))
                            {
                                acc = new ToolCallAcc();
                                toolParts[idx] = acc;
                            }
                            if (tc["id"] != null) acc.Id = (string)tc["id"];
                            string nm = (string)tc["function"]?["name"];
                            if (!string.IsNullOrEmpty(nm)) acc.Name = nm;
                            string argPiece = (string)tc["function"]?["arguments"];
                            if (!string.IsNullOrEmpty(argPiece)) acc.Args.Append(argPiece);
                        }
                    }
                }
            }

            //兜底：一行SSE都没收到，服务端可能直接回了整段JSON
            if (dataLines == 0 && nonSseLines.Length > 0)
            {
                try
                {
                    JObject reply = JObject.Parse(nonSseLines.ToString());
                    JObject m = (JObject)reply["choices"]?[0]?["message"];
                    if (m != null)
                    {
                        string c = (string)m["content"];
                        if (!string.IsNullOrEmpty(c)) OnToken?.Invoke(c);
                        return m;
                    }
                    JToken err = reply["error"];
                    if (err != null)
                    {
                        OnError?.Invoke("API返回错误：" + (err["message"] != null ? (string)err["message"] : reply.ToString()));
                        return null;
                    }
                }
                catch { }
                string rawText = nonSseLines.ToString();
                OnError?.Invoke("API返回格式异常：" + rawText.Substring(0, Math.Min(300, rawText.Length)));
                return null;
            }

            //组装assistant消息
            JObject message = new JObject { ["content"] = content.ToString() };
            if (toolParts.Count > 0)
            {
                JArray arr = new JArray();
                foreach (KeyValuePair<int, ToolCallAcc> kv in toolParts)
                {
                    arr.Add(new JObject
                    {
                        ["id"] = kv.Value.Id,
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = kv.Value.Name,
                            ["arguments"] = kv.Value.Args.ToString()
                        }
                    });
                }
                message["tool_calls"] = arr;
            }
            return message;
        }
    }
}
