using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InstrumentInterfaceLibrary;

namespace LeCroyScopeLibrary
{
    /// <summary>
    /// Teledyne LeCroy 示波器适配类（方案A·接口隔离）。
    /// 把 IScope 合同的每条指令翻译成 LeCroy MAUI 方言（见《MAUI远程控制手册》）。
    ///
    /// 已实现：连接/关闭、*IDN?、采集控制(TRMD/ARM/AUTO_SET)、时基(TIME_DIV/TRIG_DELAY)、
    ///         通道(C1:VDIV/C1:OFST/C1:ATTN/C1:CPL 阻抗)、触发(TRSL/TRSE/TRLV/TRMD/INR?)、
    ///         自动测量(PAVA?)、屏幕截图(HCSU+SCDP)、实时波形轮询(WF? 块传输+抽稀1000点)、
    ///         深存储前置(TRMD STOP + MSIZ)、原始指令逃生门(SendCmd/Query)。
    /// 仍不支持：U盘截图（手册 HCSU 的 DEST 选项只有 PRINTER/CLIPBOARD/EMAIL/FILE/REMOTE，
    ///         无 USB；如需"存在示波器本机"可用 FILE 模式，见 SaveimageToUsb 注释）。
    ///
    /// 2026-10-30 最小可用版；2026-10-31 按 MAUI 手册补全波形/测量/阻抗/触发状态。
    /// </summary>
    public class LeCroyScope : IScope
    {
        public ScpiTcp tcp = new ScpiTcp();   //通信助手（public 便于上层做连接体检）

        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "Teledyne LeCroy";

        //===== 连接 =====
        private string _host = null;   //2026-10-31 记录连接参数：I/O 超时重同步（重建会话）要用
        private int _port = 5555;

        public void OpenTranslate(string host, int port)
        {
            _host = host;
            _port = port;
            tcp.Connect(host, port);
            //最小初始化：清状态字 + 停止采集，等操作员在界面上慢慢设置
            try { tcp.Send("*CLS"); } catch { }
            try { tcp.Send("TRMD STOP"); } catch { }
        }

        public void CloseTranslate()
        {
            StopPollThread();   //先停轮询线程，避免它在断线后空转
            tcp.Disconnect();
        }

        public async Task<string> GetIDN()
        {
            return await tcp.QueryAsync("*IDN?", 5000);
        }

        //===== 采集控制（LeCroy 用 TRMD 一组命令兼顾运行/停止/触发方式）=====
        public void Start()   //对应 RUN：连续自动触发
        {
            tcp.Send("TRMD AUTO");
        }

        public void Stop()    //停止采集
        {
            tcp.Send("TRMD STOP");
        }

        public void Single()  //单次采集（ARM 从停止态发起一次采集）
        {
            tcp.Send("ARM");
        }

        public void Auto()    //自动设置
        {
            tcp.Send("AUTO_SET");
        }

        //===== 时基与通道 =====
        public void TimeScale(string sec)
        {
            tcp.Send("TIME_DIV " + sec);
        }

        public void TimeOffset(string sec)
        {
            tcp.Send("TRIG_DELAY " + sec);
        }

        public void ChanScale(string ch, string val)
        {
            tcp.Send(MapCh(ch) + ":VDIV " + val);
        }

        public void ChanOffset(string ch, string val)
        {
            tcp.Send(MapCh(ch) + ":OFST " + val);
        }

        public void ChanImpedance(string ch, string imp)
        {
            //MAUI 手册 7-20 页：耦合/阻抗指令 <channel>:CPL {A1M,D1M,D50,GND}
            //例：C2:CPL D50（50Ω）、EX:CPL D1M（1MΩ）。
            //接口传进来的是 RIGOL 方言 "FIFTy"/"OMEG"，映射到 LeCroy 方言。
            string s = (imp ?? "").ToUpper();
            string cpl;
            if (s.Contains("FIFT") || s.Contains("50") || s == "D50") cpl = "D50";
            else if (s.Contains("A1M")) cpl = "A1M";   //交流耦合 1M（有探头时）
            else cpl = "D1M";
            tcp.Send(MapCh(ch) + ":CPL " + cpl);
        }

        public void ChanProbe(string ch, string att)
        {
            tcp.Send(MapCh(ch) + ":ATTN " + att);
        }

        //===== 触发 =====
        public void TriggerMode(bool upTriggMode)   //true=上升沿 false=下降沿
        {
            tcp.Send(upTriggMode ? "TRSL POS" : "TRSL NEG");
        }

        public void TriggerSource(string src)       //"CHAN1"~"CHAN4"
        {
            //TRSE 语法：边沿触发,源=CX,保持类型=TL(无保持)
            tcp.Send("TRSE EDGE,SR," + MapCh(src) + ",HT,TL");
        }

        public void TriggerLevel(double volts)
        {
            tcp.Send("TRLV " + volts.ToString("0.###"));
        }

        public void TriggerSweep(string sweep)      //"AUTO"/"NORM"/"SING"（RIGOL 方言映射到 TRMD）
        {
            string s = sweep.ToUpper();
            if (s.StartsWith("SING")) tcp.Send("TRMD SINGLE");
            else if (s.StartsWith("NORM")) tcp.Send("TRMD NORM");
            else tcp.Send("TRMD AUTO");
        }

        public async Task<string> GetTriggerStatus()
        {
            //MAUI 手册 6-10 页：INR 内部状态变化寄存器，bit0=新波形已采得（读后自动清除）。
            //返回 RIGOL 风格的状态字：有 bit0 → "TD"（已触发），否则 "WAIT"。
            string r = await tcp.QueryAsync("INR?", 5000);
            int inr = ExtractLeadingInt(r);
            return (inr & 0x01) != 0 ? "TD" : "WAIT";
        }

        //===== 测量与杂项 =====
        public async Task<string> MeasureItemValue(string item, string ch)
        {
            //MAUI 手册 7-70 页：<trace>:PAVA?<测量项> 直接读标准测量值，无需 PACU 预配置。
            //例：C1:PAVA?RISE → "RISE,3.6E-9S,OK"。
            //item 做一层方言映射（RIGOL 风格 → LeCroy 助记符），映射不上就原样发（示波器会拒绝并蜂鸣）。
            string mnemonic = MapMeasureItem(item);
            //测量前可能有校准，超时给足 15 秒（手册 6-15 页建议）
            return await tcp.QueryAsync(MapCh(ch) + ":PAVA?" + mnemonic, 15000);
        }

        public void SaveimageToUsb()
        {
            //MAUI 手册 7-102 页：HCSU 的 DEST 选项为 {PRINTER,CLIPBOARD,EMAIL,FILE,REMOTE}，
            //没有 U 盘选项——"存 U 盘"在 LeCroy 上确实不存在对应功能。
            //DEST,FILE 是存到示波器本机硬盘（DIR 是示波器上的 DOS 路径），语义不同，不冒充。
            //如确实需要"存在示波器那边回头去取"，可改用：
            //   tcp.Send("HCSU DEV,PNG,DEST,FILE,DIR,\"D:\\HARDCOPY\",FILE,\"SHOT\"");
            //   tcp.Send("SCDP");
            throw new NotSupportedException("LeCroy 无 U 盘截图指令（手册 DEST 无 USB 选项），请用 SaveImage 保存到电脑，或修改代码用 FILE 模式存示波器本机硬盘");
        }

        public void SaveImage(string path)
        {
            //HARDCOPY_SETUP 配置截图格式与落盘路径，SCREEN_DUMP 触发截图。
            //路径形如 D:\\MHO984\\shot.png（转义由上层保证）。真机验证后如有偏差再调。
            tcp.Send("HCSU DEV,PNG,DEST,\"" + path + "\"");
            tcp.Send("SCDP");
        }

        public void SendCmd(string scpiCmd)
        {
            tcp.Send(scpiCmd);
        }

        public async Task<string> Query(string scpiCmd, int timeoutMs = 5000)
        {
            return await tcp.QueryAsync(scpiCmd, timeoutMs);
        }

        //===== 数据轮询：后台线程按 ChannelMask 循环读各通道波形，抽稀到1000点后回调 =====
        //与 RIGOL 的"仪器主动推"不同，LeCroy 是"软件主动拉"：轮询线程定期发 Cx:WF? 读全波形。

        /// <summary>实时波形回调：抽稀后的1000点电压数组 + 通道号(0~3) + 时间轴（与 RIGOL 侧 SomeActMethod 签名一致）。</summary>
        public event Action<float[], byte, float, float> WaveformReceived;

        private bool _dataReadEnabled = false;
        private byte _channelMask = 0x01;
        private Thread _pollThread = null;
        private volatile bool _pollStop = false;

        /// <summary>总开关：true 启动轮询线程，false 停止。</summary>
        public bool DataReadEnabled
        {
            get { return _dataReadEnabled; }
            set
            {
                if (value == _dataReadEnabled) return;
                _dataReadEnabled = value;
                if (value) StartPollThread();
                else StopPollThread();
            }
        }

        /// <summary>通道使能掩码：Bit0~Bit3 对应 CH1~CH4。轮询线程每轮只读使能的通道。</summary>
        public byte ChannelMask
        {
            get { return _channelMask; }
            set { _channelMask = value; }
        }

        /// <summary>兼容属性：LeCroy 每轮读所有使能通道，此属性仅记录上次读到的通道号，无控制作用。</summary>
        public int ReadChannelIndex { get; set; } = 0;

        private void StartPollThread()
        {
            StopPollThread();
            _pollStop = false;
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "LeCroyWavePoll" };
            _pollThread.Start();
        }

        private void StopPollThread()
        {
            _pollStop = true;
            if (_pollThread != null)
            {
                try { _pollThread.Join(1500); } catch { }
                _pollThread = null;
            }
        }

        private void PollLoop()
        {
            //轮询前置：二进制块格式 + 小端字节序 + 关闭响应头（幂等，发错也无妨）
            try { tcp.Send("COMM_FORMAT DEF9,WORD,BIN"); } catch { }
            try { tcp.Send("COMM_ORDER LO"); } catch { }
            try { tcp.Send("COMM_HEADER OFF"); } catch { }

            while (!_pollStop)
            {
                try
                {
                    for (int ci = 0; ci < 4 && !_pollStop; ci++)
                    {
                        if ((_channelMask & (1 << ci)) == 0) continue;
                        LeCroyWaveform wf = ReadWaveform("C" + (ci + 1), 15000);
                        if (wf == null) continue;
                        ReadChannelIndex = ci;
                        if (_deepSaveActive)
                        {
                            //深存储模式：推全部原始点（不抽稀），且只收"够深"的帧，
                            //避免把 ARM 前的旧浅缓存收进去。applied 以实际回读点数为准。
                            if (wf.RawCount < _deepSavePointsRequested) continue;
                            if (_deepSavePointsApplied == 0) _deepSavePointsApplied = wf.RawCount;
                            WaveformReceived?.Invoke(wf.Volts, (byte)ci, wf.XIncrement, wf.XOrigin);
                        }
                        else
                        {
                            float[] data;
                            float xInc;
                            Downsample1000(wf, out data, out xInc);
                            WaveformReceived?.Invoke(data, (byte)ci, xInc, wf.XOrigin);
                        }
                    }
                }
                catch (Exception ex)
                {
                    //单轮失败（断线、查询超时）不退出线程，留给下一轮重试；断线时重试由 ScpiTcp 抛异常持续失败
                    System.Diagnostics.Debug.WriteLine("[LeCroy] 轮询异常：" + ex.Message);
                }
                Thread.Sleep(300);   //轮询间隔：300ms 一档，兼顾流畅度与仪器负载
            }
        }

        /// <summary>把任意点数波形抽稀到1000点（主界面 SomeActMethod 硬编码按1000点处理）。
        /// 点数不足1000时原样返回。</summary>
        private static void Downsample1000(LeCroyWaveform wf, out float[] data, out float xInc)
        {
            int count = wf.Volts.Length;
            if (count <= 1000)
            {
                data = wf.Volts;
                xInc = wf.XIncrement;
                return;
            }
            data = new float[1000];
            double step = (double)count / 1000.0;
            for (int i = 0; i < 1000; i++)
            {
                int src = (int)(i * step);
                if (src >= count) src = count - 1;
                data[i] = wf.Volts[src];
            }
            xInc = (float)(wf.XIncrement * step);   //抽稀后相邻点的时间间隔同步放大
        }

        //===== 深存储：停采集 + 选存储档位 + 重新采集一次，数据由轮询线程按原始点推回 =====
        public bool SupportDeepSave => true;   //2026-10-31 已接通主窗口保存流程（与 RIGOL 同一回调链路）
        private bool _deepSaveActive = false;
        public bool DeepSaveActive { get { return _deepSaveActive; } set { _deepSaveActive = value; } }
        private int _deepSavePointsApplied = 0;
        public int DeepSavePointsApplied => _deepSavePointsApplied;
        private int _deepSavePointsRequested = 0;   //本次请求点数，轮询线程用它过滤旧浅缓存帧

        //MSIZ 档位表（MAUI 手册 7-21 页）：500,1000,2500,5000,10K,25K,50K,100K,250K,500K,1M…
        private static readonly int[] MsizSteps =
            { 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000,
              2500000, 5000000, 10000000, 25000000, 50000000, 100000000, 250000000, 500000000 };
        private static readonly string[] MsizCmds =
            { "500", "1000", "2500", "5000", "10K", "25K", "50K", "100K", "250K", "500K", "1MA",
              "2.5MA", "5MA", "10MA", "25MA", "50MA", "100MA", "250MA", "500MA" };

        /// <summary>开始深存储采集。时序按 MAUI 手册 6-15 页推荐的同步流程：
        /// STOP（冻结旧波形）→ MSIZ 选档 + MSIZ? 回读确认 → 预读 INR? 清旧标志 →
        /// ARM 发起单次采集 → WAIT 15（仪器侧等触发+采集，超时自动放行）→
        /// *OPC? 单次长等（客户端超时 20 秒 &gt; WAIT 15 秒，留处理余量）→
        /// 再读 INR? 验 bit0："确实有新波形"（WAIT 超时不保证触发成功，*OPC? 返回 1 也可能是空等）。
        /// 任一步失败都中止保存，不把半成品写进 Excel。深存储数据由轮询线程按原始点推回。</summary>
        public void BeginDeepSave(int points)
        {
            _deepSavePointsRequested = points;
            _deepSavePointsApplied = 0;
            _deepSaveActive = true;
            //2026-10-31 修复旧帧竞态：整个"STOP→换档→ARM→等新波形"期间挂起轮询线程。
            //否则 STOP 定格的是旧波形，若旧深度≥本次请求点数，旧帧会被收进 Excel
            //（连续两次深存储、或请求≤仪器当前档位时必中招）。验证通过后再恢复轮询。
            DataReadEnabled = false;

            tcp.Send("TRMD STOP");
            int idx = 0;
            for (int i = 0; i < MsizSteps.Length; i++) { if (MsizSteps[i] >= points) { idx = i; break; } idx = i; }
            try
            {
                tcp.Send("MSIZ " + MsizCmds[idx]);
                _deepSavePointsApplied = 0;   //先置0：实际点数等第一帧回读后定（时基可能限制不满档位）
                //MSIZ? 回读确认实际生效的档位（评审意见：设置后必须验证，不能假设成功）
                string msizBack = tcp.Query("MSIZ?", 5000);
                System.Diagnostics.Debug.WriteLine("[LeCroy深存储] MSIZ 回读：" + msizBack + "（请求 " + MsizCmds[idx] + "）");
            }
            catch
            {
                //MSIZ 被拒（机型不支持该档）时保持原档，不中断采集冻结
            }

            //预读一次 INR?：读取会清零"新波形已采集"标志，防止把 ARM 之前的旧标志当成新波形
            try { tcp.Query("INR?", 5000); } catch { }

            tcp.Send("ARM");
            tcp.Send("WAIT 15");   //仪器侧等待触发+采集完成，最长 15 秒后自动放行（不等应答）
            try
            {
                WaitAcquisitionComplete(20000);   //*OPC? 单次长等：超时说明仪器卡死/断线，走重同步
            }
            catch
            {
                ResyncAfterTimeout();   //清空悬挂应答 + 重建会话 + 恢复运行，再抛给主窗口报告
                throw;
            }

            //INR? bit0 = 自上次读取以来有新波形采集。bit0 未置位说明 WAIT 是超时放行的：
            //脉冲没发出或触发条件不满足，手里的是旧波形，必须中止
            int inr = 0;
            try { inr = ExtractLeadingInt(tcp.Query("INR?", 5000)); } catch { }
            if ((inr & 1) == 0)
            {
                _deepSaveActive = false;
                try { tcp.Send("TRMD AUTO"); } catch { }
                DataReadEnabled = true;   //中止也要恢复轮询，否则主界面波形停刷
                throw new TimeoutException("15 秒内未触发新采集（脉冲未发出或触发条件不满足），未取得新波形，保存已中止");
            }

            //验证通过：恢复的轮询线程此后读到的必然是 ARM 之后的新波形
            DataReadEnabled = true;
        }

        /// <summary>用 *OPC? 等待处理子系统完成（MAUI 手册 6-15 页推荐做法）。
        /// 单次长等：仪器在 ARM/WAIT 完成前不回应 *OPC?，回应即为 1。
        /// 不能用短超时轮询——每次超时都会留下一个悬挂的"1"应答在接收缓冲里，
        /// 会污染后续 WF? 的二进制块流。调用方给的超时必须大于前面 WAIT 的秒数。</summary>
        private void WaitAcquisitionComplete(int timeoutMs)
        {
            string r = tcp.Query("*OPC?", timeoutMs);
            if (ExtractLeadingInt(r) == 1) return;
            throw new TimeoutException("示波器采集超时：" + (timeoutMs / 1000) + " 秒内 *OPC? 未确认完成（未触发/采集未完成）");
        }

        /// <summary>*OPC? 客户端超时后的重同步（评审意见）：
        /// 超时的瞬间清空缓冲区不够——迟到的"1"可能在清空之后才到。
        /// 所以先尽力清输入缓冲，再直接重建 TCP 会话（旧连接连同悬挂应答一起丢弃），
        /// 重发随会话丢失的 COMM_* 格式指令，恢复示波器连续运行。之后禁止残留旧状态。</summary>
        private void ResyncAfterTimeout()
        {
            _deepSaveActive = false;
            _deepSavePointsRequested = 0;
            try { tcp.ClearInputBuffer(); } catch { }
            try
            {
                tcp.Reconnect();
                //COMM_* 设置跟随会话，重连后必须重发，否则 WF? 块格式/字节序可能回到默认
                try { tcp.Send("COMM_FORMAT DEF9,WORD,BIN"); } catch { }
                try { tcp.Send("COMM_ORDER LO"); } catch { }
                try { tcp.Send("COMM_HEADER OFF"); } catch { }
                try { tcp.Send("TRMD AUTO"); } catch { }
                DataReadEnabled = true;   //I/O 超时中止后同样恢复轮询
                System.Diagnostics.Debug.WriteLine("[LeCroy深存储] I/O 超时，已重建会话并恢复运行");
            }
            catch (Exception rex)
            {
                //重连也失败（示波器断电/断网）：保持断线状态，由操作员重连；轮询线程会自行重试
                System.Diagnostics.Debug.WriteLine("[LeCroy深存储] 重同步失败：" + rex.Message);
            }
        }

        /// <summary>结束深存储采集：退出深收集模式，恢复自动触发连续采集。</summary>
        public void EndDeepSave()
        {
            _deepSaveActive = false;
            _deepSavePointsRequested = 0;
            tcp.Send("TRMD AUTO");
        }

        //===== 波形读取：Cx:WF? 块传输 + WAVEDESC 解析（MAUI 手册 6-18~6-27 页）=====

        /// <summary>一次 WF? 查询的结果。Volts 已按增益/偏移换算成电压；时间轴为 XOrigin + i*XIncrement。</summary>
        public class LeCroyWaveform
        {
            public float[] Volts;       //原始点数的电压数组（未抽稀）
            public float XIncrement;    //相邻点采样间隔（秒）= 描述符 HORIZONTAL_INTERVAL
            public float XOrigin;       //第0点时刻（秒）= HORIZONTAL_OFFSET（负值=触发前）
            public int RawCount;        //原始点数 = WAVE_ARRAY_COUNT
        }

        /// <summary>读指定通道的完整波形。ch 可传 "C1"~"C4" 或 "CHANnel1"。失败抛异常，无数据返回 null。</summary>
        public LeCroyWaveform ReadWaveform(string ch, int timeoutMs = 30000)
        {
            var blocks = tcp.QueryBlockSequence(MapCh(ch) + ":WF?", timeoutMs);
            if (blocks.Count < 1) return null;
            byte[] desc = blocks[0];
            if (desc.Length < 344) throw new IOException("WAVEDESC 块长度不足：" + desc.Length);

            //2026-10-31 修复18：原校验 desc[0]=='W' 对字节序不敏感（'W' 是单字节，两种序都是 0x57），拦不住大端。
            //按 MAUI 波形模板 / RigolWFM 的做法：读 offset 34 的 COMM_ORDER（u2，恒按大端读），
            //0=HIFIRST(大端)、1=LOFIRST(小端)，后续多字节字段按它选字节序
            if (desc[0] != (byte)'W') throw new IOException("WAVEDESC 头校验失败（首字节不是 'W'）");
            bool littleEndian;
            {
                //2026-10-31 修复18（审查修订版）：COMM_ORDER 字段自身跟随描述符字节序，两种合法布局——
                //HIFIRST(大端)：offset34 两字节为 [0x00,0x00]，大端读=0；
                //LOFIRST(小端)：为 [0x01,0x00]，大端读=256、小端读=1。
                //两种自洽读法都试，避免单一读法在某种仪器行为下误判（逻辑审查发现的歧义）
                int be = (desc[34] << 8) | desc[35];
                int le = desc[34] | (desc[35] << 8);
                int commOrder = (be == 0 || be == 1) ? be : le;
                if (commOrder != 0 && commOrder != 1)
                    throw new IOException("WAVEDESC COMM_ORDER 无法识别（offset34 大端读=" + be + " 小端读=" + le + "）");
                littleEndian = (commOrder == 1);
                if (!littleEndian)
                    Console.WriteLine("[LeCroy] 警告：仪器处于 HIFIRST(大端) 模式，已按大端解析 WAVEDESC");
            }

            //按 COMM_ORDER 选字节序的字段读取（BitConverter 只按本机小端，大端模式需手动翻转）
            short ToI16(int o) => littleEndian ? BitConverter.ToInt16(desc, o) : (short)((desc[o] << 8) | desc[o + 1]);
            int ToI32(int o) => littleEndian ? BitConverter.ToInt32(desc, o)
                                             : (desc[o] << 24) | (desc[o + 1] << 16) | (desc[o + 2] << 8) | desc[o + 3];
            float ToF32(int o)
            {
                byte[] t = new byte[4];
                Array.Copy(desc, o, t, 0, 4);
                if (!littleEndian) Array.Reverse(t);
                return BitConverter.ToSingle(t, 0);
            }
            double ToF64(int o)
            {
                byte[] t = new byte[8];
                Array.Copy(desc, o, t, 0, 8);
                if (!littleEndian) Array.Reverse(t);
                return BitConverter.ToDouble(t, 0);
            }

            int commType = ToI16(32);          //0=BYTE 1=WORD
            int userTextLen = ToI32(40);
            int trigTimeLen = ToI32(48);
            int risTimeLen = ToI32(52);
            int wave1Len = ToI32(60);
            int rawCount = ToI32(116);
            float vGain = ToF32(156);
            float vOff = ToF32(160);
            float hInt = ToF32(176);
            double hOff = ToF64(180);

            if (wave1Len <= 0) return null;   //无数据块（通道未开或未采集）

            //块顺序：WAVEDESC, USERTEXT, TRIGTIME, RISTIME, WAVE_ARRAY_1, WAVE_ARRAY_2
            //长度为零的块整块不出现，所以 WAVE_ARRAY_1 的下标 = 1 + 前面非零块数
            int idx = 1;
            if (userTextLen > 0) idx++;
            if (trigTimeLen > 0) idx++;
            if (risTimeLen > 0) idx++;
            if (blocks.Count <= idx) throw new IOException("WF? 响应缺少 WAVE_ARRAY_1 块（共 " + blocks.Count + " 块）");
            byte[] raw = blocks[idx];

            var volts = new float[rawCount];
            if (commType == 1)   //WORD：16bit 有符号（字节序随 COMM_ORDER，见修复18）
            {
                for (int i = 0; i < rawCount && i * 2 + 1 < raw.Length; i++)
                {
                    short v = littleEndian ? BitConverter.ToInt16(raw, i * 2)
                                           : (short)((raw[i * 2] << 8) | raw[i * 2 + 1]);
                    volts[i] = v * vGain - vOff;
                }
            }
            else                 //BYTE：8bit 有符号
            {
                for (int i = 0; i < rawCount && i < raw.Length; i++)
                {
                    sbyte v = (sbyte)raw[i];
                    volts[i] = v * vGain - vOff;
                }
            }

            return new LeCroyWaveform
            {
                Volts = volts,
                XIncrement = hInt,
                XOrigin = (float)hOff,
                RawCount = rawCount
            };
        }

        //===== 内部工具 =====

        /// <summary>把 "CHANnel1"/"CHAN1"/"C1" 统一映射成 "C1"。</summary>
        private static string MapCh(string ch)
        {
            if (string.IsNullOrWhiteSpace(ch)) return "C1";
            string s = ch.Trim().ToUpper().Replace(" ", "");
            if (s.StartsWith("C") && s.Length == 2 && s[1] >= '1' && s[1] <= '4') return s;
            int idx = s.IndexOf('L');   //CHANnel1 / CHANNEL1 → 取数字
            string num = idx >= 0 ? s.Substring(idx + 1) : s.Substring(s.Length - 1);
            if (num.Length >= 1 && num[0] >= '1' && num[0] <= '4') return "C" + num[0];
            return "C1";
        }

        /// <summary>RIGOL 风格测量项名 → LeCroy PAVA 助记符（MAUI 手册 7-60 页测量表）。</summary>
        private static string MapMeasureItem(string item)
        {
            if (string.IsNullOrWhiteSpace(item)) return "PKPK";
            string s = item.Trim().ToUpper();
            switch (s)
            {
                case "VPP": case "VPPP": case "PKPK": return "PKPK";
                case "VMAX": case "MAX": return "MAX";
                case "VMIN": case "MIN": return "MIN";
                case "VAVG": case "AVERAGES": case "MEAN": return "MEAN";
                case "VRMS": case "RMS": return "RMS";
                case "FREQ": case "FREQUENCY": return "FREQ";
                case "PERIOD": case "PER": return "PER";
                case "RISETIME": case "RISE": return "RISE";
                case "FALLTIME": case "FALL": return "FALL";
                case "PWIDth": case "PWIDTH": case "WID": return "WID";
                case "NWIDth": case "NWIDTH": case "WIDN": return "WIDN";
                case "PDUTY": case "DUTY": return "DUTY";
                case "AMPLitude": case "AMPL": return "AMPL";
                case "TOP": return "TOP";
                case "BASE": return "BASE";
                case "AREA": return "AREA";
                case "SDEV": return "SDEV";
                case "OVSP": return "OVSP";   //正过冲
                case "OVSN": return "OVSN";   //负过冲
                default: return s;            //已是助记符或未知项：原样发，让仪器报错
            }
        }

        /// <summary>从仪器响应里提取开头的整数（兼容 "INR 0"/"0"/"INR0" 等带响应头形式）。</summary>
        private static int ExtractLeadingInt(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return 0;
            string s = response.Trim();
            int start = 0;
            while (start < s.Length && !char.IsDigit(s[start]) && s[start] != '-') start++;
            int end = start;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            int v;
            return int.TryParse(s.Substring(start, end - start), out v) ? v : 0;
        }
    }
}
