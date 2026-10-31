using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SingalClassLibrary
{
    /// <summary>
    /// 信号源通信类。
    /// 与示波器/电压源的通信类不同——它基本是"半残"状态：
    /// 发送功能完整，接收功能是个空壳（HandleIncomingData 的 try 里什么都没有）。
    /// 这不算 bug 而是"够用"：信号源是主从模式里的"从"，只接收指令、几乎不需要上报数据，
    /// 学长因此没写接收解析。但连*IDN?*这类查询也就没法用了。
    /// 另一个后果：连接状态只有"连上"那一刻的回调，使用中断线无任何感知。
    /// （破案提示：主程序报错"代码和我说的不一样"时，打开的很可能就是这个文件——
    ///   三个库各有一个同名 TcpTranslateClass.cs，只有示波器那个有完整的接收代码。）
    /// </summary>
    public class TcpTranslateClass
    {
        private TcpClient _client;
        private const int ReconnectDelay = 5000;
        //private Byte ReciveDataLen = 8;
        private List<byte> mRecvBuffer = new List<byte>();   // ⚠ 从未使用（连解析都没有，自然用不上）
        public string ChannelName = "OnGet";//0 电压；2 电流   // ⚠ 注释复制自电压源，与实际无关
        public float[] Line1Data = new float[300];            // ⚠ 从未使用
        public bool Ch1 = false;

        public delegate void TurnEquipMent(float[] BackData, bool Ch1);   // ⚠ 从未被触发（没人调用它）
        public TurnEquipMent thisNetPublicTool = null;        // ⚠ 主程序从未给信号源挂数据回调

        public delegate void ReciveState(string BackData, Byte EquipAddress, bool Connectflag);
        public ReciveState reciveState = null;
        public byte Chnumber = 0;          // ⚠ 复制模板留下的摆设字段
        public bool EquipDataRead = true;  // ⚠ 同上，摆设

        /// <summary>
        /// 建立 TCP 连接（死循环重试直到成功）。
        /// 与示波器版几乎一样，只是回调文案改成"信号源"。
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
                    _ = Task.Run(() => HandleIncomingData()); // 启动接收线程（但里面是空的…）
                    if (reciveState != null)
                    {
                        reciveState("信号源通信建立成功： " + $"Attempting connection to {host}:{port}", 4, true);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    //网络连接异常回调
                    Console.WriteLine($"Connection failed: {ex.Message}");
                    if (reciveState != null)
                    {
                        reciveState("信号源通信建立失败： " + $"Connection failed: {ex.Message}",4,true);
                        // ⚠ 同样笔误：失败也传 true
                    }
                    await Task.Delay(ReconnectDelay);
                }
            }
        }

        /// <summary>
        /// 接收线程——空壳。try 块里没有任何代码。
        /// 仪器发来的数据（如果有）永远躺在 socket 缓冲区里没人读，
        /// 积累多了可能撑满仪器发送缓冲导致仪器侧报错（对普源DG系列实测影响极小）。
        /// </summary>
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

        /// <summary>
        /// 发送指令（信号源所有指令的唯一出口，主程序的那些波形下载全靠它）。
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
    }
}
