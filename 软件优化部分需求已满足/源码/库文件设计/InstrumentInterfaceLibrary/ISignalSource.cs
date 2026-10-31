namespace InstrumentInterfaceLibrary
{
    /// <summary>
    /// 信号源统一接口。现有信号源与 SIGLENT SDG6000X 都实现它。
    /// 双脉冲测试的核心入口是 OutputOneDifferentPulses / OutputTwoDifferentPulses。
    /// </summary>
    public interface ISignalSource
    {
        /// <summary>机型显示名。</summary>
        string DeviceName { get; }

        //===== 连接 =====
        void OpenTranslate(string host, int port);
        void CloseTranslate();

        //===== 输出 =====
        void ValueSet(byte value);   //输出幅度设置

        //===== 波形下载 =====
        void DownloadWave(int ch, double[] data);   //下载任意波形
        void Apply();                               //应用已下载的波形

        //===== 脉冲输出（双脉冲测试入口）=====
        //2026-10-31 单位统一为微秒（µs，double）：接口约定物理时间，主窗口把界面输入原值直传，
        //各机型适配类内部自行换算（RIGOL 换算成任意波点数，SDG 换算成秒），换设备时主窗口零改动。
        /// <summary>输出单个脉冲。pulse1Width 单位：微秒。</summary>
        void OutputOneDifferentPulses(double pulse1Width);
        /// <summary>输出双脉冲：第一个脉冲宽度、第二个脉冲宽度、两脉冲间隔（顺序与原信号源类一致），单位均为微秒。</summary>
        void OutputTwoDifferentPulses(double pulse1Width, double pulse2Width, double interval);
    }
}
