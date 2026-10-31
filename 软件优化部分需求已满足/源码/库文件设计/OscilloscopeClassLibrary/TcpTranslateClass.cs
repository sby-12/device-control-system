using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;

namespace OscilloscopeClassLibrary
{
    public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;
        //private Byte ReciveDataLen = 8;
        private List<byte> mRecvBuffer = new List<byte>();
        public string ChannelName ="OnGet";//0 电压；2 电流
        public float[]  Line1Data = new float[300];

        //2026-10 新增：回调时附带时间轴参数（相邻点时间间隔、第0点时刻）
        public delegate void TurnEquipMent(float[] BackData, byte Ch1, float xIncrement, float xOrigin); //0 CH1 ;1 CH2;2 Ch3;3 CH4
        public TurnEquipMent thisNetPublicTool = null;

        public delegate void ReciveState(string BackData, Byte EquipAddress, bool Connectflag);
        public ReciveState reciveState = null;
        public byte Chnumber = 0;
        public bool EquipDataRead = true;
        public byte CHANNum = 1;//Bit0 通道1 Bit1 通道2 Bit3 通道3 Bit4 通道4

        //2026-10 新增：真实时间轴状态机
        public float XIncrement = 0;   //相邻两点的时间间隔（秒），来自 :WAVeform:XINCrement?
        public float XOrigin = 0;      //第0个点的时刻（秒），来自 :WAVeform:XORigin?
        public byte ExpectQuery = 0;   //问答状态机：0 无等待;1 等XINCrement;2 等XORigin

        //2026-10-17 通用查询基础设施：等待中的查询任务（一次只允许一条查询在飞，防连点打架）
        private TaskCompletionSource<string> _pendingQuery = null;

        //2026-10-31 修复3：限制重试次数 + 连接成功回调
        private const int MaxConnectRetries = 3;   //TCP建链最多重试3次（间隔5秒），不再无限循环
        private CancellationTokenSource _connectCts = null;  //CloseAsync 用它取消进行中的重试
        public Action AfterConnected = null;   //TCP真正建链成功后才执行（替代 Thread.Sleep 赌运气）
        //2026-10-31 修复24：三次重试全失败时通知上层回滚槽位（与 ValueSource 库同方案）
        public Action AfterConnectFailed = null;

        //通用查询：发送一条带?的SCPI命令，等待示波器回一整行（超时的返回null）
        public async Task<string> QueryAsync(string scpiCmd, int timeoutMs)
        {
            if (_pendingQuery != null) return null;   //上一条查询还没回，直接放弃本条
            var tcs = new TaskCompletionSource<string>();
            _pendingQuery = tcs;
            await SendAsync(Encoding.UTF8.GetBytes(scpiCmd + "\n"));
            Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            _pendingQuery = null;
            if (done == tcs.Task) return tcs.Task.Result;
            Console.WriteLine($"[查询超时] {scpiCmd}");
            return null;
        }

        public async Task ConnectAsync(string host, int port)
        {
            _connectCts?.Cancel();                 //2026-10-31 修复3：先停掉上一次未完成的重试，防止僵尸重试
            _connectCts = new CancellationTokenSource();
            var token = _connectCts.Token;

            for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
            {
                //2026-10-31 修复3：每轮重试都新建 TcpClient。失败过的 TcpClient 不能复用，
                //一旦 Connect 失败，它的 Connected 永远为 false，原代码复用同一个对象导致之后每次都必然失败
                _client = new TcpClient { NoDelay = true }; // 禁用Nagle算法（适合实时3D数据）
                try
                {
                    Console.WriteLine($"Attempting connection to {host}:{port} (第{attempt}/{MaxConnectRetries}次)");
                    await _client.ConnectAsync(host, port);
                    _ = Task.Run(() => HandleIncomingData()); // 启动接收线程
                    if(reciveState != null)
                    {
                        reciveState("示波器通信建立成功： " + $"Attempting connection to {host}:{port}",3,true);
                    }
                    AfterConnected?.Invoke();   //2026-10-31 修复3：TCP真正通了，才执行依赖连接的初始化
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;   //CloseAsync 主动取消：安静退出，不再重试也不再报错
                }
                catch (Exception ex)
                {
                    //网络连接异常回调
                    Console.WriteLine($"Connection failed: {ex.Message}");
                    try { _client.Close(); } catch { }
                    if (reciveState != null)
                    {
                        if (attempt < MaxConnectRetries)
                        {
                            reciveState("示波器通信建立失败（第" + attempt + "次，" + (ReconnectDelay / 1000) + "秒后重试）： " + ex.Message,3,false);
                        }
                        else
                        {
                            reciveState("示波器通信建立失败（已重试" + MaxConnectRetries + "次，请检查设备电源和网线后重新连接）： " + ex.Message,3,false);
                        }
                    }
                    if (attempt < MaxConnectRetries)
                    {
                        await Task.Delay(ReconnectDelay, token);
                    }
                }
            }
            //2026-10-31 修复24：能走到这里说明三次重试全失败（成功会 return，取消也 return 了）。
            //通知上层回滚设备槽位，别留死设备。
            //2026-10-31 审查修订(S3)：回调整体包 try/catch（同 ValueSource 库）
            try { AfterConnectFailed?.Invoke(); }
            catch (Exception cbEx) { Console.WriteLine($"AfterConnectFailed callback error: {cbEx.Message}"); }
        }

        private async Task HandleIncomingData()
        {
           
            try
            {
                var client = _client;   //2026-10-31 修复24：接收循环全程只盯本次连接的对象，
                                        //重试循环换了新 _client 也不串台（这里只在连接成功后进入，对象非空）
                NetworkStream stream = client.GetStream();
                while (client.Connected)
                {
                    byte[] readBuffer = new byte[client.Available];
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                    if (bytesRead == 0)
                    {
                        //2026-10-31 修复14：对端关闭（仪器重启/拔网线）后 ReadAsync 返回 0。
                        //原代码这个分支是空的，且 while(_client.Connected) 探测不到对端关闭，
                        //导致接收线程 2ms 轮询永久空转、软件无任何提示。Poll+Available 组合确认真断开才退出。
                        if (!client.Connected ||
                            (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0))
                        {
                            try { client.Close(); } catch { }
                            if (reciveState != null)
                            {
                                reciveState("示波器连接已断开，请检查网线/设备后重新连接", 3, false);
                            }
                            break;
                        }
                    }
                    if (bytesRead != 0)
                    {
                        //2026-10-17 按行缓冲：单条响应可能跨多个TCP包（深存储读取时一条波形达1MB以上），
                        //必须收齐整行（以\n结尾）再解析，否则会拿半个响应当完整波形。
                        mRecvBuffer.AddRange(readBuffer);
                        while (true)
                        {
                            int nl = mRecvBuffer.IndexOf(0x0A);
                            if (nl < 0) break;          //只有半行，等下一个包
                            string Recive = Encoding.UTF8.GetString(mRecvBuffer.GetRange(0, nl).ToArray());
                            mRecvBuffer.RemoveRange(0, nl + 1);
                            ProcessOneLine(Recive);
                        }
                    }
                    Thread.Sleep(2);
                   
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
        }

        //2026-10-17 从接收循环拆出：处理一整行响应（按行缓冲后调用）
        private void ProcessOneLine(string Recive)
        {
            //2026-10-17 通用查询优先：若正在等查询回答，这行直接交给查询方；
            //同时丢弃可能残缺的波形查询状态机（下次轮转会自行重新同步）
            if (_pendingQuery != null)
            {
                ExpectQuery = 0;
                _pendingQuery.TrySetResult(Recive);
                return;
            }
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
                }
                else if (ExpectQuery == 2)               // 等的是 XORigin
                {
                    float.TryParse(val, out XOrigin);
                    ExpectQuery = 0;
                    Console.WriteLine($"[时间轴] XOrigin = {XOrigin}");   //调试用：VS输出窗口可见
                    _ = SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:DATA?" + "\n"));
                }
                else                                     // 维持原行为：连接时的通道名查询
                {
                    ChannelName = val;
                }
            }
            else if (ReciveDataString.Length >= 1000)
            {
                Line1Data = ConvertScientificToFloat(ReciveDataString);
                if (thisNetPublicTool != null)
                {
                    if (Chnumber == 1)
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
                        if ((CHANNum & 0x02) == 0x02)
                        {
                            Chnumber = 1;
                        }
                        else if ((CHANNum & 0x04) == 0x04)
                        {
                            Chnumber = 2;
                        }
                        else if ((CHANNum & 0x08) == 0x08)
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
                        else if ((CHANNum & 0x04) == 0x04)
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
        }

        public async Task SendAsync(byte[] data)
        {
            //2026-10-31 修复24：_client 只读一次存局部变量（检查与使用之间不被重试循环换对象）
            var client = _client;
            try
            {
                if (client?.Connected == true)
                {
                    await client.GetStream().WriteAsync(data, 0, data.Length);
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
                _connectCts?.Cancel();   //2026-10-31 修复3：取消进行中的重试，防止旧设备在后台无限重试空转
                var client = _client;
                if (client?.Connected == true)
                {
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                //发送数据异常回调
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }

        static float[] ConvertScientificToFloat(string[] scientificData)
        {
            float[] result = new float[scientificData.Length];
            for (int i = 0; i < scientificData.Length; i++)
            {
                try
                {
                    // 使用 float.Parse 方法将科学计数法字符串转换为 float 类型
                    result[i] = float.Parse(scientificData[i]);
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
