using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;               // TcpClient / NetworkStream
using System.Threading.Tasks;
using System.Threading;

namespace OscilloscopeClassLibrary
{
    /// <summary>
    /// 示波器通信类：TCP 收发 + 报文解析 + 回调通知。
    /// 是全软件的"数据咽喉"：示波器的所有指令从这里发、所有回答在这里解析。
    /// 【2026-10 真实时间轴】新增 ExpectQuery 状态机，实现"问时间参数→问波形"的自动接力。
    /// </summary>
    public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;   // 初始连接失败后的重试间隔（毫秒）
        //private Byte ReciveDataLen = 8;
        private List<byte> mRecvBuffer = new List<byte>();   // ⚠ 从未使用，历史遗留
        public string ChannelName ="OnGet";//0 电压；2 电流
        // ⚠ 上面这条注释是从电压源代码复制来的，与示波器无关；此处实际存"波形源通道名"（如 CHAN1）
        public float[]  Line1Data = new float[300];
        // ⚠ 初始长度 300 有误导性：真正解析时会被整体替换成 1000 长的新数组（见 ConvertScientificToFloat）

        //2026-10 新增：回调时附带时间轴参数（相邻点时间间隔、第0点时刻）
        public delegate void TurnEquipMent(float[] BackData, byte Ch1, float xIncrement, float xOrigin); //0 CH1 ;1 CH2;2 Ch3;3 CH4
        public TurnEquipMent thisNetPublicTool = null;
        // ↑ 主程序把 SomeActMethod 挂到这里；数据到达时被调用

        public delegate void ReciveState(string BackData, Byte EquipAddress, bool Connectflag);
        public ReciveState reciveState = null;
        // ↑ 主程序把 WriteDataLog 挂到这里；连接状态/异常时被调用

        public byte Chnumber = 0;          // 当前正在读的通道：0=CH1 1=CH2 2=CH3 3=CH4
        public bool EquipDataRead = true;  // 取数许可闸：true=定时器可以发起下一轮问答
        public byte CHANNum = 1;//Bit0 通道1 Bit1 通道2 Bit3 通道3 Bit4 通道4
        // ↑ 通道勾选状态位掩码（注意注释里 bit3/bit4 写错了，实际是 bit2=CH3 bit3=CH4）

        //2026-10 新增：真实时间轴状态机
        public float XIncrement = 0;   //相邻两点的时间间隔（秒），来自 :WAVeform:XINCrement?
        public float XOrigin = 0;      //第0个点的时刻（秒），来自 :WAVeform:XORigin?
        public byte ExpectQuery = 0;   //问答状态机：0 无等待;1 等XINCrement;2 等XORigin

        /// <summary>
        /// 建立 TCP 连接（死循环重试直到成功），成功后启动后台接收线程。
        /// ⚠ 潜在问题：只处理"首次连接"的重试；使用过程中断线，接收线程退出后
        ///    没有任何重连机制，软件必须重启才能恢复。
        /// </summary>
        public async Task ConnectAsync(string host, int port)
        {
            _client = new TcpClient { NoDelay = true }; // 禁用Nagle算法（适合实时3D数据）
            // ↑ NoDelay=true：不等缓冲区凑满，有数据立刻发（降低指令延迟）

            while (true)
            {
                try
                {
                    Console.WriteLine($"Attempting connection to {host}:{port}");
                    await _client.ConnectAsync(host, port);
                    _ = Task.Run(() => HandleIncomingData()); // 启动接收线程
                    if(reciveState != null)
                    {
                        reciveState("示波器通信建立成功： " + $"Attempting connection to {host}:{port}",3,true);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    //网络连接异常回调
                    Console.WriteLine($"Connection failed: {ex.Message}");
                    if (reciveState != null)
                    {
                        reciveState("示波器通信建立失败： " + $"Connection failed: {ex.Message}",3,true);
                        // ⚠ 潜在问题：失败时 Connectflag 也传 true，界面会显示"示波器已连接"（笔误，应为 false）
                    }
                    await Task.Delay(ReconnectDelay);
                }
            }
        }

        /// <summary>
        /// ★ 接收线程主体（后台常驻）★
        /// 循环：读 socket → 按逗号切分 → 按"段数"判断报文类型 → 处理。
        /// 报文类型判断（关键设计，也是脆弱点）：
        ///   段数==1   → 单值回答（XINCrement / XORigin / 通道名，看 ExpectQuery）
        ///   段数>=1000 → 波形数据（1000 个电压值的 ASCII 串）
        /// ⚠ 潜在问题①：TCP 不保证一次 Read 收到完整报文。如果 1000 个点的报文被拆成两包，
        ///    前包段数不足 1000 会被直接丢弃，波形丢帧。
        ///    电压源库(TcpTranslateClass)用 mRecvBuffer 累积的方式处理了这个问题，示波器库没有。
        /// ⚠ 潜在问题②：若两条单值回答恰好粘在一包里（"1E-7,-5E-4"），段数==2，
        ///    两种判断都不命中，回答丢失，状态机卡死（无超时恢复）。
        ///    现在的"一问一答、答完才发下一问"设计把粘包概率降到了很低，但没有根除。
        /// </summary>
        private async Task HandleIncomingData()
        {
           
            try
            {
                NetworkStream stream = _client.GetStream();
                while (_client.Connected)
                {
                    byte[] readBuffer = new byte[_client.Available];
                    // ⚠ 潜在问题：Available 是"此刻已到达的字节数"，不是"报文长度"。
                    //    为 0 时 ReadAsync 立即返回 0（下面的 !=0 判断挡掉，靠 Sleep(2) 空转）
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                    if (bytesRead != 0)
                    {
                        string Recive = Encoding.UTF8.GetString(readBuffer);
                       // reciveState(Recive);
                        string[] ReciveDataString = Recive.Split(',');
                        Console.WriteLine(ReciveDataString.Length);
                        if (ReciveDataString.Length == 1)
                        {
                            string val = ReciveDataString[0].Trim();
                            if (ExpectQuery == 1)                    // 等的是 XINCrement
                            {
                                float.TryParse(val, out XIncrement);
                                ExpectQuery = 2;
                                Console.WriteLine($"[时间轴] XIncrement = {XIncrement}");   //调试用：VS输出窗口可见
                                _ = SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XORigin?" + "\n"));
                                // ↑ 收到增量的回答，立刻追问原点——链条自动接力，无需定时器
                            }
                            else if (ExpectQuery == 2)               // 等的是 XORigin
                            {
                                float.TryParse(val, out XOrigin);
                                ExpectQuery = 0;
                                Console.WriteLine($"[时间轴] XOrigin = {XOrigin}");   //调试用：VS输出窗口可见
                                _ = SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:DATA?" + "\n"));
                                // ↑ 拿到原点，终于可以问波形本体了
                            }
                            else                                     // 维持原行为：连接时的通道名查询
                            {
                                ChannelName = val;
                            }
                            // ⚠ float.TryParse 失败时结果为 0：主程序 SomeActMethod 对 xIncrement<=0
                            //    有降级保护（退回序号横轴），不会崩。
                        }
                        else if (ReciveDataString.Length >= 1000)
                        {
                            Line1Data = ConvertScientificToFloat(ReciveDataString);
                            // ⚠ 注意：ConvertScientificToFloat 返回的数组长度=段数（可能>1000），
                            //    而主程序 SomeActMethod 只取前 1000 个，多余点被丢弃，行为正确但耦合隐晦
                            if (thisNetPublicTool != null)
                            {
                                // ===== 波形到达：回调主程序 + 轮转下一个勾选通道 + 重新开闸 =====
                                if(Chnumber ==1)
                                {
                                    thisNetPublicTool(Line1Data, 1, XIncrement, XOrigin);
                                    if ((CHANNum & 0x04) == 0x04)
                                    {
                                        Chnumber = 2;
                                    }
                                    else if ((CHANNum & 0x08) == 0x08)
                                    {
                                        Chnumber = 3;
                                    }
                                    else if ((CHANNum & 0x01) == 0x01)
                                    {
                                        Chnumber = 0;
                                    }
                                    else
                                    {
                                        Chnumber = 1;
                                    }
                                    EquipDataRead = true;
                                }
                                else if (Chnumber == 0)
                                {
                                    thisNetPublicTool(Line1Data, 0, XIncrement, XOrigin);
                                    if((CHANNum & 0x02) == 0x02)
                                    {
                                        Chnumber = 1;
                                    }
                                    else if((CHANNum & 0x04) == 0x04)
                                    {
                                        Chnumber = 2;
                                    }
                                    else if((CHANNum & 0x08) == 0x08)
                                    { 
                                        Chnumber = 3; 
                                    }
                                    else
                                    {
                                        Chnumber = 0;
                                    }
                                    EquipDataRead = true;
                                }
                                else if (Chnumber == 2)
                                {
                                    thisNetPublicTool(Line1Data, 2, XIncrement, XOrigin);
                                    if ((CHANNum & 0x08) == 0x08)
                                    {
                                        Chnumber = 3;
                                    }
                                    else if ((CHANNum & 0x01) == 0x01)
                                    {
                                        Chnumber = 0;
                                    }
                                    else if ((CHANNum & 0x02) == 0x02)
                                    {
                                        Chnumber = 1;
                                    }
                                    else
                                    {
                                        Chnumber = 2;
                                    }
                                    EquipDataRead = true;
                                }
                                else if (Chnumber == 3)
                                {
                                    thisNetPublicTool(Line1Data, 3, XIncrement, XOrigin);
                                    if ((CHANNum & 0x01) == 0x01)
                                    {
                                        Chnumber = 0;
                                    }
                                    else if ((CHANNum & 0x02) == 0x02)
                                    {
                                        Chnumber = 1;
                                    }
                                    else if ((CHANNum & 0x03) == 0x03)
                                    // ⚠ ★真BUG★：这里应为 (CHANNum & 0x04)。0x03 是 CH1+CH2 的掩码，
                                    //    含义对不上。后果：只勾选 CH3+CH4 两个通道时，读完 CH4 后
                                    //    无法轮换到 CH3，会一直在 CH4 上空转，CH3 永远读不到。
                                    {
                                        Chnumber = 2;
                                    }
                                    else
                                    {
                                        Chnumber = 3;
                                    }
                                    EquipDataRead = true;
                                }

                            }
                               
                        }
                        // ⚠ 潜在问题：段数为 2~999 的报文（如两条单值粘包、半截波形）走到这里
                        //    什么都不做，静默丢弃。没有日志，出问题难排查。

                    }
                    Thread.Sleep(2);   // 让出 CPU，避免空转烧核
                   
                }
            }
            catch (Exception ex)
            {
                //数据接收异常回调
                Console.WriteLine($"Receive error: {ex.Message}");
                if (reciveState != null)
                {
                    reciveState("数据接收异常： " + $"Receive error: {ex.Message}",0,false);
                }
            }
            // ⚠ 潜在问题：线程从这里退出后（断线/异常），连接就永久失效，无重连、无状态提示。
        }

        /// <summary>
        /// 发送指令（所有 SCPI 指令的唯一出口）。
        /// 未连接时静默跳过——这既是保护也是隐患：初始化阶段的指令丢失无从察觉。
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
                if (reciveState != null)
                {
                    reciveState("数据发送异常： " + $"Receive error: {ex.Message}",0,false);
                }
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

        /// <summary>
        /// 把 ASCII 字符串数组（可能是科学计数法，如 "3.20E-4"）转成 float 数组。
        /// 解析失败（格式错误）时该位置填 0 并输出控制台信息——不抛异常，宁可错不可崩。
        /// </summary>
        static float[] ConvertScientificToFloat(string[] scientificData)
        {
            float[] result = new float[scientificData.Length];
            for (int i = 0; i < scientificData.Length; i++)
            {
                try
                {
                    // 使用 float.Parse 方法将科学计数法字符串转换为 float 类型
                    result[i] = float.Parse(scientificData[i]);
                    // ⚠ float.Parse 默认用当前系统区域设置解析小数点；中文系统没问题，
                    //    若系统区域是逗号小数点的国家会全体解析失败（应传 CultureInfo.InvariantCulture）
                }
                catch (FormatException)
                {
                    // 处理格式错误的情况，这里简单输出错误信息
                    Console.WriteLine($"数据 {scientificData[i]} 格式错误，无法转换。");
                    result[i] = 0; // 可以根据需求进行其他处理
                }
            }
            return result;
        }

    }
}
