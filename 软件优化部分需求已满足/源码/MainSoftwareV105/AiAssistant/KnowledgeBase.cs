using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MainSoftwareV100
{
    /// <summary>
    /// 知识库（静态类，不用 new 直接用）。
    /// 从 exe 目录下的知识库文件夹（默认"AI知识库"）读全部 .md 文件，
    /// 按关键词命中率打分，返回最相关的若干片段垫给AI。
    /// 零依赖、可离线、Markdown纯文本——比向量检索简单一个数量级，对这个规模（几万个字）完全够用。
    /// </summary>
    public static class KnowledgeBase
    {
        private class Doc
        {
            public string FileName = "";
            public string[] Paragraphs = new string[0];   //按空行切分成段
        }

        private static List<Doc> _docs = new List<Doc>();
        private static string _loadedDir = "";

        /// <summary>加载（或重新加载）知识库。返回载入的文档数。</summary>
        public static int Load()
        {
            _docs = new List<Doc>();
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string dir = Path.Combine(exeDir, AiConfig.KbDir);
            _loadedDir = dir;
            if (!Directory.Exists(dir)) return 0;

            foreach (string file in Directory.GetFiles(dir, "*.md"))
            {
                Doc d = new Doc();
                d.FileName = Path.GetFileNameWithoutExtension(file);
                string text = File.ReadAllText(file, Encoding.UTF8);
                //按空行切段，段是检索和返回的基本单位
                d.Paragraphs = text.Split(new string[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(p => p.Trim())
                                   .Where(p => p.Length > 10)
                                   .ToArray();
                _docs.Add(d);
            }
            return _docs.Count;
        }

        public static string InfoText()
        {
            int paraCount = _docs.Sum(d => d.Paragraphs.Length);
            return "知识库已载入" + _docs.Count + "个文档、共" + paraCount + "个段落（目录：" + _loadedDir + "）";
        }

        /// <summary>
        /// 关键词检索：把query按空格/标点切成词，统计每个段落命中多少词，返回得分最高的片段。
        /// maxChars 控制总长度，防止知识塞太多挤占对话上下文。
        /// </summary>
        public static string Search(string query, int maxChars = 2000)
        {
            if (_docs.Count == 0) return "（知识库为空）";

            //切词：中文按二字滑窗+整词，西文按空格
            List<string> terms = CutTerms(query);
            if (terms.Count == 0) return "";

            List<KeyValuePair<int, string>> scored = new List<KeyValuePair<int, string>>();
            foreach (Doc d in _docs)
            {
                foreach (string p in d.Paragraphs)
                {
                    int score = 0;
                    foreach (string t in terms)
                        if (p.Contains(t)) score += t.Length;   //长词命中权重更高
                    if (score > 0) scored.Add(new KeyValuePair<int, string>(score, p));
                }
            }
            if (scored.Count == 0) return "";

            //按得分降序，拼接到达maxChars为止
            StringBuilder sb = new StringBuilder();
            int used = 0;
            foreach (var kv in scored.OrderByDescending(k => k.Key))
            {
                string block = "[知识] " + kv.Value.Replace("\r\n", " ") + "\n";
                if (used + block.Length > maxChars) continue;   //跳过长段而不是截断，保持语义完整
                sb.Append(block);
                used += block.Length;
                if (used > maxChars * 0.9) break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 切词：连续中文按2~4字滑窗，数字/字母按整体。
        /// 不追求语言学正确，只追求"段落里含不含这个串"。
        /// </summary>
        private static List<string> CutTerms(string query)
        {
            List<string> terms = new List<string>();
            StringBuilder cur = new StringBuilder();
            Action flush = () =>
            {
                if (cur.Length == 0) return;
                string w = cur.ToString();
                cur.Clear();
                if (w.Length <= 1) return;
                //中文词：加整词 + 二字滑窗（查"触发不稳"时能命中"不稳"）
                if (w.Any(c => c > 127))
                {
                    terms.Add(w);
                    for (int i = 0; i + 2 <= w.Length; i++) terms.Add(w.Substring(i, 2));
                }
                else terms.Add(w.ToLower());
            };

            foreach (char c in query)
            {
                bool isWord = char.IsLetterOrDigit(c) || c > 127;
                if (isWord) cur.Append(c);
                else flush();
            }
            flush();

            //去重、去掉太常见的泛词
            string[] stop = { "什么", "怎么", "为什么", "如何", "一下", "还是", "的", "了", "是", "在", "和", "我", "你", "这", "那", "有", "个", "请问" };
            return terms.Where(t => t.Length >= 2 && !stop.Contains(t)).Distinct().ToList();
        }
    }
}
