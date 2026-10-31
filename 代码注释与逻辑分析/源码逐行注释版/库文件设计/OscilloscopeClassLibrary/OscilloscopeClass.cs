using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;               // Thread.Sleep
using System.Threading.Tasks;
using System.Timers;                  // System.Timers.Timer（1秒定时取波形的那个定时器）

namespace OscilloscopeClassLibrary
{
    /// <summary>
    /// 示波器驱动类（SCPI 文本协议）。
    /// 两个角色：
    /// ① 翻译官——每个 public 方法把界面操作翻译成一条 SCPI 指令发出去；
    /// ② 调度员——内部 1 秒定时器 + 与 TcpTranslateClass 配合，周期性地"问时间参数→问波形数据"。
    /// 通信细节全在 TcpTranslateClass 里，这里只管"说什么"。
    /// </summary>
    public class OscilloscopeClass
    {

        public TcpTranslateClass translateClass;   // 通信对象（TCP+解析+回调）
        System.Timers.Timer ThisEquitRequestTime = null;  // 1秒定时器（驱动周期取数）
        public byte CHANNum = 1;//Bit0 通道1 Bit1 通道2 Bit3 通道3 Bit4 通道4
        // ⚠ 注意：这个字段是"备份"，真正生效的是 translateClass.CHANNum（两处初始值一致，
        //    主程序勾选框改的是 translateClass 里的那个，本字段此后无人再读）

        public OscilloscopeClass()
        {
            translateClass = new TcpTranslateClass();

            ThisEquitRequestTime = new System.Timers.Timer(1000);//20ms进入一次
            // ⚠ 注释笔误：实际是 1000ms（1秒），不是 20ms
            ThisEquitRequestTime.Elapsed += new System.Timers.ElapsedEventHandler(ThisEquitRequestTime_Event);
            ThisEquitRequestTime.AutoReset = true;   // 循环触发（到点就响，响完自动复位）
            ThisEquitRequestTime.Enabled = false;    // 先不响，连接成功后由 InitOscillPrame 启动
        }

        //通信连接
        public void OpenTranslate(string host, int port)
        {
            _ = translateClass.ConnectAsync(host, port);
            // ↑ _ = 是 C# 的"我不关心这个异步任务的返回值，别警告我"写法
            InitOscillPrame("CHAN1");
            // ⚠ 潜在问题：ConnectAsync 是后台异步连接（可能要重试几秒），
            //    这里紧接着就发初始化指令，指令可能发到还没连上的 socket 里被丢弃。
            //    实际靠 SendAsync 内部的 _client?.Connected 检查侥幸挡住，属于"竞态但没炸"。
            // ThisEquitRequestTime.Start();
        }

        //通信关闭
        public void CloseTranslate()
        {
            ThisEquitRequestTime.Stop();
            translateClass.CloseAsync();
        }

        // 问仪器当前波形源是哪个通道（连接弹窗里调用）
        public void FindChannel()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce?" + "\n"));
        }

        public string GetChannel()
        {
            return translateClass.ChannelName;
        }
        //对矢网进行初始设置
        /// <summary>
        /// 示波器初始化：连接成功后对 4 个通道依次设置——
        /// 波形源、边沿触发、普通模式、ASCII 格式、1000 点、存储深度。
        /// 每条指令间 Thread.Sleep(2) 是怕仪器处理不过来（经验值，不严谨但有效）。
        /// 方法名注释写"矢网"是复制粘贴遗留（矢网=矢量网络分析仪，另一台仪器）。
        /// </summary>
        public void InitOscillPrame(string ChannelName)
        {
            //通道1
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN1" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:MODE EDGE" + "\n"));//触发模式设置为边沿触发
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:FORMat ASCii" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts 1000" + "\n"));
            //通道2
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            // ⚠ ACQuire:MDEP 1 = 存储深度设为1（普源系指令），与下面的 POINts 1000 的关系是仪器内部细节
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN2" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:FORMat ASCii" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts 1000" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            Thread.Sleep(2);
            //通道3
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN3" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:FORMat ASCii" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts 1000" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            //通道4
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN4" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:FORMat ASCii" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts 1000" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            ThisEquitRequestTime.Start();          // 初始化完成，启动 1 秒定时取数
            translateClass.reciveState("示波器初始化成功",0,false);
            // ↑ 注意：这里 EquipAddress 传 0，只写日志，不更新状态栏（状态栏由 ConnectAsync 回调负责）
        }
        #region 示波器坐标轴变换
        // ===== 下面的方法都是"界面操作 → 一条 SCPI 指令"的直译 =====

        // 垂直档位 :CHANnelx:SCALe（单位 V/div）
        public void ChanScale(string Channel, string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":SCALe " + Time + "\n"));
        }

        // 垂直偏移 :CHANnelx:OFFSet
        public void ChanOffset(string Channel, string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":OFFSet " + Time + "\n"));
        }

        // 水平时基 :TIMebase:MAIN:SCALe（单位 秒/div）
        public void TimeScale(string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TIMebase:MAIN:SCALe " + Time + "\n"));
        }

        //触发模式选择（边沿方向）：true=上升沿 POS，false=下降沿 NEG
        public void TriggerMode(bool UpTriggMode)//触发模式选择
        {
            if(UpTriggMode)
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SLOPe POS" + "\n"));//上升沿触发
            }
            else
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SLOPe NEG" + "\n"));//下升沿触发（"下升"应为"下降"，注释笔误）
            }
        }

        // 触发源 :TRIGger:EDGE:SOURce CHANx
        public void TriggerSource(string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SOURce " + Time + "\n"));
        }

        // 触发电平 :TRIGger:EDGE:LEVel
        public void TriggerLevel(double Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:LEVel " + Time.ToString() + "\n"));
        }

        // 运行 / 停止 / 自动 / 单次：对应示波器面板的 RUN STOP AUTO SINGLE
        public void Start()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":RUN" + "\n"));
        }

        public void Stop()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":STOP" + "\n"));
        }

        public void Auto()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":AUToset" + "\n"));
        }

        public void Single()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SINGle" + "\n"));
        }


        #endregion
        #region 示波器数据解析
        /// <summary>
        /// ★ 1秒定时器回调 ★
        /// 每次"嘀嗒"检查：当前该读哪个通道(Chnumber)？该通道被勾选了吗(CHANNum 位掩码)？
        /// 允许读(EquipDataRead)吗？三个条件都满足 → 启动一轮问答链条。
        /// 【2026-10 真实时间轴】改为先发 :WAVeform:XINCrement? 启动链条，
        /// 后续 XORigin? 和 DATA? 由接收线程按状态机(ExpectQuery)接力发出。
        /// </summary>
        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            if ((translateClass.Chnumber == 0) && (translateClass.EquipDataRead) &&((translateClass.CHANNum & 0x01) == 0x01))
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN1" + "\n"));
                Thread.Sleep(2);
                //2026-10 真实时间轴：先问时间参数，由接收线程按状态机接力发 DATA?
                translateClass.ExpectQuery = 1;
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XINCrement?" + "\n"));
                translateClass.EquipDataRead = false;
                // ↑ 立刻置 false：本轮问答没走完前，定时器再来也不许发问（防止指令刷屏）
             }
            if ((translateClass.Chnumber == 1) && (translateClass.EquipDataRead) && ((translateClass.CHANNum & 0x02) == 0x02))
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN2" + "\n"));
                Thread.Sleep(2);
                translateClass.ExpectQuery = 1;
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XINCrement?" + "\n"));
                translateClass.EquipDataRead = false;
            }
            if ((translateClass.Chnumber == 2) && (translateClass.EquipDataRead) && ((translateClass.CHANNum & 0x04) == 0x04))
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN3" + "\n"));
                Thread.Sleep(2);
                translateClass.ExpectQuery = 1;
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XINCrement?" + "\n"));
                translateClass.EquipDataRead = false;
            }
            if ((translateClass.Chnumber == 3) && (translateClass.EquipDataRead) && ((translateClass.CHANNum & 0x08) == 0x08))
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN4" + "\n"));
                Thread.Sleep(2);
                translateClass.ExpectQuery = 1;
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XINCrement?" + "\n"));
                translateClass.EquipDataRead = false;
            }

        }
        #endregion


    }
}
