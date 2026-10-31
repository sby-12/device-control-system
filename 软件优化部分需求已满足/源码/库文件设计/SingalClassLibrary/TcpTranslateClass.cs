using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SingalClassLibrary
{
    public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;
        //private Byte ReciveDataLen = 8;
        private List<byte> mRecvBuffer = new List<byte>();
        public string ChannelName = "OnGet";//0 电压；2 电流
        public float[] Line1Data = new float[300];

        //2026-10-31 修复3：限制重试次数 + 连接成功回调
        private const int MaxConnectRetries = 3;   //TCP建链最多重试3次（间隔5秒），不再无限循环
        private CancellationTokenSource _connectCts = null;  //CloseAsync 用它取消进行中的重试
        public Action AfterConnected = null;   //TCP真正建链成功后才执行（替代 Thread.Sleep 赌运气）
        //2026-10-31 修复24：三次重试全失败时通知上层回滚槽位（与 ValueSource 库同方案）
        public Action AfterConnectFailed = null;

        public delegate void TurnEquipMent(float[] BackData, bool Ch1);
        public TurnEquipMent thisNetPublicTool = null;

        public delegate void ReciveState(string BackData, Byte EquipAddress, bool Connectflag);
        public ReciveState reciveState = null;
        public byte Chnumber = 0;
        public bool EquipDataRead = true;

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
                    if (reciveState != null)
                    {
                        reciveState("信号源通信建立成功： " + $"Attempting connection to {host}:{port}", 4, true);
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
                            reciveState("信号源通信建立失败（第" + attempt + "次，" + (ReconnectDelay / 1000) + "秒后重试）： " + ex.Message, 4, false);
                        }
                        else
                        {
                            reciveState("信号源通信建立失败（已重试" + MaxConnectRetries + "次，请检查设备电源和网线后重新连接）： " + ex.Message, 4, false);
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
    }
}
