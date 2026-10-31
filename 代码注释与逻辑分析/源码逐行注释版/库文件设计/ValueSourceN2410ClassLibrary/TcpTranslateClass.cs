using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ValueSourceN2410ClassLibrary
{
    /// <summary>
    /// 电压源通信类（Modbus-RTU over TCP）。
    /// 与示波器库的接收逻辑形成鲜明对比——这里做了"粘包/拆包处理"：
    /// 用一个累积缓冲区 mRecvBuffer 拼数据，凑够一帧(13字节)才解析。
    /// 这是示波器库缺失的正确姿势（见示波器库注释中的潜在问题①）。
    /// </summary>
    public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;
        private Byte ReciveDataLen = 13;              // 一帧固定 13 字节：地址1+功能码1+字节数1+数据8+CRC2
        private List<byte> mRecvBuffer = new List<byte>();   // ★ 累积缓冲区：TCP 拆包时靠它拼帧
        public float[] floats = new float[2];//0 电压；2 电流
        // ⚠ 注释写"0 电压；2 电流"，实际含义是 [0]=电压 [1]=电流
        public PublicToolClass toolClass = new PublicToolClass();   // 字节序转换工具

        public delegate void TurnEquipMent(float[] BackData);
        public TurnEquipMent thisNetPublicTool = null;
        // ↑ 主程序挂 N3416InfDisplay / N3621InfDisplay（界面电压电流显示）

        public delegate void ReciveState(string BackData,Byte EquipAddress,bool Connectflag);
        public ReciveState reciveState = null;
        Byte EquipMent = 1;   // 仪器地址，用于区分回调文案（1=N2410, 2=N36200）
        
        public TcpTranslateClass(byte EquipMentAddress)
        {
            EquipMent = EquipMentAddress;
        }

        /// <summary>
        /// 建立 TCP 连接（死循环重试）。成功后启动接收线程。
        /// 与示波器版的差异：这里按 EquipMent 区分回调文案（一个类服务两台仪器）。
        /// </summary>
        public async Task ConnectAsync(string host, int port)
        {
            _client = new TcpClient { NoDelay = true }; // 禁用Nagle算法（适合实时3D数据）

            while (true)
            {
                try
                {
                    Console.WriteLine($"Attempting connection to {host}:{port}");
                    await _client.ConnectAsync(host, port);
                    if (reciveState != null)
                    {
                        if(EquipMent == 1)
                        {
                            reciveState("N2410通信建立成功： " + $"Attempting connection to {host}:{port}",1,true);
                        }
                        else
                        {
                            reciveState("N36200通信建立成功： " + $"Attempting connection to {host}:{port}",2,true);
                        }
                    }
                    _ = Task.Run(() => HandleIncomingData()); // 启动接收线程
                    break;
                }
                catch (Exception ex)
                {
                    //网络连接异常回调
                    if (reciveState != null)
                    {
                        if(EquipMent == 1)
                        {
                            reciveState("N2410通信建立失败： " + $"Attempting connection to {host}:{port}",1,true);
                        }
                        else
                        {
                            reciveState("N36200通信建立失败： " + $"Attempting connection to {host}:{port}",2,true);
                        }
                        // ⚠ 同样的笔误：失败也传 Connectflag=true
                    }
                    Console.WriteLine($"Connection failed: {ex.Message}");
                    await Task.Delay(ReconnectDelay);
                }
            }
        }

        /// <summary>
        /// 接收线程：读 socket → 交给 ProcessValueData 拼帧解析。
        /// </summary>
        private async Task HandleIncomingData()
        {
           
            try
            {
                NetworkStream stream = _client.GetStream();
                while (_client.Connected)
                {
                    byte[] readBuffer = new byte[_client.Available];
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                    //if(bytesRead == 0)
                    //{
                    //    _client.Close();
                    //    break;//关闭连接 回调主程序
                    //}
                    // ↑ 被注释掉的断线检测：bytesRead==0 表示对端关闭。没启用 → 断线无感知（通病）
                    if(bytesRead != 0)
                    {
                        ProcessValueData(readBuffer);
                    }
                    
                }
            }
            catch (Exception ex)
            {
                //数据接收异常回调
                Console.WriteLine($"Receive error: {ex.Message}");
            }
        }

   
        /// <summary>
        /// ★ 拼帧解析（核心方法，示波器库缺的正是这套）★
        /// 流程：新数据追加进累积缓冲区 → 不够13字节就等下一波 →
        ///       逐字节找帧头（地址+0x03+0x08）→ 找到就切出13字节解析 → 清缓冲。
        /// </summary>
        private void ProcessValueData(byte[] rawData)
        {
            List<byte[]> ReadBuffer2 = new List<byte[]>();   // ⚠ 声明了从未使用
            mRecvBuffer.AddRange(rawData);                   // ① 把新收到的字节追加到累积缓冲区
            if (mRecvBuffer.Count < ReciveDataLen)
                return;                                      // ② 不够一帧，等下次再来

            for (int i = 0; i < mRecvBuffer.Count; i++)      // ③ 逐字节扫描帧头
            {
                try
                {
                    if ((mRecvBuffer[i] == EquipMent) && (mRecvBuffer[i + 1] == 0x03) && (mRecvBuffer[i + 2] == 0x08))
                    // ↑ 帧头三特征：本机地址 + 功能码0x03(读) + 字节数0x08(8字节数据)
                    {
                        byte[] cmd = mRecvBuffer.GetRange(i, ReciveDataLen).ToArray();   // 切出一整帧
                        //数据解析
                        //byte[] data = new byte[4] { cmd[3], cmd[4], cmd[5], cmd[6]};
                        // ↑ 被注释掉的是"顺序取4字节"，下面是调整过大端后的取法：
                        byte[] data = new byte[4] { cmd[5], cmd[6], cmd[3], cmd[4] };
                        // ⚠ 帧内字节序与寄存器序的换算：寄存器是 [高字][低字]、字内 [高字节][低字节]，
                        //    拼回 float 要按 [5][6][3][4] 取——这是对着协议手册调出来的，别"优化"它
                        floats[0] = toolClass.BigEndianByteToFloat(data);   // 电压
                        //byte[] data1 = new byte[4] { cmd[7], cmd[8], cmd[9], cmd[10] };
                        byte[] data1 = new byte[4] { cmd[9], cmd[10], cmd[7], cmd[8] };
                        floats[1] = toolClass.BigEndianByteToFloat(data1);  // 电流
                        if(thisNetPublicTool!=null)
                        {
                            thisNetPublicTool(floats);      // 回调主程序刷新界面显示
                        }
                        mRecvBuffer.Clear();                 // ④ 解析完清空累积缓冲（假定一响应周期内只有一帧）
                        // ⚠ 潜在问题：如果两帧连在一起到达，Clear 会把第二帧也扔掉，丢一帧数据。
                        //    200ms 查询一次、一帧 13 字节，实际很少粘两帧，所以没炸过。
                    }
                    else
                    {
                        mRecvBuffer.RemoveAt(0);
                        // ↑ 帧头不匹配：丢弃最老的一个字节（滑动窗口），继续找
                    }
                }
                catch (Exception ex)
                {
                    //数据解析异常回调
                    // ⚠ catch 块是空的：解析异常被完全吞掉，包括 i+2 越界等，静默容错。
                }
            }
        }

        /// <summary>
        /// 发送（Modbus 帧的唯一出口）。
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            try
            {
                if (_client?.Connected == true)
                {
                    await _client.GetStream().WriteAsync(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                //发送数据异常回调
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }

        public void CloseAsync()
        {
            try
            {
                if (_client?.Connected == true)
                {
                    _client.Close();
                }
            }
            catch (Exception ex)
            {
                //发送数据异常回调
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }

    }
}
