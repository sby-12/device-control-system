using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace ValueSourceN2410ClassLibrary
{
    /// <summary>
    /// N36200 大功率可编程电压源驱动类（Modbus-RTU 协议，帧格式与 N2410Class 完全同款，
    /// 只是仪器地址=2、寄存器地址映射不同）。
    /// 方法结构与 N2410Class 一一对应，注释只标差异点。
    /// </summary>
    public class N36200Class
    {
        #region 变量定义
        public TcpTranslateClass translateClass;
        public PublicToolClass publicToolClass;
        Timer ThisEquitRequestTime = null;
        Byte EquipMent = 1;
        #endregion
        public N36200Class(Byte EquipMentAddress)
        {
            EquipMent = EquipMentAddress;          // 主程序传的是 2
            translateClass = new TcpTranslateClass(EquipMent);
            publicToolClass = new PublicToolClass();
            ThisEquitRequestTime = new Timer(200);//20ms进入一次（同样笔误，实际200ms）
            ThisEquitRequestTime.Elapsed += new System.Timers.ElapsedEventHandler(ThisEquitRequestTime_Event);
            ThisEquitRequestTime.AutoReset = true;
            ThisEquitRequestTime.Enabled = false;
     
        }
        //通信连接
        public void OpenTranslate(string host, int port)
        {
            _ = translateClass.ConnectAsync(host, port);
            ThisEquitRequestTime.Start();
        }

        //通信关闭
        public void CloseTranslate()
        {
            ThisEquitRequestTime.Stop();
            translateClass.CloseAsync();
        }

        /// <summary>
        /// 模式设置为普通模式：寄存器 0x0016 写 0。
        /// 连接后由连接弹窗立即调用。
        /// </summary>
        public void SetChannelMode()
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x16;            // ← 0x0016 = 工作模式
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            SendData[7] = 0x00;
            SendData[8] = 0x00;            // 0 = 普通模式
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 设置为 CV 优先（电压优先模式）：寄存器 0x001A 写 1。
        /// </summary>
        public void SetChannelCvOrCc()
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x1A;            // ← 0x001A = CV/CC 优先
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = 0x04;
            SendData[7] = 0x01;            // 1 = CV 优先
            SendData[8] = 0x00;
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 打开/关闭通道输出：寄存器 0x0014 写 1/0。
        /// </summary>
        public void SetChannelOpenOrOff(bool Open)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x14;            // ← 0x0014 = 输出开关
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
            // ⚠ 注意：[7] 没赋值（默认 0），与 N2410 版相比少一行显式赋值，效果相同但风格不一
            SendData[9] = 0x00;
            SendData[10] = 0x00;
            SendData[11] = publicToolClass.CRC16(SendData, 11)[1];
            SendData[12] = publicToolClass.CRC16(SendData, 11)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        /// <summary>
        /// 电压设置：寄存器 0x0028。
        /// ⚠ 这里曾存学长的需求"最大电压限度改为2000"——本方法本身不做范围限制，
        ///    上限靠界面输入框(N36ValueValue 的 Maximum)约束，写超了仪器会按自身上限截断或报错。
        /// </summary>
        public void SetValue(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x28;            // ← 0x0028 = 电压设定
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
        /// 电流设置：寄存器 0x002A。
        /// </summary>
        public void SetCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x2A;            // ← 0x002A = 电流设定
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
        /// 电流保护设置：寄存器 0x00CA。
        /// </summary>
        public void SetOverLoadCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0xCA;            // ← 0x00CA = 过流保护值
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
        /// 电压保护设置：寄存器 0x00C8。
        /// ⚠ 方法名 Volatage 是 Voltage 的拼写错误（全项目沿用，改名需同步改调用处）。
        /// ⚠ 主程序没有调用它的地方——界面没有"过压保护设置"按钮，属于已写未接线的功能。
        /// </summary>
        public void SetOverLoadVolatage(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0xC8;            // ← 0x00C8 = 过压保护值
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
        /// 周期查询帧（0x03 读）：读 0x0006 起 4 个寄存器——电压float + 电流float。
        /// 与 N2410 的 FindState 寄存器不同（0x0006 vs 0x000C），因为两台仪器寄存器映射不同。
        /// </summary>
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

        /// <summary>
        /// 200ms 定时器回调：发一次状态查询。
        /// </summary>
        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            FindState();
        }

    }
}
