using System.Threading.Tasks;

namespace InstrumentInterfaceLibrary
{
    /// <summary>
    /// 示波器统一接口。任何品牌的示波器适配类都实现它，
    /// 主窗口只认 IScope，换示波器 = 新增一个实现类，主程序不改。
    /// 2026-10-30 从 RIGOL MHO984 的实际用法中抽出（方案A·接口隔离）。
    /// </summary>
    public interface IScope
    {
        /// <summary>机型显示名（如 "RIGOL MHO984"、"Teledyne LeCroy"）。</summary>
        string DeviceName { get; }

        //===== 连接 =====
        void OpenTranslate(string host, int port);
        void CloseTranslate();
        Task<string> GetIDN();   //*IDN? 查询型号，连接体检用

        //===== 采集控制 =====
        void Start();    //RUN
        void Stop();     //STOP
        void Single();   //单次触发
        void Auto();     //自动设置

        //===== 时基与通道 =====
        void TimeScale(string sec);     //主时基档位（秒/格）
        void TimeOffset(string sec);    //主时基偏移（秒）
        void ChanScale(string ch, string val);    //垂直档位，ch 如 "CHANnel1"
        void ChanOffset(string ch, string val);   //垂直偏移
        void ChanImpedance(string ch, string imp); //阻抗，取值各品牌有方言（OMEG/FIFTy…）
        void ChanProbe(string ch, string att);     //探头倍率

        //===== 触发 =====
        void TriggerMode(bool upTriggMode);  //true上升沿/false下降沿
        void TriggerSource(string src);      //"CHAN1"~"CHAN4"
        void TriggerLevel(double volts);
        void TriggerSweep(string sweep);     //AUTO/NORMal/SINGle
        Task<string> GetTriggerStatus();     //TD/WAIT/RUN/STOP…

        //===== 测量与杂项 =====
        Task<string> MeasureItemValue(string item, string ch);  //VPP/FREQuency…
        void SaveimageToUsb();   //屏幕截图存示波器U盘
        void SaveImage(string path);
        void SendCmd(string scpiCmd);            //逃生门：原始指令
        Task<string> Query(string scpiCmd, int timeoutMs = 5000);

        //===== 数据轮询（波形数据回读的全局开关与通道状态）=====
        /// <summary>总开关：true=软件轮询读波形数据。</summary>
        bool DataReadEnabled { get; set; }
        /// <summary>通道使能掩码：Bit0~Bit3 对应 CH1~CH4。</summary>
        byte ChannelMask { get; set; }
        /// <summary>当前轮询到哪个通道（0~3）。</summary>
        int ReadChannelIndex { get; set; }

        //===== 深存储（可选能力）=====
        /// <summary>本机型是否支持深度保存。不支持时主程序退回普通快照并隐藏相关按钮。</summary>
        bool SupportDeepSave { get; }
        /// <summary>深存储收集模式开关（等价原 DeepSaveMode 字段）。</summary>
        bool DeepSaveActive { get; set; }
        /// <summary>本次深存储实际生效点数（回读自仪器）。</summary>
        int DeepSavePointsApplied { get; }
        void BeginDeepSave(int points);
        void EndDeepSave();
    }
}
