using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ValueSourceN2410ClassLibrary
{
      public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;
        private Byte ReciveDataLen = 13;
        private List<byte> mRecvBuffer = new List<byte>();
        public float[] floats = new float[2];//0 电压；2 电流
        public PublicToolClass toolClass = new PublicToolClass();

        //2026-10-31 修复3：限制重试次数 + 连接成功回调
        private const int MaxConnectRetries = 3;   //TCP建链最多重试3次（间隔5秒），不再无限循环
        private CancellationTokenSource _connectCts = null;  //CloseAsync 用它取消进行中的重试
        public Action AfterConnected = null;   //TCP真正建链成功后才执行（替代 Thread.Sleep 赌运气）
        //2026-10-31 修复24：三次重试全失败时通知上层。原来只通过 reciveState 改状态文字，
        //设备槽位仍挂死设备，主界面按钮一点就调到死设备上（VS 报 NullReferenceException）
        public Action AfterConnectFailed = null;

        public delegate void TurnEquipMent(float[] BackData);
        public TurnEquipMent thisNetPublicTool = null;

        public delegate void ReciveState(string BackData,Byte EquipAddress,bool Connectflag);
        public ReciveState reciveState = null;
        Byte EquipMent = 1;

        //2026-10-17 通用寄存器读取基础设施：等待中的读取任务（一次只允许一条在飞）
        private TaskCompletionSource<byte[]> _pendingRead = null;

        //通用寄存器读取（0x03功能码读1个寄存器=4字节数据，手册4.1）。
        //返回该寄存器的4字节原始数据（已按 {b5,b6,b3,b4} 排列好，可直接交给BigEndianByteToFloat/UInt32），超时返回null
        public async Task<byte[]> ReadRegisterAsync(ushort regAddr, int timeoutMs = 1000)
        {
            if (_pendingRead != null) return null;   //上一条还没回，防连点
            byte[] SendData = new byte[8];
            SendData[0] = EquipMent;
            SendData[1] = 0x03;
            SendData[2] = (byte)(regAddr >> 8);
            SendData[3] = (byte)(regAddr & 0xFF);
            SendData[4] = 0x00;
            SendData[5] = 0x02;
            SendData[6] = toolClass.CRC16(SendData, 6)[1];
            SendData[7] = toolClass.CRC16(SendData, 6)[0];
            var tcs = new TaskCompletionSource<byte[]>();
            _pendingRead = tcs;
            await SendAsync(SendData);
            Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            _pendingRead = null;
            if (done == tcs.Task) return tcs.Task.Result;
            Console.WriteLine($"[N36200] 读寄存器 0x{regAddr:X4} 超时");
            return null;
        }
        public TcpTranslateClass(byte EquipMentAddress)
        {
            EquipMent = EquipMentAddress;
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
                        if (EquipMent == 1)
                        {
                            string head = (attempt < MaxConnectRetries)
                                ? ("（第" + attempt + "次，" + (ReconnectDelay / 1000) + "秒后重试）")
                                : ("（已重试" + MaxConnectRetries + "次，请检查设备电源和网线后重新连接）");
                            reciveState("N2410通信建立失败" + head + "： " + ex.Message,1,false);
                        }
                        else
                        {
                            string head2 = (attempt < MaxConnectRetries)
                                ? ("（第" + attempt + "次，" + (ReconnectDelay / 1000) + "秒后重试）")
                                : ("（已重试" + MaxConnectRetries + "次，请检查设备电源和网线后重新连接）");
                            reciveState("N36200通信建立失败" + head2 + "： " + ex.Message,2,false);
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
            //2026-10-31 审查修订(S3)：回调整体包 try/catch——回调抛异常会传回这里，
            //让 ConnectAsync 任务以未观察异常终结（调试期噪音、改策略时会咬人）
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
                        //2026-10-31 修复14：对端关闭后 ReadAsync 返回 0。原来该分支被注释掉，
                        //且 while(_client.Connected) 探测不到对端关闭，接收线程永久空转、无任何提示。
                        //Poll+Available 组合确认真断开才退出并上报（EquipMent 1=N2410 2=N36200）
                        if (!client.Connected ||
                            (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0))
                        {
                            try { client.Close(); } catch { }
                            if (reciveState != null)
                            {
                                string name = (EquipMent == 1) ? "N2410" : "N36200";
                                reciveState(name + "连接已断开，请检查网线/设备后重新连接", EquipMent, false);
                            }
                            break;
                        }
                    }
                    if(bytesRead != 0)
                    {
                        ProcessValueData(readBuffer);
                    }
                    else
                    {
                        await Task.Delay(2);   //2026-10-31 修复14附带：空闲时让出 CPU，避免 0 字节读死循环空转
                    }
                    
                }
            }
            catch (Exception ex)
            {
                //数据接收异常回调
                Console.WriteLine($"Receive error: {ex.Message}");
            }
        }

   
        private void ProcessValueData(byte[] rawData)
        {
            mRecvBuffer.AddRange(rawData);
            if (mRecvBuffer.Count < ReciveDataLen)
                return;
            for (int i = 0; i < mRecvBuffer.Count; i++)
            {
                try
                {
                    //2026-10-17 通用读取响应优先：单寄存器读取，字节数0x04，整帧9字节
                    if (_pendingRead != null && (mRecvBuffer[i] == EquipMent) && (mRecvBuffer[i + 1] == 0x03) && (mRecvBuffer[i + 2] == 0x04))
                    {
                        if (i + 9 > mRecvBuffer.Count) break;   //帧没收齐，等下一个包
                        byte[] cmd = mRecvBuffer.GetRange(i, 9).ToArray();
                        byte[] data = new byte[4] { cmd[5], cmd[6], cmd[3], cmd[4] };
                        _pendingRead.TrySetResult(data);
                        mRecvBuffer.RemoveRange(i, 9);
                        i = -1;                                 //缓冲区变了，从头重新扫
                        continue;
                    }
                    if ((mRecvBuffer[i] == EquipMent) && (mRecvBuffer[i + 1] == 0x03) && (mRecvBuffer[i + 2] == 0x08))
                    {
                        if (i + ReciveDataLen > mRecvBuffer.Count) break;   //帧没收齐，等下一个包
                        byte[] cmd = mRecvBuffer.GetRange(i, ReciveDataLen).ToArray();
                        //数据解析
                        byte[] data = new byte[4] { cmd[5], cmd[6], cmd[3], cmd[4] };
                        floats[0] = toolClass.BigEndianByteToFloat(data);
                        byte[] data1 = new byte[4] { cmd[9], cmd[10], cmd[7], cmd[8] };
                        floats[1] = toolClass.BigEndianByteToFloat(data1);
                        if(thisNetPublicTool!=null)
                        {
                            thisNetPublicTool(floats);
                        }
                        mRecvBuffer.RemoveRange(i, ReciveDataLen);   //2026-10-17 原Clear()会把紧跟其后的其他帧也抹掉，改为精确摘除本帧
                        i = -1;
                        continue;
                    }
                    //不是任何已知帧的头，丢掉一个字节继续扫（原逻辑）
                    mRecvBuffer.RemoveAt(0);
                    i = -1;
                }
                catch (Exception ex)
                {
                    //数据解析异常回调
                }
            }
        }

        public async Task SendAsync(byte[] data)
        {
            //2026-10-31 修复24：_client 只读一次存局部变量。原来"检查 Connected"和"GetStream"
            //各读一次字段，连接重试循环可能在两读之间把字段换成新 TcpClient，极端时序下写错连接
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

    }
}
