using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using InstrumentInterfaceLibrary;   //2026-10-30 接口隔离：工具只认接口不认具体机型

namespace MainSoftwareV100
{
    /// <summary>
    /// AI工具注册表：把仪器库函数包装成"大模型可调用的工具"。
    /// 每个工具 = 名字 + 中文说明 + 参数定义(JSON) + 执行体。
    /// 说明写得好不好直接决定AI会不会正确调用，所以每个说明都写清了"干什么、参数怎么填、返回什么"。
    /// </summary>
    public static class AiTools
    {
        public class Tool
        {
            public string Name = "";
            public string Description = "";
            public JObject Parameters = new JObject();   //JSON Schema
            public Func<JObject, Task<string>> Run = null;
            public bool Dangerous = false;               //危险操作：执行前先弹窗问操作员
            public Func<JObject, string> Validate = null; //参数校验：返回null通过，返回说明文字则拦截（不执行）
        }

        private static readonly List<Tool> _tools = new List<Tool>();
        private static IScope _scope = null;        //2026-10-30 接口隔离：任何品牌的示波器都能接
        private static IPowerSource _power = null;  //2026-10-30 接口隔离：电流源A/B 任一槽位

        /// <summary>危险操作确认回调：由窗口注入，返回true才执行。</summary>
        public static Func<string, Task<bool>> ConfirmDangerous = null;

        //最近一次电源回读值（主窗口200ms轮询回调里更新）
        public static double LastPowerVoltage = 0;
        public static double LastPowerCurrent = 0;
        public static DateTime LastPowerTime = DateTime.MinValue;

        public static void Init(IScope scope, IPowerSource power)
        {
            _scope = scope;
            _power = power;
        }

        /// <summary>
        /// 仪器动态获取回调：由AI窗口注入，每次执行工具时实时从主窗口取当前连接对象。
        /// 这样"先开AI窗口、后主界面才连接仪器"也能正常工作，不用重开窗口；
        /// 仪器断开后也会立刻回到"尚未连接"提示，而不是拿着失效引用操作。
        /// </summary>
        public static Func<IScope> ScopeProvider = null;
        public static Func<IPowerSource> PowerProvider = null;

        /// <summary>波形帧获取回调：由AI窗口注入。参数提取类工具用它取深存/快照数据（ch:0~3），返回null表示无数据。</summary>
        public static Func<int, WaveSnapshot.ChannelFrame> FrameProvider = null;

        /// <summary>当前示波器连接：优先走Provider实时取，未注入时用Init传入的静态引用。</summary>
        private static IScope Scope
        {
            get { return ScopeProvider != null ? ScopeProvider() : _scope; }
        }

        /// <summary>当前电源连接：优先走Provider实时取，未注入时用Init传入的静态引用。</summary>
        private static IPowerSource Power
        {
            get { return PowerProvider != null ? PowerProvider() : _power; }
        }

        /// <summary>注册一个工具。</summary>
        private static void Add(string name, string desc, string[] required, JObject props, Func<JObject, Task<string>> run,
            bool dangerous = false, Func<JObject, string> validate = null)
        {
            JObject paramObj = new JObject();
            if (props != null) paramObj = new JObject(props);
            paramObj["type"] = "object";
            if (required != null && required.Length > 0) paramObj["required"] = new JArray(required);

            _tools.Add(new Tool()
            {
                Name = name,
                Description = desc,
                Parameters = paramObj,
                Run = run,
                Dangerous = dangerous,
                Validate = validate
            });
        }

        //快捷参数构造：{"channel": {"type":"string","description":"..."}}
        private static JObject P(string name, string type, string desc)
        {
            return new JObject { [name] = new JObject { ["type"] = type, ["description"] = desc } };
        }

        /// <summary>给API的tools字段。</summary>
        public static JArray ApiSchema()
        {
            JArray arr = new JArray();
            foreach (Tool t in _tools)
            {
                arr.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.Parameters
                    }
                });
            }
            return arr;
        }

        public static int Count { get { return _tools.Count; } }

        /// <summary>
        /// 执行一次工具调用。返回结果文本；出错返回错误说明（喂给AI让它自己纠正）。
        /// 顺序：找工具 → 仪器连接检查 → 参数范围校验 → 危险操作确认（留审计日志） → 执行。
        /// </summary>
        public static async Task<string> Execute(string name, JObject args)
        {
            Tool t = _tools.FirstOrDefault(x => x.Name == name);
            if (t == null) return "工具不存在：" + name;

            //仪器没连上时给出人话提示，而不是抛"未将对象引用设置到对象的实例"
            //（Scope/Power实时从主窗口取，主界面连上后直接再问一次即可，不用重开AI窗口）
            if (name.StartsWith("scope_") && Scope == null)
                return "示波器尚未连接。请先在主界面连上示波器再让我操作；连上后不用重开AI窗口，直接再问一次即可。";
            if (name.StartsWith("power_") && Power == null)
                return "电源N36200尚未连接。请先在主界面连上电源再让我操作；连上后不用重开AI窗口，直接再问一次即可。";

            //参数范围校验：拦截明显不合理的值（负数电压、超量程、通道越界），不发给仪器
            if (t.Validate != null)
            {
                string verr = null;
                try { verr = t.Validate(args ?? new JObject()); }
                catch (Exception vex) { verr = "参数格式不对：" + vex.Message; }
                if (verr != null) return "参数不合理，未执行：" + verr + "。请改正后重试。";
            }

            if (t.Dangerous && ConfirmDangerous != null)
            {
                string argText = (args != null) ? args.ToString(Newtonsoft.Json.Formatting.None) : "";
                bool ok = await ConfirmDangerous(t.Name + " " + argText);
                AuditLog(t.Name + " " + argText + " → 操作员" + (ok ? "批准" : "拒绝"));
                if (!ok) return "操作员拒绝执行该操作。";
            }

            try
            {
                string r = await t.Run(args ?? new JObject());
                if (t.Dangerous) AuditLog(t.Name + " 执行结果：" + r);
                return r;
            }
            catch (Exception ex)
            {
                if (t.Dangerous) AuditLog(t.Name + " 执行出错：" + ex.Message);
                return "执行出错：" + ex.Message;
            }
        }

        /// <summary>
        /// 危险操作审计日志：exe目录下的"AI操作日志.txt"，一行一条（时间、工具与参数、操作员决定、执行结果）。
        /// 出事故时可复盘"是AI请求的、谁批准的、执行成没成"。写失败静默忽略，绝不阻塞测控。
        /// </summary>
        private static void AuditLog(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                File.AppendAllText(Path.Combine(dir, "AI操作日志.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        //============ 参数校验器（执行前拦截，返回null表示通过）============

        //可调上限：N36200是系列型号、各机型量程不同，按实验室实际机型改这两个常量即可
        private const double PowerMaxVolts = 200;   //电压/过压保护上限（V）
        private const double PowerMaxAmps = 100;    //电流/过流保护上限（A）

        /// <summary>数值参数范围校验：key应在min~max之间。</summary>
        private static Func<JObject, string> Range(string key, double min, double max, string unit)
        {
            return a =>
            {
                if (a[key] == null) return "缺少参数 " + key;
                double v = (double)a[key];
                if (v < min || v > max)
                    return "参数 " + key + " 应在 " + min + " ~ " + max + " " + unit + " 之间（收到 " + v + "）";
                return null;
            };
        }

        /// <summary>示波器通道号1~4校验。</summary>
        private static string Ch14(JObject a)
        {
            if (a["channel"] == null) return "缺少参数 channel";
            int ch = (int)a["channel"];
            return (ch >= 1 && ch <= 4) ? null : "通道号只能是1~4（收到 " + ch + "）";
        }

        /// <summary>指定key的通道号1~4校验（参数提取工具的多通道版）。</summary>
        private static Func<JObject, string> ChKey(string key)
        {
            return a =>
            {
                if (a[key] == null) return "缺少参数 " + key;
                int ch = (int)a[key];
                return (ch >= 1 && ch <= 4) ? null : key + " 只能是1~4（收到 " + ch + "）";
            };
        }

        //============ 参数提取工具的取数辅助（数据已在电脑内存，不经仪器）============

        /// <summary>取通道波形（通道号1~4），转double数组；无数据返回null。</summary>
        private static double[] FrameArr(int ch1to4)
        {
            if (FrameProvider == null) return null;
            WaveSnapshot.ChannelFrame f = FrameProvider(ch1to4 - 1);
            if (f == null || f.Data == null || f.Data.Length < 10) return null;
            double[] d = new double[f.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = f.Data[i];
            return d;
        }

        /// <summary>取通道采样间隔（秒），异常给1ns兜底。</summary>
        private static double FrameDt(int ch1to4)
        {
            if (FrameProvider == null) return 1e-9;
            WaveSnapshot.ChannelFrame f = FrameProvider(ch1to4 - 1);
            return (f != null && f.XIncrement > 0) ? f.XIncrement : 1e-9;
        }

        /// <summary>组合多个校验器，任一不通过即拦截。</summary>
        private static Func<JObject, string> All(params Func<JObject, string>[] validators)
        {
            return a =>
            {
                foreach (Func<JObject, string> v in validators)
                {
                    string err = v(a);
                    if (err != null) return err;
                }
                return null;
            };
        }

        /// <summary>
        /// 登记全部工具。在AI窗口打开时调用一次即可（幂等，内部清空重建）。
        /// </summary>
        public static void Build()
        {
            _tools.Clear();

            //============ 波形查看与公式计算（不看原始点也能懂波形）============
            Add("get_wave_stats", "获取指定通道最近一帧波形的完整统计摘要：最大/最小/峰峰值/平均值/有效值、纹波、峰值因数（正弦波应≈1.414）、波形因数（正弦波应≈1.11）、最快上升/下降时间(10%~90%)、近似占空比、削顶/削底检测、估算频率。分析波形正不正常优先调它。",
                new[] { "channel" }, P("channel", "integer", "通道号1~4"),
                async a => WaveSnapshot.StatsText((int)a["channel"] - 1), validate: Ch14);

            Add("get_wave_spectrum", "获取指定通道波形的频谱分析：主频（插值精化）、前5个谱峰的频率与幅值、THD总谐波失真估算(2~5次)。用于判断谐波失真、振铃频率、干扰成分。",
                new[] { "channel" }, P("channel", "integer", "通道号1~4"),
                async a => WaveSnapshot.SpectrumText((int)a["channel"] - 1), validate: Ch14);

            Add("get_wave_downsample", "获取指定通道波形的抽稀采样序列（等间隔取样，最多500个点），用于观察波形大致形状。",
                new[] { "channel" }, P("channel", "integer", "通道号1~4"),
                async a => WaveSnapshot.DownsampleText((int)a["channel"] - 1, 500), validate: Ch14);

            Add("get_wave_history", "获取指定通道最近若干帧（最多10帧）的统计趋势：每帧的峰峰值/最大值/平均值/有效值/频率及最新帧的变化量。回答\"波形有没有变大/漂移/过冲比刚才大了没有\"这类前后对比问题时调用。",
                new[] { "channel" }, P("channel", "integer", "通道号1~4"),
                async a => WaveSnapshot.HistoryText((int)a["channel"] - 1), validate: Ch14);

            Add("calc_conduction_loss", "计算器件导通损耗。mode=\"voltage\"用P=V*I（如IGBT饱和压降法），mode=\"resistance\"用P=I^2*R（如MOSFET导通电阻法），占空比duty取0~1。返回瓦特。",
                new[] { "mode", "current", "duty" },
                new JObject
                {
                    ["mode"] = new JObject { ["type"] = "string", ["description"] = "\"voltage\"或\"resistance\"" },
                    ["voltage_drop"] = new JObject { ["type"] = "number", ["description"] = "导通压降V（mode=voltage时用）" },
                    ["resistance"] = new JObject { ["type"] = "number", ["description"] = "导通电阻Ω（mode=resistance时用）" },
                    ["current"] = new JObject { ["type"] = "number", ["description"] = "通过电流A" },
                    ["duty"] = new JObject { ["type"] = "number", ["description"] = "占空比0~1，不填默认1" }
                },
                async a =>
                {
                    string mode = (string)a["mode"];
                    double i = (double)a["current"];
                    double duty = a["duty"] != null ? (double)a["duty"] : 1.0;
                    if (mode == "voltage")
                    {
                        double v = (double)a["voltage_drop"];
                        return "导通损耗约 " + (v * i * duty).ToString("0.###") + " W（P=V×I×占空比=" + v + "×" + i + "×" + duty + "）";
                    }
                    else
                    {
                        double r = (double)a["resistance"];
                        return "导通损耗约 " + (i * i * r * duty).ToString("0.###") + " W（P=I²×R×占空比=" + i + "²×" + r + "×" + duty + "）";
                    }
                });

            Add("calc_rms_from_peak", "正弦波由峰值求有效值：Vrms=Vp/√2。返回有效值。",
                new[] { "peak" }, P("peak", "number", "峰值V"),
                async a => "有效值约 " + (((double)a["peak"]) / Math.Sqrt(2)).ToString("0.###") + " V");

            Add("calc_time_constant", "RC或RL电路时间常数：RC电路τ=R×C，RL电路τ=L/R。返回τ和约5τ（工程上认为充放电完成）。",
                new[] { "type" },
                new JObject
                {
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "\"RC\"或\"RL\"" },
                    ["resistance"] = new JObject { ["type"] = "number", ["description"] = "电阻Ω" },
                    ["capacitance"] = new JObject { ["type"] = "number", ["description"] = "电容F（RC时用）" },
                    ["inductance"] = new JObject { ["type"] = "number", ["description"] = "电感H（RL时用）" }
                },
                async a =>
                {
                    double r = (double)a["resistance"];
                    if ((string)a["type"] == "RC")
                    {
                        double c = (double)a["capacitance"];
                        return "τ=R×C=" + (r * c).ToString("0.###") + " 秒，5τ≈" + (5 * r * c).ToString("0.###") + " 秒";
                    }
                    double l = (double)a["inductance"];
                    return "τ=L/R=" + (l / r).ToString("0.###") + " 秒，5τ≈" + (5 * l / r).ToString("0.###") + " 秒";
                });

            Add("search_knowledge", "搜索本地知识库（仪器手册要点、器件测试方法、故障分析），返回最相关的资料片段。回答操作问题、测试方法问题、故障原因前应先调用它。",
                new[] { "query" }, P("query", "string", "检索关键词，如\"二极管反向恢复\"或\"无波形\"，多个词用空格分开"),
                async a => KnowledgeBase.Search((string)a["query"]));

            //============ 参数提取（调DptAnalysis数学库，数据取自已采集的深存/快照波形）============
            Add("analyze_switching", "从已采集波形中提取功率器件开关参数：td(on)/tr/ton、td(off)/tf/toff、Tz拖尾时间、dv/dt、di/dt、Eon/Eoff开通关断能量、Vdsm过冲峰值、ΔVce过冲、Ls杂散电感、Pmax峰值功率、对管串扰正负尖峰。适用于双脉冲测试波形：vge_channel=驱动脉冲通道，vce_channel=器件电压通道，ic_channel=器件电流通道，other_channel=对管Vge通道（测串扰用，可省略）。数据自动优先取深度保存的原始波形，没有再退回实时快照。分析开关损耗、开关速度、过冲是否正常时调用，判读标准可再调search_knowledge查\"动态参数\"。",
                new[] { "vge_channel", "vce_channel", "ic_channel" },
                new JObject
                {
                    ["vge_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vge驱动脉冲通道1~4" },
                    ["vce_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vce器件电压通道1~4" },
                    ["ic_channel"] = new JObject { ["type"] = "integer", ["description"] = "Ic器件电流通道1~4" },
                    ["other_channel"] = new JObject { ["type"] = "integer", ["description"] = "对管Vge通道1~4（测串扰尖峰，不填则跳过）" }
                },
                async a =>
                {
                    double[] vge = FrameArr((int)a["vge_channel"]);
                    double[] vce = FrameArr((int)a["vce_channel"]);
                    double[] ic = FrameArr((int)a["ic_channel"]);
                    if (vge == null || vce == null || ic == null)
                        return "通道数据不足：请先在主界面让示波器采到波形（深度保存或实时曲线），并确认所选通道有信号";
                    double[] other = (a["other_channel"] != null) ? FrameArr((int)a["other_channel"]) : null;
                    if (other != null && other.Length != vce.Length) other = null;
                    DptSwitching.Result r = DptSwitching.Analyze(vge, vce, ic, FrameDt((int)a["vce_channel"]), other);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("开关参数（时间ns、能量uJ）：");
                    sb.Append("td(on)=" + r.TdOn.ToString("0.###") + "，tr=" + r.Tr.ToString("0.###") + "，ton=" + r.Ton.ToString("0.###") + "；");
                    sb.Append("td(off)=" + r.TdOff.ToString("0.###") + "，tf=" + r.Tf.ToString("0.###") + "，toff=" + r.Toff.ToString("0.###") + "，Tz=" + r.Tz.ToString("0.###") + "；");
                    sb.Append("dv/dt(on/off)=" + r.DvDtOn.ToString("0.###") + "/" + r.DvDtOff.ToString("0.###") + " kV/us，di/dt(on/off)=" + r.DiDtOn.ToString("0.###") + "/" + r.DiDtOff.ToString("0.###") + " A/us；");
                    sb.Append("Eon=" + r.Eon.ToString("0.###") + "，Eoff=" + r.Eoff.ToString("0.###") + " uJ；");
                    sb.Append("Vdsm=" + r.Vdsm.ToString("0.###") + "V，ΔVce=" + r.DeltaVce.ToString("0.###") + "V，Ls=" + r.Ls.ToString("0.###") + "nH，Pmax=" + r.Pmax.ToString("0.###") + "kW");
                    if (other != null) sb.Append("，串扰尖峰+/" + r.CrosstalkVmax.ToString("0.###") + "V，-/" + r.CrosstalkVmin.ToString("0.###") + "V");
                    if (!string.IsNullOrEmpty(r.Message)) sb.Append("。提示：" + r.Message);
                    return sb.ToString();
                },
                validate: All(ChKey("vge_channel"), ChKey("vce_channel"), ChKey("ic_channel")));

            Add("analyze_recovery", "从已采集波形中提取反向恢复参数：Irr反向恢复电流峰值、Trr反向恢复时间、Vrr反向电压峰值、dv/dt、di/dt、Err反向恢复能量、Qrr反向恢复电荷、Pdmax峰值功率。适用于双脉冲第二个脉冲开通沿（续流二极管/体二极管换流）。vge_channel=驱动脉冲通道，vce_channel=器件电压通道，ic_channel=器件电流通道。Qrr大说明续流二极管恢复特性差，会增加开通损耗和振铃。",
                new[] { "vge_channel", "vce_channel", "ic_channel" },
                new JObject
                {
                    ["vge_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vge驱动脉冲通道1~4" },
                    ["vce_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vce器件电压通道1~4" },
                    ["ic_channel"] = new JObject { ["type"] = "integer", ["description"] = "Ic器件电流通道1~4" }
                },
                async a =>
                {
                    double[] vge = FrameArr((int)a["vge_channel"]);
                    double[] vce = FrameArr((int)a["vce_channel"]);
                    double[] ic = FrameArr((int)a["ic_channel"]);
                    if (vge == null || vce == null || ic == null)
                        return "通道数据不足：请先在主界面让示波器采到波形（深度保存或实时曲线），并确认所选通道有信号";
                    DptRecovery.Result r = DptRecovery.Analyze(vge, vce, ic, FrameDt((int)a["vce_channel"]));
                    StringBuilder sb = new StringBuilder();
                    sb.Append("反向恢复参数：Irr=" + r.Irr.ToString("0.###") + "A，Trr=" + r.Trr.ToString("0.###") + "ns，Vrr=" + r.Vrr.ToString("0.###") + "V；");
                    sb.Append("dv/dt=" + r.DvDt.ToString("0.###") + "kV/us，di/dt=" + r.DiDt.ToString("0.###") + "A/us；");
                    sb.Append("Err=" + r.Err.ToString("0.###") + "uJ，Qrr=" + r.Qrr.ToString("0.###") + "uC，Pdmax=" + r.Pdmax.ToString("0.###") + "kW");
                    if (!string.IsNullOrEmpty(r.Message)) sb.Append("。提示：" + r.Message);
                    return sb.ToString();
                },
                validate: All(ChKey("vge_channel"), ChKey("vce_channel"), ChKey("ic_channel")));

            Add("analyze_gate_charge", "从已采集波形中提取栅极电荷参数：Qg总栅极电荷、Qgs栅源电荷、Qgd栅漏（米勒）电荷、Vgp米勒平台电压。需要恒流源驱动测试的波形：vge_channel=栅压通道，ig_channel=驱动恒流通道。Qg决定驱动损耗（P=Qg×Vg×开关频率），Qgd决定开关速度。",
                new[] { "vge_channel", "ig_channel" },
                new JObject
                {
                    ["vge_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vge栅压通道1~4" },
                    ["ig_channel"] = new JObject { ["type"] = "integer", ["description"] = "Ig恒流驱动电流通道1~4" }
                },
                async a =>
                {
                    double[] vge = FrameArr((int)a["vge_channel"]);
                    double[] ig = FrameArr((int)a["ig_channel"]);
                    if (vge == null || ig == null)
                        return "通道数据不足：栅极电荷测试需要 Vge 与 Ig（恒流驱动电流）两路波形都已采集";
                    DptGateCharge.Result r = DptGateCharge.Analyze(vge, ig, FrameDt((int)a["vge_channel"]));
                    string s = "栅极电荷：Qg=" + r.Qg.ToString("0.###") + "nC，Qgs=" + r.Qgs.ToString("0.###") + "nC，Qgd=" + r.Qgd.ToString("0.###") + "nC，Vgp米勒平台=" + r.Vgp.ToString("0.###") + "V";
                    if (!string.IsNullOrEmpty(r.Message)) s += "。提示：" + r.Message;
                    return s;
                },
                validate: All(ChKey("vge_channel"), ChKey("ig_channel")));

            Add("analyze_avalanche", "从已采集波形中提取雪崩（UIS）参数：IAS雪崩电流峰值、EAS雪崩能量、TAV雪崩持续时间、VAV雪崩电压平台、di/dt关断电流斜率。vce_channel=器件电压通道，ic_channel=器件电流通道。雪崩区间自动识别为Vds超过母线电压20%的段落。EAS应与手册额定值按温度降额后对比。",
                new[] { "vce_channel", "ic_channel" },
                new JObject
                {
                    ["vce_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vce器件电压通道1~4" },
                    ["ic_channel"] = new JObject { ["type"] = "integer", ["description"] = "Ic器件电流通道1~4" }
                },
                async a =>
                {
                    double[] vce = FrameArr((int)a["vce_channel"]);
                    double[] ic = FrameArr((int)a["ic_channel"]);
                    if (vce == null || ic == null)
                        return "通道数据不足：UIS雪崩测试需要 Vce 与 Ic 两路波形都已采集";
                    DptAvalanche.Result r = DptAvalanche.Analyze(vce, ic, FrameDt((int)a["vce_channel"]));
                    string s = "雪崩参数：IAS=" + r.Ias.ToString("0.###") + "A，EAS=" + r.Eas.ToString("0.###") + "mJ，TAV=" + r.Tav.ToString("0.###") + "us，VAV=" + r.Vav.ToString("0.###") + "V，di/dt=" + r.DiDt.ToString("0.###") + "A/us";
                    if (!string.IsNullOrEmpty(r.Message)) s += "。提示：" + r.Message;
                    return s;
                },
                validate: All(ChKey("vce_channel"), ChKey("ic_channel")));

            Add("analyze_short_circuit", "从已采集波形中提取短路（SC）参数：Imax短路电流峰值、Tsc短路耐受时间、Esc短路能量、Vpeak应力电压、Desat退饱和时间。vce_channel=器件电压通道，ic_channel=器件电流通道。判读：Desat检测时间应远小于Tsc（IGBT要求10us内关断，SiC要求2~3us内），Vce异常跌落说明器件已局部损坏。",
                new[] { "vce_channel", "ic_channel" },
                new JObject
                {
                    ["vce_channel"] = new JObject { ["type"] = "integer", ["description"] = "Vce器件电压通道1~4" },
                    ["ic_channel"] = new JObject { ["type"] = "integer", ["description"] = "Ic器件电流通道1~4" }
                },
                async a =>
                {
                    double[] vce = FrameArr((int)a["vce_channel"]);
                    double[] ic = FrameArr((int)a["ic_channel"]);
                    if (vce == null || ic == null)
                        return "通道数据不足：短路测试需要 Vce 与 Ic 两路波形都已采集";
                    DptShortCircuit.Result r = DptShortCircuit.Analyze(vce, ic, FrameDt((int)a["vce_channel"]));
                    string s = "短路参数：Imax=" + r.Imax.ToString("0.###") + "A，Tsc=" + r.Tsc.ToString("0.###") + "us，Esc=" + r.Esc.ToString("0.###") + "mJ，Vpeak=" + r.Vpeak.ToString("0.###") + "V，Desat时间=" + r.DesatTime.ToString("0.###") + "us";
                    if (!string.IsNullOrEmpty(r.Message)) s += "。提示：" + r.Message;
                    return s;
                },
                validate: All(ChKey("vce_channel"), ChKey("ic_channel")));


            //============ 示波器 ============
            Add("scope_get_idn", "查询示波器型号（连接体检用）。", new string[0], null,
                async a => await Scope.GetIDN() ?? "无响应");

            Add("scope_run", "启动示波器采集（等同按RUN键）。", new string[0], null,
                async a => { Scope.Start(); return "已发送RUN"; });

            Add("scope_stop", "停止示波器采集（等同按STOP键）。", new string[0], null,
                async a => { Scope.Stop(); return "已发送STOP"; });

            Add("scope_single", "单次触发（等同按SINGLE键），抓一帧就停。", new string[0], null,
                async a => { Scope.Single(); return "已发送SINGLE"; });

            Add("scope_autoset", "自动设置（AUTOSET），让示波器自动调档位和触发。", new string[0], null,
                async a => { Scope.Auto(); return "已发送AUTOSET"; });

            Add("scope_set_time_scale", "设置主时基档位（每格时间），单位秒。如2微秒=0.000002。",
                new[] { "seconds" }, P("seconds", "number", "每格秒数"),
                async a => { Scope.TimeScale(((double)a["seconds"]).ToString()); return "已设置时基"; },
                validate: Range("seconds", 1e-9, 1000, "秒"));

            Add("scope_set_time_offset", "设置主时基偏移（波形起点相对触发点的时间），单位秒，可正可负。",
                new[] { "seconds" }, P("seconds", "number", "偏移秒数"),
                async a => { Scope.TimeOffset(((double)a["seconds"]).ToString()); return "已设置时基偏移"; },
                validate: Range("seconds", -1000, 1000, "秒"));

            Add("scope_set_channel_scale", "设置指定通道垂直档位（伏/格）。",
                new[] { "channel", "volts" },
                new JObject
                {
                    ["channel"] = new JObject { ["type"] = "integer", ["description"] = "通道号1~4" },
                    ["volts"] = new JObject { ["type"] = "number", ["description"] = "伏/格" }
                },
                async a => { Scope.ChanScale("CHANnel" + (int)a["channel"], ((double)a["volts"]).ToString()); return "已设置垂直档位"; },
                validate: All(Ch14, Range("volts", 0.0005, 100, "V/格")));

            Add("scope_set_channel_offset", "设置指定通道垂直偏移，单位伏。",
                new[] { "channel", "volts" },
                new JObject
                {
                    ["channel"] = new JObject { ["type"] = "integer", ["description"] = "通道号1~4" },
                    ["volts"] = new JObject { ["type"] = "number", ["description"] = "偏移伏数" }
                },
                async a => { Scope.ChanOffset("CHANnel" + (int)a["channel"], ((double)a["volts"]).ToString()); return "已设置垂直偏移"; },
                validate: All(Ch14, Range("volts", -100, 100, "V")));

            Add("scope_measure", "测量指定通道的波形参数。item常用值：VPP峰峰值、VMAX最大值、VMIN最小值、VAVG平均值、VRMS有效值、FREQuency频率、PERiod周期、RISe上升时间、FALL下降时间、PDUTy正占空比、NDUTy负占空比、OVERshoot过冲。返回科学计数字符串。",
                new[] { "item", "channel" },
                new JObject
                {
                    ["item"] = new JObject { ["type"] = "string", ["description"] = "测量项，如VPP、FREQuency" },
                    ["channel"] = new JObject { ["type"] = "integer", ["description"] = "通道号1~4" }
                },
                async a => await Scope.MeasureItemValue((string)a["item"], "CHANnel" + (int)a["channel"]) ?? "测量无响应（检查示波器是否连接、通道是否开启）",
                validate: Ch14);

            Add("scope_trigger_sweep", "设置触发方式：AUTO自动、NORMal普通、SINGle单次。",
                new[] { "sweep" }, P("sweep", "string", "AUTO/NORMal/SINGle"),
                async a => { Scope.TriggerSweep((string)a["sweep"]); return "已设置触发方式"; });

            Add("scope_trigger_level", "设置触发电平，单位伏。",
                new[] { "volts" }, P("volts", "number", "触发电平V"),
                async a => { Scope.TriggerLevel((double)a["volts"]); return "已设置触发电平"; },
                validate: Range("volts", -100, 100, "V"));

            Add("scope_trigger_status", "查询触发状态（TD已触发/WAIT等待/RUN运行/AUTO/STOP）。", new string[0], null,
                async a => await Scope.GetTriggerStatus() ?? "无响应");

            Add("scope_save_image_usb", "把当前屏幕截图存到示波器U盘，path如\"D:/123.png\"。",
                new[] { "path" }, P("path", "string", "U盘上的保存路径，含文件名"),
                async a => { Scope.SaveImage((string)a["path"]); return "已发送截图指令"; });

            //============ N36200电源 ============
            Add("power_get_status", "查询电源状态字：Bit0输出开关、Bit1告警、Bit2 CV/CC(0=CV,1=CC)。返回十进制数。",
                new string[0], null,
                async a =>
                {
                    uint? s = await Power.GetDeviceStatus();
                    return s.HasValue ? ("状态字=" + s.Value + "（Bit0输出=" + ((s.Value & 1) == 1 ? "ON" : "OFF") + "，Bit2=" + (((s.Value >> 2) & 1) == 1 ? "CC" : "CV") + "）") : "无响应";
                });

            Add("power_get_readback", "获取最近一次轮询到的电源输出电压/电流回读值（约200ms更新一次）及回读功率、温度查询。",
                new string[0], null,
                async a =>
                {
                    float? p = await Power.GetBackPower();
                    float? t = await Power.GetBackTemperature();
                    StringBuilder sb = new StringBuilder();
                    sb.Append("最近回读：电压=" + LastPowerVoltage.ToString("0.###") + "V，电流=" + LastPowerCurrent.ToString("0.###") + "A");
                    if (p.HasValue) sb.Append("，功率=" + p.Value.ToString("0.###") + "W");
                    if (t.HasValue) sb.Append("，温度=" + t.Value.ToString("0.###") + "℃");
                    return sb.ToString();
                });

            Add("power_set_voltage", "设置电源输出电压，单位伏。",
                new[] { "volts" }, P("volts", "number", "电压V"),
                async a => { Power.SetValue((float)(double)a["volts"]); return "已设置电压"; },
                validate: Range("volts", 0, PowerMaxVolts, "V"));

            Add("power_set_current", "设置电源输出电流（限流值），单位安。",
                new[] { "amps" }, P("amps", "number", "电流A"),
                async a => { Power.SetCurrent((float)(double)a["amps"]); return "已设置电流"; },
                validate: Range("amps", 0, PowerMaxAmps, "A"));

            Add("power_set_ovp", "设置过压保护OVP值，单位伏。",
                new[] { "volts" }, P("volts", "number", "过压保护V"),
                async a => { Power.SetOverLoadVolatage((float)(double)a["volts"]); return "已设置过压保护"; },
                validate: Range("volts", 0, PowerMaxVolts, "V"));

            Add("power_set_ocp", "设置过流保护OCP值，单位安。",
                new[] { "amps" }, P("amps", "number", "过流保护A"),
                async a => { Power.SetOverLoadCurrent((float)(double)a["amps"]); return "已设置过流保护"; },
                validate: Range("amps", 0, PowerMaxAmps, "A"));

            Add("power_set_output", "打开或关闭电源输出。这是危险操作，执行前会向操作员确认。",
                new[] { "on" }, P("on", "boolean", "true开/false关"),
                async a => { Power.SetOutput((bool)a["on"]); return ((bool)a["on"] ? "已打开输出" : "已关闭输出"); },
                dangerous: true);

            Add("power_factory_reset", "电源恢复出厂设置。危险操作，执行前会向操作员确认。",
                new string[0], null,
                async a => { Power.FactoryReset(); return "已发送恢复出厂"; },
                dangerous: true);

            //============ 逃生门 ============
            Add("scope_send_raw", "向示波器发送任意原始SCPI指令（自动补换行）。仅在现有工具都满足不了时使用，发送前会向操作员确认。",
                new[] { "command" }, P("command", "string", "完整SCPI指令，如:TIMebase:MAIN:SCALe 0.001"),
                async a => { Scope.SendCmd((string)a["command"]); return "已发送：" + (string)a["command"]; },
                dangerous: true);
        }
    }
}
