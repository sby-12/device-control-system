using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;   // System.Timers.Timer（200ms 周期查询电压电流）

namespace ValueSourceN2410ClassLibrary
{
    /// <summary>
    /// N2410 可编程电压源驱动类。
    /// ⚠ 与示波器/信号源完全不同：这台仪器走 Modbus-RTU 二进制协议（不是 SCPI 文本），
    ///    帧格式 = [地址][功能码0x10写/0x03读][寄存器地址][数据][CRC16低][CRC16高]。
    /// 每个 Set 方法都在手工拼这一帧字节，结构一模一样，只是寄存器地址不同——
    /// 是典型的"协议手册直接翻译成代码"。
    /// </summary>
    public class N2410Class
    {
        public TcpTranslateClass translateClass;     // TCP 通信（走网口转 Modbus）
        public PublicToolClass publicToolClass;      // 工具类：CRC16 校验 + float↔大端字节
        Timer ThisEquitRequestTime = null;           // 200ms 定时查询定时器
        Byte EquipMent = 1;                          // 仪器地址（N2410=1，N36200=2）

        public N2410Class(Byte EquipMentAddress) 
        {
            EquipMent = EquipMentAddress;
            translateClass = new TcpTranslateClass(EquipMent);
            
            publicToolClass = new PublicToolClass();

            ThisEquitRequestTime = new Timer(200);//20ms进入一次
            // ⚠ 注释笔误：实际 200ms
            ThisEquitRequestTime.Elapsed += new System.Timers.ElapsedEventHandler(ThisEquitRequestTime_Event);
            ThisEquitRequestTime.AutoReset = true;
            ThisEquitRequestTime.Enabled = false;    // 连接成功后才 Start
            
        }

        //通信连接：连 TCP + 启动 200ms 周期查询
        public void OpenTranslate(string host, int port)
        {
            _ = translateClass.ConnectAsync(host, port);
            ThisEquitRequestTime.Start();
            // ⚠ 潜在问题：查询定时器在 TCP 还没连上时就开始发，SendAsync 会因未连接静默丢弃，
            //    连上之前的查询全部浪费（无害但不严谨）
        }

        //通信关闭
        public void CloseTranslate()
        {
            ThisEquitRequestTime.Stop();
            translateClass.CloseAsync();
        }

        /// <summary>
        /// 电压设置：往寄存器 0x0008 写 float（大端）。
        /// 帧解析：[0]=仪器地址 [1]=0x10写多个寄存器 [2,3]=寄存器地址(00 08) [4,5]=寄存器数量(00 02)
        ///         [6]=字节数(04) [7-10]=float大端 [11]=CRC低 [12]=CRC高
        /// </summary>
        public void SetValue(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;       // 地址
            SendData[1] = 0x10;            // 功能码：写多个保持寄存器
            SendData[2] = 0x00;            // 寄存器地址高字节
            SendData[3] = 0x08;            // 寄存器地址低字节 → 0x0008 = 电压设定
            SendData[4] = 0x00;
            SendData[5] = 0x02;            // 写 2 个寄存器（=4字节=1个float）
            SendData[6] = 0x04;            // 数据字节数 4
            byte[] b_value = publicToolClass.FloatToBigEndianByte(value);   // float → 4字节大端
            SendData[7] = b_value[0];
            SendData[8] = b_value[1];
            SendData[9] = b_value[2];
            SendData[10] = b_value[3];
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];   // CRC 低字节在前
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];   // CRC 高字节在后（Modbus 规矩）
            _=translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 电流设置：寄存器 0x000A（帧结构与上面逐字节相同，只换了寄存器地址）。
        /// </summary>
        public void SetCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x0A;            // ← 0x000A = 电流设定
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

        /// <summary>
        /// 电流保护（过流上限）设置：寄存器 0x001C。
        /// </summary>
        public void SetOverLoadCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x1C;            // ← 0x001C = 过流保护值
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

        /// <summary>
        /// 通道选择：寄存器 0x0002，写 1=通道1，2=通道2（N2410 是双通道机型）。
        /// 数据是 4 字节整数（不是 float），高字节在前：[7]=00 [8]=Cvalue [9]=00 [10]=00。
        /// </summary>
        public void SetChannel(byte Cvalue)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x02;            // ← 0x0002 = 通道选择
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
        
            SendData[7] = 0x00;
            SendData[8] = Cvalue;
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 通道输出开关：寄存器 0x0006，写 1=开 0=关。
        /// </summary>
        public void SetChannelOpen(bool Open)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x06;            // ← 0x0006 = 输出开关
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;

            SendData[7] = 0x00;
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

        /// <summary>
        /// 查询状态帧（功能码 0x03 读保持寄存器）：读 0x000C 起的 4 个寄存器（8字节）。
        /// 仪器会回一帧 13 字节数据（电压float+电流float），由 TcpTranslateClass.ProcessValueData 解析。
        /// </summary>
        private void  FindState()
        {
            byte[] SendData = new byte[8];
            SendData[0] = EquipMent;
            SendData[1] = 0x03;            // 功能码：读保持寄存器
            SendData[2] = 0x00;
            SendData[3] = 0x0C;            // 起始寄存器 0x000C
            SendData[4] = 0x00;
            SendData[5] = 0x04;            // 读 4 个寄存器（=2个float=电压+电流）
            SendData[6] = publicToolClass.CRC16(SendData, 6)[1];
            SendData[7] = publicToolClass.CRC16(SendData, 6)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 定时器回调：每 200ms 问一次电压电流 → 形成周期上报流。
        /// </summary>
        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            FindState();
        }


    }
}
