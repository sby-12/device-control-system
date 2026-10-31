using System;
using System.Globalization;
using System.Threading.Tasks;
using InstrumentInterfaceLibrary;

namespace Sdg6000xLibrary
{
    /// <summary>
    /// SIGLENT SDG6000X 信号源适配类（方案A·接口隔离）。
    /// SCPI 方言见《SDG6000X信号源编程手册》。
    ///
    /// 已实现：连接/关闭、输出幅度(C1:BSWV AMP/OFST)、双脉冲输出
    ///         （C1:MultiPulse:*，仪器自带双脉冲功能，比原机型拼波形干净得多）、
    ///         手动触发(C1:MultiPulse:Trigger)。
    /// 未实现（见《未实现清单》）：任意波形下载（DownloadWave/波形下载按钮）。
    ///
    /// 2026-10-30 最小可用版。
    /// 单位约定：OutputOneDifferentPulses / OutputTwoDifferentPulses 的宽度与间隔参数
    ///           按接口统一约定传"微秒"（2026-10-31 修改，原来是纳秒），本类内部换算成秒发送。
    /// </summary>
    public class Sdg6000xSignal : ISignalSource
    {
        public ScpiTcp tcp = new ScpiTcp();

        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "SIGLENT SDG6000X";

        //脉冲之间的"休息"宽度：给 1 秒，保证负载和电源有充分恢复时间
        private const double LongOffSeconds = 1.0;

        //===== 连接 =====
        public void OpenTranslate(string host, int port)
        {
            tcp.Connect(host, port);
        }

        public void CloseTranslate()
        {
            tcp.Disconnect();
        }

        //===== 输出幅度 =====
        /// <summary>幅度设置：AMP=Vpp，OFST=偏置（与原机型 ValueSet 的 VOLT/OFFS 同口径，Vpp 与 V/2）。</summary>
        public void ValueSet(byte value)
        {
            tcp.Send("C1:BSWV AMP," + value.ToString(CultureInfo.InvariantCulture));
            tcp.Send("C1:BSWV OFST," + (value / 2.0).ToString("0.###", CultureInfo.InvariantCulture));
        }

        //===== 波形下载：暂未实现 =====
        public void DownloadWave(int ch, double[] data)
        {
            throw new NotSupportedException("SDG6000X 任意波形下载（ARWV/二进制块传输）暂未实现");
        }

        /// <summary>Apply 对应原机型的"触发一次脉冲"语义：SDG 用 MultiPulse 手动触发。</summary>
        public void Apply()
        {
            tcp.Send("C1:MultiPulse:Trigger");
        }

        //===== 脉冲输出（双脉冲测试入口）=====
        /// <summary>输出单个脉冲。pulse1Width 单位：微秒（接口约定）。</summary>
        public void OutputOneDifferentPulses(double pulse1Width)
        {
            double w1 = UsToSec(pulse1Width);
            tcp.Send("C1:BSWV WVTP,MULTPULSE");
            tcp.Send("C1:MultiPulse:Source MAN");
            tcp.Send("C1:MultiPulse:Count 1");
            tcp.Send("C1:MultiPulse:Item1:PosWidth " + Fmt(w1));
            tcp.Send("C1:MultiPulse:Item1:NegWidth " + Fmt(LongOffSeconds));
            tcp.Send("C1:OUTP ON");
            tcp.Send("C1:MultiPulse:Trigger");
        }

        /// <summary>
        /// 输出双脉冲：第一个脉冲宽度、第二个脉冲宽度、两脉冲间隔（顺序与接口定义一致）。
        /// 三个参数单位均为微秒（接口约定）。
        /// </summary>
        public void OutputTwoDifferentPulses(double pulse1Width, double pulse2Width, double interval)
        {
            double w1 = UsToSec(pulse1Width);
            double w2 = UsToSec(pulse2Width);
            double gap = UsToSec(interval);
            tcp.Send("C1:BSWV WVTP,MULTPULSE");
            tcp.Send("C1:MultiPulse:Source MAN");
            tcp.Send("C1:MultiPulse:Count 2");
            tcp.Send("C1:MultiPulse:Item1:PosWidth " + Fmt(w1));
            tcp.Send("C1:MultiPulse:Item1:NegWidth " + Fmt(gap));
            tcp.Send("C1:MultiPulse:Item2:PosWidth " + Fmt(w2));
            tcp.Send("C1:MultiPulse:Item2:NegWidth " + Fmt(LongOffSeconds));
            tcp.Send("C1:OUTP ON");
            tcp.Send("C1:MultiPulse:Trigger");
        }

        //===== 内部工具 =====
        private static double UsToSec(double us)
        {
            return us * 1e-6;
        }

        /// <summary>数值格式化为仪器爱吃的英文小数点格式（不受系统区域设置影响）。</summary>
        private static string Fmt(double seconds)
        {
            return seconds.ToString("0.#########", CultureInfo.InvariantCulture);
        }
    }
}
