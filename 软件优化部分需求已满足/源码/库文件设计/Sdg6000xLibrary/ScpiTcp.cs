using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sdg6000xLibrary
{
    /// <summary>
    /// 最小 SCPI over TCP 通信助手（同步收发 + 锁保护 + Task 包装异步）。
    /// 2026-10-30 为三个新设备适配库各配一份（库之间保持独立，互不引用）。
    /// </summary>
    public class ScpiTcp : IDisposable
    {
        private TcpClient _client = null;
        private NetworkStream _stream = null;
        private readonly object _lock = new object();   //发收互斥，避免多线程指令交叠

        public bool IsConnected
        {
            get { return _client != null && _client.Connected; }
        }

        /// <summary>连接仪器（重复调用会先断开旧连接）。</summary>
        public void Connect(string host, int port)
        {
            Disconnect();
            _client = new TcpClient();
            _client.Connect(host, port);    //同步连接，失败直接抛异常，由上层提示
            _client.ReceiveTimeout = 5000;
            _client.SendTimeout = 5000;
            _stream = _client.GetStream();
        }

        public void Disconnect()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>发一条指令（自动补换行）。非查询类指令用这个。</summary>
        public void Send(string cmd)
        {
            lock (_lock)
            {
                if (_stream == null) throw new InvalidOperationException("未连接信号源");
                byte[] buf = Encoding.ASCII.GetBytes(cmd + "\n");
                _stream.Write(buf, 0, buf.Length);
                _stream.Flush();
            }
        }

        /// <summary>发查询并等一行应答（读到 \n 为止，超时抛异常）。</summary>
        public string Query(string cmd, int timeoutMs)
        {
            lock (_lock)
            {
                if (_stream == null) throw new InvalidOperationException("未连接信号源");
                byte[] buf = Encoding.ASCII.GetBytes(cmd + "\n");
                _stream.Write(buf, 0, buf.Length);
                _stream.Flush();

                int oldTimeout = _client.ReceiveTimeout;
                _client.ReceiveTimeout = timeoutMs;
                try
                {
                    var sb = new StringBuilder();
                    var one = new byte[1];
                    while (true)
                    {
                        int n = _stream.Read(one, 0, 1);
                        if (n <= 0) break;
                        char c = (char)one[0];
                        if (c == '\n') break;
                        if (c != '\r') sb.Append(c);
                    }
                    return sb.ToString().Trim();
                }
                finally
                {
                    _client.ReceiveTimeout = oldTimeout;
                }
            }
        }

        //===== 异步包装 =====
        public Task SendAsync(string cmd)
        {
            return Task.Run(() => Send(cmd));
        }

        public Task<string> QueryAsync(string cmd, int timeoutMs)
        {
            return Task.Run(() => Query(cmd, timeoutMs));
        }
    }
}
