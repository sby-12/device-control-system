using System;
using InstrumentInterfaceLibrary;
using ValueSourceN2410ClassLibrary;
using OscilloscopeClassLibrary;
using SingalClassLibrary;
using LeCroyScopeLibrary;
using It16000dLibrary;
using Sdg6000xLibrary;

namespace MainSoftwareV100
{
    /// <summary>
    /// 设备工厂（方案A·接口隔离的第二块拼图）。
    /// 连接弹窗选好机型后，由这里 new 出对应的适配类。
    /// 主窗口只认 IScope/IPowerSource/ISignalSource 三个接口，
    /// 将来再换品牌：在这里加一个 case + 下拉框加一行，主窗口一行不改。
    ///
    /// 2026-10-30 建立。机型字符串同时用于：下拉框显示、存档 txt、这里匹配，
    /// 所以新增机型时务必三处一起加，且字符串不要改（改了会导致旧存档匹配不上）。
    /// </summary>
    public static class DeviceFactory
    {
        //===== 电源机型 =====
        public const string POWER_N2410   = "NGI N2410";
        public const string POWER_N36200  = "NGI N36200";
        public const string POWER_IT16000D = "ITECH IT16000D";

        //===== 示波器机型 =====
        public const string SCOPE_RIGOL  = "RIGOL MHO984";
        public const string SCOPE_LECROY = "Teledyne LeCroy";

        //===== 信号源机型 =====
        public const string SIGNAL_OLD     = "信号源（原机型）";
        public const string SIGNAL_SDG6000X = "SIGLENT SDG6000X";

        /// <summary>下拉框选项列表（顺序即显示顺序）。</summary>
        public static readonly string[] PowerModels  = { POWER_N2410, POWER_N36200, POWER_IT16000D };
        public static readonly string[] ScopeModels  = { SCOPE_RIGOL, SCOPE_LECROY };
        public static readonly string[] SignalModels = { SIGNAL_OLD, SIGNAL_SDG6000X };

        /// <summary>
        /// 创建一个电源适配类。
        /// modbusAddress：NGI 电源走 Modbus-RTU 需要站号，约定 A 槽=1、B 槽=2；
        /// ITECH 是 SCPI 不用站号，传了也忽略。
        /// </summary>
        public static IPowerSource CreatePower(string model, byte modbusAddress)
        {
            switch (model)
            {
                case POWER_N2410:    return new N2410Class(modbusAddress);
                case POWER_N36200:   return new N36200Class(modbusAddress);
                case POWER_IT16000D: return new It16000dPower();
                default:
                    throw new ArgumentException("未知电源机型：" + model);
            }
        }

        public static IScope CreateScope(string model)
        {
            switch (model)
            {
                case SCOPE_RIGOL:  return new OscilloscopeClass();
                case SCOPE_LECROY: return new LeCroyScope();
                default:
                    throw new ArgumentException("未知示波器机型：" + model);
            }
        }

        public static ISignalSource CreateSignal(string model)
        {
            switch (model)
            {
                case SIGNAL_OLD:      return new SignalClass();
                case SIGNAL_SDG6000X: return new Sdg6000xSignal();
                default:
                    throw new ArgumentException("未知信号源机型：" + model);
            }
        }
    }
}
