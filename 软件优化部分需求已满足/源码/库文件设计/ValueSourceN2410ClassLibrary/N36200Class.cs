using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using InstrumentInterfaceLibrary;

namespace ValueSourceN2410ClassLibrary
{
    //2026-10-30 实现 IPowerSource 统一接口（方案A·接口隔离），方法体一行未动
    public class N36200Class : IPowerSource
    {
        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "NGI N36200";

        #region 变量定义
        public TcpTranslateClass translateClass;
        public PublicToolClass publicToolClass;
        Timer ThisEquitRequestTime = null;
        Byte EquipMent = 1;
        #endregion
        public N36200Class(Byte EquipMentAddress)
        {
            EquipMent = EquipMentAddress;
            translateClass = new TcpTranslateClass(EquipMent);
            publicToolClass = new PublicToolClass();
            ThisEquitRequestTime = new Timer(200);//20ms进入一次
            ThisEquitRequestTime.Elapsed += new System.Timers.ElapsedEventHandler(ThisEquitRequestTime_Event);
            ThisEquitRequestTime.AutoReset = true;
            ThisEquitRequestTime.Enabled = false;
     
        }
        //通信连接
        public void OpenTranslate(string host, int port)
        {
            //2026-10-31 修复3：轮询定时器会向电源发 Modbus 查询，必须等 TCP 真正建链后再启动，
            //否则连接失败时定时器空转、指令全被静默丢弃。挂到 AfterConnected，连接成功时启动。
            translateClass.AfterConnected = () => ThisEquitRequestTime.Start();
            _ = translateClass.ConnectAsync(host, port);
        }

        //通信关闭
        public void CloseTranslate()
        {
            ThisEquitRequestTime.Stop();
            translateClass.CloseAsync();
        }

        //模式设置为普通模式
        public void SetChannelMode()
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x16;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            SendData[7] = 0x00;
            SendData[8] = 0x00;
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //设置为CV优先
        public void SetChannelCvOrCc()
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x1A;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            SendData[7] = 0x01;
            SendData[8] = 0x00;
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //打开关闭通道
        public void SetChannelOpenOrOff(bool Open)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x14;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            SendData[8] = 0x00;
            if (Open)
            {
                SendData[8] = 0x01;
            }
            else
            {
                SendData[8] = 0x00;
            }
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //电压设置
        public void SetValue(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x28;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            byte[] b_value = publicToolClass.FloatToBigEndianByte(value);
            SendData[7] = b_value[0];
            SendData[8] = b_value[1];
            SendData[9] = b_value[2];
            SendData[10] = b_value[3];
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //电流设置
        public void SetCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x2A;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            byte[] b_value = publicToolClass.FloatToBigEndianByte(value);
            SendData[7] = b_value[0];
            SendData[8] = b_value[1];
            SendData[9] = b_value[2];
            SendData[10] = b_value[3];
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //电流保护设置（过流保护OCP）
        //手册5.8.6：202(0xCA)=过流保护
        public void SetOverLoadCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0xCA;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            byte[] b_value = publicToolClass.FloatToBigEndianByte(value);
            SendData[7] = b_value[0];
            SendData[8] = b_value[1];
            SendData[9] = b_value[2];
            SendData[10] = b_value[3];
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            Console.WriteLine($"[N36200] 过流保护(OCP)发送: 寄存器0x{SendData[2]:X2}{SendData[3]:X2}, 值={value}A");
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //电压保护设置（过压保护OVP）
        //手册5.8.4：200(0xC8)=过压保护
        public void SetOverLoadVolatage(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0xC8;
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            byte[] b_value = publicToolClass.FloatToBigEndianByte(value);
            SendData[7] = b_value[0];
            SendData[8] = b_value[1];
            SendData[9] = b_value[2];
            SendData[10] = b_value[3];
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            Console.WriteLine($"[N36200] 过压保护(OVP)发送: 寄存器0x{SendData[2]:X2}{SendData[3]:X2}, 值={value}V");
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //=====================================================
        //以下各区为2026-10-17新增：把《N36200.N36300 ModbusRTU通讯协议》第5章寄存器表封装为函数
        //地址来自协议手册5.x节；寄存器值含义以手册正文参数说明为准
        //=====================================================

        #region 写寄存器公共工具（手册4.2的0x10功能码帧）

        //写单个寄存器：组帧与手册4.2一致（写2个寄存器长度、数据4字节、CRC16），帧长13字节
        private void WriteRegister(ushort regAddr, byte[] data4)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = (byte)(regAddr >> 8);
            SendData[3] = (byte)(regAddr & 0xFF);
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            Array.Copy(data4, 0, SendData, 7, 4);
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            Console.WriteLine($"[N36200] 写寄存器 0x{regAddr:X4} = {BitConverter.ToString(data4)}");
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //写Float型寄存器（电压/电流/保护值这类带小数的设置）
        private void WriteFloatRegister(ushort regAddr, float value)
        {
            WriteRegister(regAddr, publicToolClass.FloatToBigEndianByte(value));
        }

        //写Uint32型寄存器（开关/模式/编号这类整数设置）
        private void WriteUintRegister(ushort regAddr, uint value)
        {
            WriteRegister(regAddr, publicToolClass.UInt32ToBigEndianByte(value));
        }

        #endregion
        #region 输出与工作模式（手册5.1.8、5.1.9、5.9.5）

        //输出开关（地址20）：true=开输出。SetChannelOpenOrOff的语义化别名，新代码建议用这个
        public void SetOutput(bool on)
        {
            WriteUintRegister(0x14, on ? 1u : 0u);
        }

        //V/I普通模式（地址22=0）
        public void SetModeNormal()
        {
            WriteUintRegister(0x16, 0u);
        }

        //SEQ序列模式（地址22=128）
        public void SetModeSequence()
        {
            WriteUintRegister(0x16, 128u);
        }

        //CP恒功率模式（地址22=127）
        public void SetModeConstantPower()
        {
            WriteUintRegister(0x16, 127u);
        }

        //CV优先（地址26=1），CC优先（地址26=2）
        public void SetCvPriority()
        {
            WriteUintRegister(0x1A, 1u);
        }
        public void SetCcPriority()
        {
            WriteUintRegister(0x1A, 2u);
        }

        #endregion
        #region 保护设置扩展（手册5.8）

        //过功率保护OPP（地址204=0xCC），单位W
        public void SetOverPowerProtection(float watts)
        {
            WriteFloatRegister(0xCC, watts);
        }

        //欠压保护UVP（地址198=0xC6），单位V
        public void SetUnderVoltageProtection(float volts)
        {
            WriteFloatRegister(0xC6, volts);
        }

        //过压保护延迟（地址224=0xE0），单位s
        public void SetOvpDelay(float seconds)
        {
            WriteFloatRegister(0xE0, seconds);
        }

        //过流保护延迟（地址226=0xE2），单位s
        public void SetOcpDelay(float seconds)
        {
            WriteFloatRegister(0xE2, seconds);
        }

        //过功率保护延迟（地址228=0xE4），单位s
        public void SetOppDelay(float seconds)
        {
            WriteFloatRegister(0xE4, seconds);
        }

        //欠压保护延迟（地址222=0xDE），单位s
        public void SetUvpDelay(float seconds)
        {
            WriteFloatRegister(0xDE, seconds);
        }

        //折返保护（地址1060=0x424）：0=关、1=CV、2=CC
        public void SetFoldbackProtection(uint mode)
        {
            WriteUintRegister(0x424, mode);
        }

        #endregion
        #region 极限与系统设置（手册5.6、5.9.6、5.9.8、5.12.10）

        //最大电压（地址48=0x30），单位V
        public void SetMaxVoltage(float volts)
        {
            WriteFloatRegister(0x30, volts);
        }

        //最大电流（地址50=0x32），单位A
        public void SetMaxCurrent(float amps)
        {
            WriteFloatRegister(0x32, amps);
        }

        //功率设置（地址52=0x34），单位W
        public void SetPowerLimit(float watts)
        {
            WriteFloatRegister(0x34, watts);
        }

        //电源内阻（地址44=0x2C），单位mΩ
        public void SetInnerResistance(float milliOhm)
        {
            WriteFloatRegister(0x2C, milliOhm);
        }

        //泄放开关（地址1000=0x3E8）：true=开
        public void SetBleeder(bool on)
        {
            WriteUintRegister(0x3E8, on ? 1u : 0u);
        }

        //恢复出厂设置（地址2000=0x7D0），写1生效
        public void FactoryReset()
        {
            WriteUintRegister(0x7D0, 1u);
        }

        #endregion
        #region 参数保存/调用（手册5.3）

        //把当前参数存到设备内存文件（地址320功能选择0→322文件号→324确认），fileNo范围1-10
        public void SaveParamsToFile(uint fileNo)
        {
            WriteUintRegister(0x140, 0u);       //320 选择保存
            WriteUintRegister(0x142, fileNo);   //322 文件号
            WriteUintRegister(0x144, 1u);       //324 确认
        }

        //从设备内存文件调用参数（地址320=1），fileNo范围1-10
        public void LoadParamsFromFile(uint fileNo)
        {
            WriteUintRegister(0x140, 1u);       //320 选择调用
            WriteUintRegister(0x142, fileNo);   //322 文件号
            WriteUintRegister(0x144, 1u);       //324 确认
        }

        #endregion
        #region 序列模式（手册5.4、5.5）

        //运行指定序列文件（地址22=128序列模式→122文件编号→20开输出），fileNo范围1-10
        public void RunSequence(uint fileNo)
        {
            WriteUintRegister(0x16, 128u);      //22 SEQ序列模式
            WriteUintRegister(0x7A, fileNo);    //122 运行的文件编号
            WriteUintRegister(0x14, 1u);        //20 开输出
        }

        //编辑指定序列文件（地址120），fileNo范围1-10
        public void EditSequence(uint fileNo)
        {
            WriteUintRegister(0x78, fileNo);    //120 序列文件
        }

        #endregion
        #region 回读查询（手册5.1，基于通用寄存器读取0x03）

        //读Float型寄存器：超时或失败返回null
        private async Task<float?> ReadFloatRegister(ushort regAddr)
        {
            byte[] raw = await translateClass.ReadRegisterAsync(regAddr);
            if (raw == null) return null;
            return publicToolClass.BigEndianByteToFloat(raw);
        }

        //读Uint32型寄存器：超时或失败返回null
        private async Task<uint?> ReadUintRegister(ushort regAddr)
        {
            byte[] raw = await translateClass.ReadRegisterAsync(regAddr);
            if (raw == null) return null;
            return publicToolClass.BigEndianByteToUInt32(raw);
        }

        //设备状态（地址2）位定义：Bit0输出(0关1开) Bit1告警 Bit2 CV/CC(0=CV,1=CC) Bit4出线方式 Bit5远近端采样 Bit6安全互锁
        public async Task<uint?> GetDeviceStatus()
        {
            return await ReadUintRegister(0x02);
        }

        //回读功率（地址10），单位W。电压/电流已有200ms轮询在送，这个是补功率
        public async Task<float?> GetBackPower()
        {
            return await ReadFloatRegister(0x0A);
        }

        //回读毫安时（地址14）
        public async Task<float?> GetBackMilliampHour()
        {
            return await ReadFloatRegister(0x0E);
        }

        //回读温度（地址16），单位℃
        public async Task<float?> GetBackTemperature()
        {
            return await ReadFloatRegister(0x10);
        }

        //过压保护设定值回读（地址200=0xC8，手册5.8.4）
        public async Task<float?> GetOvpValue()
        {
            return await ReadFloatRegister(0xC8);
        }

        //过流保护设定值回读（地址202=0xCA，手册5.8.6）
        public async Task<float?> GetOcpValue()
        {
            return await ReadFloatRegister(0xCA);
        }

        #endregion

        private void FindState()
        {
            byte[] SendData = new byte[8];
            SendData[0] = EquipMent;
            SendData[1] = 0x03;
            SendData[2] = 0x00;
            SendData[3] = 0x06;
            SendData[4] = 0x00;
            SendData[5] = 0x04;
            SendData[6] = publicToolClass.CRC16(SendData, 6)[1];
            SendData[7] = publicToolClass.CRC16(SendData, 6)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            FindState();
        }

    }
}
