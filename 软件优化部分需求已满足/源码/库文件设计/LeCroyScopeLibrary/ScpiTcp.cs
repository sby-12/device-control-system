using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LeCroyScopeLibrary
{
    /// <summary>
    /// 最小 SCPI over TCP 通信助手（同步收发 + 锁保护 + Task 包装异步）。
    /// 与现有各仪器库的 TcpTranslateClass 类似但大幅精简：
    /// 只负责"发一行指令、读一行应答"，不做波形轮询和数据回调。
    /// 2026-10-30 为三个新设备适配库各配一份（库之间保持独立，互不引用）。
    /// </summary>
    public class ScpiTcp : IDisposable
    {
        private TcpClient _client = null;
        private NetworkStream _stream = null;
        private readonly object _lock = new object();   //发收互斥，避免多线程指令交叠
        private string _host = null;    //2026-10-31 记录连接参数：I/O 超时后重同步（重建会话）要用
        private int _port = 5555;

        public bool IsConnected
        {
            get { return _client != null && _client.Connected; }
        }

        /// <summary>连接仪器（重复调用会先断开旧连接）。</summary>
        public void Connect(string host, int port)
        {
            Disconnect();
            _host = host;
            _port = port;
            _client = new TcpClient();
            _client.Connect(host, port);    //同步连接，失败直接抛异常，由上层提示
            _client.ReceiveTimeout = 5000;
            _client.SendTimeout = 5000;
            _stream = _client.GetStream();
        }

        /// <summary>I/O 超时后的重同步：重建通信会话，丢弃旧连接上可能迟到的悬挂应答。
        /// 场景：*OPC? 长等客户端超时后，仪器稍后仍可能补发应答"1"，
        /// 不清掉就会污染后续 WF? 的二进制块流。重建会话是最干净的清理方式。</summary>
        public void Reconnect()
        {
            if (_host == null) throw new InvalidOperationException("尚未连接过，无法重连");
            Disconnect();
            _client = new TcpClient();
            _client.Connect(_host, _port);
            _client.ReceiveTimeout = 5000;
            _client.SendTimeout = 5000;
            _stream = _client.GetStream();
        }

        /// <summary>非阻塞清空接收缓冲区（重连前调用，尽量把已到站的迟到应答丢掉）。</summary>
        public void ClearInputBuffer()
        {
            lock (_lock)
            {
                if (_stream == null) return;
                var buf = new byte[4096];
                while (_client.Available > 0)
                {
                    int n = _stream.Read(buf, 0, Math.Min(buf.Length, _client.Available));
                    if (n <= 0) break;
                }
            }
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
                if (_stream == null) throw new InvalidOperationException("未连接示波器");
                byte[] buf = Encoding.ASCII.GetBytes(cmd + "\n");
                _stream.Write(buf, 0, buf.Length);
                _stream.Flush();
            }
        }

        /// <summary>发查询并读一行应答（读到 \n 为止，超时抛异常）。</summary>
        public string Query(string cmd, int timeoutMs)
        {
            lock (_lock)
            {
                if (_stream == null) throw new InvalidOperationException("未连接示波器");
                byte[] buf = Encoding.ASCII.GetBytes(cmd + "\n");
                _stream.Write(buf, 0, buf.Length);
                _stream.Flush();

                //临时延长读超时：LeCroy 部分查询（截图准备、测量值）应答较慢
                int oldTimeout = _client.ReceiveTimeout;
                _client.ReceiveTimeout = timeoutMs;
                try
                {
                    var sb = new StringBuilder();
                    var one = new byte[1];
                    while (true)
                    {
                        int n = _stream.Read(one, 0, 1);   //ReceiveTimeout 到时会抛 SocketException
                        if (n <= 0) break;                 //对方关连接
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

        //===== 波形块传输（WF? 专用，IEEE488.2 定长块序列）=====
        //响应格式：若干 "#N<N位ASCII长度><数据>" 块顺序拼接，块数由仪器决定
        //（WAVEDESC、USERTEXT、TRIGTIME、RISTIME、WAVE_ARRAY_1、WAVE_ARRAY_2，
        //  长度为零的块不出现），最后跟一个换行符。
        //策略：第一块用 timeoutMs 硬等；后续块用空闲超时 idleMs 判断"还有没有下一块"。

        /// <summary>发波形查询，读取全部定长块（按出现顺序返回每块的纯数据）。</summary>
        public System.Collections.Generic.List<byte[]> QueryBlockSequence(string cmd, int timeoutMs, int idleMs = 800)
        {
            lock (_lock)
            {
                if (_stream == null) throw new InvalidOperationException("未连接示波器");
                byte[] buf = Encoding.ASCII.GetBytes(cmd + "\n");
                _stream.Write(buf, 0, buf.Length);
                _stream.Flush();

                int oldTimeout = _client.ReceiveTimeout;
                _client.ReceiveTimeout = timeoutMs;
                try
                {
                    var blocks = new System.Collections.Generic.List<byte[]>();
                    blocks.Add(ReadOneBlock());                 //第一块：WAVEDESC，必须有
                    //2026-10-31 修复19：块内字节间超时放宽到至少5秒。原来沿用 idleMs(800ms)，
                    //深存储 100K 点大块在弱网/仪器忙时块内停顿超 800ms 就整帧作废
                    _client.ReceiveTimeout = Math.Max(idleMs, 5000);   //后续块改空闲超时
                    while (true)
                    {
                        if (!WaitDataAvailable(idleMs)) break;  //空闲超时=没有更多块
                        blocks.Add(ReadOneBlock());
                    }
                    ConsumeTrailingNewline(idleMs);             //吃掉块序列末尾的换行，防止污染下一次查询
                    return blocks;
                }
                finally
                {
                    _client.ReceiveTimeout = oldTimeout;
                }
            }
        }

        /// <summary>读取一个 "#N<长度><数据>" 块，返回纯数据部分。</summary>
        private byte[] ReadOneBlock()
        {
            int sharp = _stream.ReadByte();
            if (sharp != '#') throw new IOException("WF? 响应缺少块起始符 '#'（实际收到：" + sharp + "）");
            int nDigit = _stream.ReadByte() - '0';
            if (nDigit < 1 || nDigit > 9) throw new IOException("WF? 块长度位数非法：" + nDigit);
            var lenBuf = new byte[nDigit];
            ReadFull(lenBuf, 0, nDigit);
            long len = long.Parse(Encoding.ASCII.GetString(lenBuf));
            if (len < 0 || len > 512L * 1024 * 1024) throw new IOException("WF? 块长度异常：" + len);
            var payload = new byte[len];
            ReadFull(payload, 0, (int)len);
            return payload;
        }

        private void ReadFull(byte[] buf, int off, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = _stream.Read(buf, off + got, count - got);
                if (n <= 0) throw new IOException("连接已断开");
                got += n;
            }
        }

        /// <summary>等待数据到达（最多等 maxMs），超时空闲返回 false。</summary>
        private bool WaitDataAvailable(int maxMs)
        {
            int waited = 0;
            while (!_stream.DataAvailable && waited < maxMs)
            {
                Thread.Sleep(10);
                waited += 10;
            }
            return _stream.DataAvailable;
        }

        /// <summary>块序列末尾有一个换行符，必须在锁内吃掉，否则下一次逐字节查询会读到它立即返回空串。</summary>
        private void ConsumeTrailingNewline(int waitMs)
        {
            if (WaitDataAvailable(waitMs))
            {
                int b = _stream.ReadByte();
                //如果不是换行（理论上不会），只能丢弃——TCP 流已错位，记日志让上层排查
                if (b != '\n' && b >= 0) System.Diagnostics.Debug.WriteLine("[ScpiTcp] 块序列末尾出现非换行字节：" + b);
            }
        }

        //===== 异步包装：接口成员都是 Task 签名，内部仍走同步锁 =====
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
