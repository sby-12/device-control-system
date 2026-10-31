using System;
using System.Threading.Tasks;
using InstrumentInterfaceLibrary;

namespace It16000dLibrary
{
    /// <summary>
    /// ITECH IT16000D 系列大功率直流电源适配类（方案A·接口隔离）。
    /// 标准 SCPI 方言（见《IT16000D电源编程手册》），比 NGI 的 Modbus-RTU 简单得多。
    ///
    /// 已实现：连接/远程模式、电压设定(VOLT)、限流(CURR)、过压保护(VOLT:PROT)、
    ///         过流保护(CURR:PROT)、输出开关(OUTP)、状态字(STAT:OPER:COND?)、
    ///         功率回读(MEAS:POW?)、出厂复位(*RST)。
    /// 未实现（见《未实现清单》）：温度回读（手册无此命令）。
    /// </summary>
    public class It16000dPower : IPowerSource
    {
        public ScpiTcp tcp = new ScpiTcp();

        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "ITECH IT16000D";

        //===== 连接 =====
        public void OpenTranslate(string host, int port)
        {
            tcp.Connect(host, port);
            try { tcp.Send("SYST:REM"); } catch { }   //进远程控制模式，防止面板操作干扰
        }

        public void CloseTranslate()
        {
            tcp.Disconnect();
        }

        //===== 输出设置 =====
        public void SetValue(float volts)
        {
            tcp.Send("VOLT " + volts.ToString("0.###"));
        }

        public void SetCurrent(float amps)
        {
            tcp.Send("CURR " + amps.ToString("0.###"));
        }

        public void SetOverLoadCurrent(float amps)
        {
            //手册全称 [SOURce:]CURRent[:OVER]:PROTection[:LEVel]，缩写形式即可
            tcp.Send("CURR:PROT " + amps.ToString("0.###"));
        }

        public void SetOverLoadVolatage(float volts)
        {
            tcp.Send("VOLT:PROT " + volts.ToString("0.###"));
        }

        public void SetOutput(bool on)
        {
            tcp.Send(on ? "OUTP ON" : "OUTP OFF");
        }

        //===== 状态回读 =====
        /// <summary>读操作状态寄存器（STATus:OPERation:CONDition?），解析成无符号整数。</summary>
        public async Task<uint?> GetDeviceStatus()
        {
            string ans = await tcp.QueryAsync("STAT:OPER:COND?", 5000);
            if (string.IsNullOrWhiteSpace(ans)) return null;
            //应答可能是十进制，也可能带 0x 前缀，都试一遍
            if (uint.TryParse(ans, out uint v)) return v;
            if (ans.StartsWith("#H") || ans.StartsWith("#h"))
            {
                if (uint.TryParse(ans.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v)) return v;
            }
            //2026-10-31 修复12：注释里说的 0x 前缀原来没有对应处理分支，补上
            if (ans.StartsWith("0x") || ans.StartsWith("0X"))
            {
                if (uint.TryParse(ans.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v)) return v;
            }
            return null;
        }

        /// <summary>回读输出功率（MEASure[:SCALar]:POWer[:DC]?），单位瓦。</summary>
        public async Task<float?> GetBackPower()
        {
            string ans = await tcp.QueryAsync("MEAS:POW?", 5000);
            if (string.IsNullOrWhiteSpace(ans)) return null;
            if (float.TryParse(ans, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float p)) return p;
            if (float.TryParse(ans, out p)) return p;   //本机区域设置兜底
            return null;
        }

        /// <summary>手册中无温度回读命令，明确拒绝。</summary>
        public Task<float?> GetBackTemperature()
        {
            throw new NotSupportedException("IT16000D 手册无温度回读命令");
        }

        //===== 维护 =====
        public void FactoryReset()
        {
            tcp.Send("*RST");
        }
    }
}
