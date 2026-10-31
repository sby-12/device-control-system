using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using InstrumentInterfaceLibrary;

namespace ValueSourceN2410ClassLibrary
{
    //2026-10-30 实现 IPowerSource 统一接口（方案A·接口隔离）。
    //N2410 是老机型：过压保护/状态回读/出厂复位这些新功能没有硬件支撑，
    //对应接口成员统一抛 NotSupportedException，由上层（设备选择逻辑）按机型屏蔽相关按钮。
    public class N2410Class : IPowerSource
    {
        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "NGI N2410";

        public TcpTranslateClass translateClass;
        public PublicToolClass publicToolClass;
        Timer ThisEquitRequestTime = null;
        Byte EquipMent = 1;
        public N2410Class(Byte EquipMentAddress) 
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

        //电压设置
        public void SetValue(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x08;
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
            _=translateClass.SendAsync(SendData);//数据发送
        }

        //电流设置
        public void SetCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x0A;
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

        //电流保护设置
        public void SetOverLoadCurrent(float value)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x1C;
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

        //通道选择
        public void SetChannel(byte Cvalue)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x02;
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

        //通道开关
        public void SetChannelOpen(bool Open)
        {
            byte[] SendData = new byte[13];
            SendData[0] = EquipMent;
            SendData[1] = 0x10;
            SendData[2] = 0x00;
            SendData[3] = 0x06;
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

        private void  FindState()
        {
            byte[] SendData = new byte[8];
            SendData[0] = EquipMent;
            SendData[1] = 0x03;
            SendData[2] = 0x00;
            SendData[3] = 0x0C;
            SendData[4] = 0x00;
            SendData[5] = 0x04;
            SendData[6] = publicToolClass.CRC16(SendData, 6)[1];
            SendData[7] = publicToolClass.CRC16(SendData, 6)[0];
            _ = translateClass.SendAsync(SendData);//数据发送
        }

        //===== 2026-10-30 IPowerSource 接口补充成员（N2410 老机型能力裁剪）=====

        /// <summary>输出开关（接口统一入口）。直接转发给本机已有的 SetChannelOpen。</summary>
        public void SetOutput(bool on)
        {
            SetChannelOpen(on);
        }

        /// <summary>N2410 无过压保护功能，上层按机型屏蔽 OVP 按钮。</summary>
        public void SetOverLoadVolatage(float value)
        {
            throw new NotSupportedException("N2410 不支持过压保护（OVP）设置");
        }

        /// <summary>N2410 无状态字回读，由定时轮询 FindState 代替。</summary>
        public Task<uint?> GetDeviceStatus()
        {
            throw new NotSupportedException("N2410 不支持状态字回读");
        }

        /// <summary>N2410 无功率回读。</summary>
        public Task<float?> GetBackPower()
        {
            throw new NotSupportedException("N2410 不支持功率回读");
        }

        /// <summary>N2410 无温度回读。</summary>
        public Task<float?> GetBackTemperature()
        {
            throw new NotSupportedException("N2410 不支持温度回读");
        }

        /// <summary>N2410 无出厂复位指令。</summary>
        public void FactoryReset()
        {
            throw new NotSupportedException("N2410 不支持出厂复位");
        }

        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            FindState();
        }


    }
}
