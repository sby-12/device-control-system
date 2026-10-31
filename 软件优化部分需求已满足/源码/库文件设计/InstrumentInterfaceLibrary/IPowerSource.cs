using System.Threading.Tasks;

namespace InstrumentInterfaceLibrary
{
    /// <summary>
    /// 可编程直流电源统一接口。NGI N36200、N2410、ITECH IT16000D 都实现它。
    /// 不支持的成员（如 N2410 没有过压保护）由实现类抛 NotSupportedException。
    /// </summary>
    public interface IPowerSource
    {
        /// <summary>机型显示名。</summary>
        string DeviceName { get; }

        //===== 连接 =====
        void OpenTranslate(string host, int port);
        void CloseTranslate();

        //===== 输出设置 =====
        void SetValue(float volts);            //输出电压设定
        void SetCurrent(float amps);           //输出电流/限流设定
        void SetOverLoadCurrent(float amps);   //过流保护 OCP
        void SetOverLoadVolatage(float volts); //过压保护 OVP
        void SetOutput(bool on);               //输出开关（开=true 危险操作，UI层做确认）

        //===== 状态回读 =====
        Task<uint?> GetDeviceStatus();         //状态字：Bit0输出/Bit1告警/Bit2 CV/CC
        Task<float?> GetBackPower();           //回读功率
        Task<float?> GetBackTemperature();     //回读温度

        //===== 维护 =====
        void FactoryReset();                   //恢复出厂（危险操作）
    }
}
