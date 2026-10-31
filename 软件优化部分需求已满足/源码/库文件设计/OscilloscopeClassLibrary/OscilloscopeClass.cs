using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using InstrumentInterfaceLibrary;

namespace OscilloscopeClassLibrary
{
    //2026-10-30 实现 IScope 统一接口（方案A·接口隔离），方法体一行未动，现有功能完全保留
    public class OscilloscopeClass : IScope
    {
        public const int WavePointCount = 5000;

        /// <summary>机型显示名，设备选择下拉框用。</summary>
        public string DeviceName => "RIGOL MHO984";

        /// <summary>总开关：true=软件轮询读波形数据。映射到底层 translateClass.EquipDataRead。</summary>
        public bool DataReadEnabled
        {
            get { return translateClass.EquipDataRead; }
            set { translateClass.EquipDataRead = value; }
        }

        /// <summary>通道使能掩码：Bit0~Bit3 对应 CH1~CH4。映射到底层 translateClass.CHANNum。</summary>
        public byte ChannelMask
        {
            get { return translateClass.CHANNum; }
            set { translateClass.CHANNum = value; }
        }

        /// <summary>当前轮询到哪个通道（0~3）。映射到底层 translateClass.Chnumber。</summary>
        public int ReadChannelIndex
        {
            get { return translateClass.Chnumber; }
            set { translateClass.Chnumber = (byte)value; }
        }

        /// <summary>本机型支持深度保存。</summary>
        public bool SupportDeepSave => true;
        public TcpTranslateClass translateClass;
        System.Timers.Timer ThisEquitRequestTime = null;
        public byte CHANNum = 1;//Bit0 通道1 Bit1 通道2 Bit3 通道3 Bit4 通道4
        public OscilloscopeClass()
        {
            translateClass = new TcpTranslateClass();

            ThisEquitRequestTime = new System.Timers.Timer(1000);//20ms进入一次
            ThisEquitRequestTime.Elapsed += new System.Timers.ElapsedEventHandler(ThisEquitRequestTime_Event);
            ThisEquitRequestTime.AutoReset = true;
            ThisEquitRequestTime.Enabled = false;
        }

        //通信连接
        public void OpenTranslate(string host, int port)
        {
            //2026-10-31 修复3：InitOscillPrame 要发 SCPI 指令，必须等 TCP 真正建链后才执行。
            //原来 ConnectAsync 是后台 fire-and-forget，这里立即执行初始化，指令全部发到未连接的 socket 被静默丢弃，
            //表现为"假成功"；现在挂到 AfterConnected，由库在连接真正成功时调用。
            translateClass.AfterConnected = () => InitOscillPrame("CHAN1");
            _ = translateClass.ConnectAsync(host, port);
            // ThisEquitRequestTime.Start();
        }

        //通信关闭
        public void CloseTranslate()
        {
            ThisEquitRequestTime.Stop();
            translateClass.CloseAsync();
        }

        public void FindChannel()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce?" + "\n"));
        }

        public string GetChannel()
        {
            return translateClass.ChannelName;
        }
        //对矢网进行初始设置
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
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + WavePointCount + "\n"));
            //通道2
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN2" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:FORMat ASCii" + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + WavePointCount + "\n"));
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
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + WavePointCount + "\n"));
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
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + WavePointCount + "\n"));
            Thread.Sleep(2);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEP 1" + "\n"));
            ThisEquitRequestTime.Start();
            if (translateClass.reciveState != null)   //回调可能尚未挂接（连接窗口先Open后Attach的时序），防空引用
                translateClass.reciveState("示波器初始化成功",0,false);
        }
        #region 示波器坐标轴变换

        public void ChanScale(string Channel, string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":SCALe " + Time + "\n"));
        }

        public void ChanOffset(string Channel, string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":OFFSet " + Time + "\n"));
        }

        public void ChanImpedance(string Channel, string Imp)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":IMPedance " + Imp + "\n"));
        }

        public void ChanProbe(string Channel, string Att)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":" + Channel + ":PROBe " + Att + "\n"));   //普源字典：探头倍率命令为 :CHANx:PROBe <倍率>（:PROBe:ATTenuation 会报远程指令错误）
        }
        public void TimeScale(string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TIMebase:MAIN:SCALe " + Time + "\n"));
        }
        public void TimeOffset (string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TIMebase:MAIN:OFFSet " + Time + "\n"));
        }

        public void TriggerMode(bool UpTriggMode)//触发模式选择
        {
            if(UpTriggMode)
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SLOPe POS" + "\n"));//上升沿触发
            }
            else
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SLOPe NEG" + "\n"));//下升沿触发
            }
        }

        public void TriggerSource(string Time)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:SOURce " + Time + "\n"));
        }

        public void TriggerLevel(double Time)
        {
            //2026-10-31 修复21：SCPI 只接受英文句点作小数点（NI 官方确认），数值必须按 InvariantCulture 格式化，
            //否则系统区域设置小数点为逗号的电脑上会发出 ":LEVel 1,00" 被仪器拒收
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":TRIGger:EDGE:LEVel " + Time.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"));
        }

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

        //示波器数据保存
        public void SaveimageToUsb()
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":SAVE:IMAGe:STARt" + "\n"));
        }

        //2026-10-17 新增：深存储保存（RAW模式，点数可超过NORMal的1000点上限）
        //2026-10-30 字段保留（内部仍用 DeepSaveMode 名字），对外暴露接口属性 DeepSaveActive
        public bool DeepSaveMode = false;

        /// <summary>深存储收集模式开关（IScope 接口属性，等价内部字段 DeepSaveMode）。</summary>
        public bool DeepSaveActive
        {
            get { return DeepSaveMode; }
            set { DeepSaveMode = value; }
        }

        //2026-10-30 新增：本次深存储实际生效的点数（回读自示波器）。
        //快时基下示波器会把请求的深度压小（时间窗口物理上存不下那么多点），
        //主窗口必须按这个实际值验收数据，否则又会"请求>实际"触发补采。
        //2026-10-30 由 public 字段改为自动属性（字段无法实现接口属性 IScope.DeepSavePointsApplied）。
        //类内 BeginDeepSave 的读写在属性上等价，MainWindow 的外部读取不受影响。
        public int DeepSavePointsApplied { get; set; } = 0;

        //开始深存储采集。
        //顺序有讲究（2026-10-30修复"点数不够就退出STOP进AUTO"的bug）：
        //  旧顺序 Stop→改MDEPth→读RAW 是错的——STOP定格状态下示波器不接受存储深度变更，
        //  会自己退出STOP重新采集（AUTO），RAW读到的仍是旧短记录，点数永远不够。
        //  正确顺序：RUN→改MDEPth→等新一帧采完(TD)→STOP定格→回读实际深度→切RAW按实际点数读。
        public void BeginDeepSave(int points)
        {
            DeepSaveMode = true;
            translateClass.EquipDataRead = false;   //暂停常规轮转，避免旧配置的残留读取插进来
            translateClass.ExpectQuery = 0;

            Start();                                //:RUN（已在运行也无害）
            Thread.Sleep(200);
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEPth " + points + "\n"));

            //等待新一帧采集完成：TD=已触发并采满一帧；AUTO触发会自取，通常几百ms内。
            //最多等5秒——NORMal触发下没信号就超时，STOP定格的是旧帧，读出来点数会不够，
            //主窗口看门狗会兜底提示，不会卡死。
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(100);
                string st = QuerySync(":TRIGger:STATus?");
                if (st != null && st.Contains("TD")) break;
            }

            Stop();                                 //:STOP，定格刚采满的这一帧
            Thread.Sleep(200);

            //回读实际存储深度：请求值可能被示波器压小（时基太快时存不下），
            //POINts若发得比实际点数大，示波器又会自动退STOP补采——这是本次bug的直接触发点。
            DeepSavePointsApplied = points;
            string mdep = QuerySync(":ACQuire:MDEPth?");
            if (mdep != null)
            {
                try
                {
                    double v = double.Parse(mdep, System.Globalization.CultureInfo.InvariantCulture);
                    if (v >= 1000) DeepSavePointsApplied = (int)v;
                }
                catch { /*回读解析失败就按请求值来*/ }
            }
            if (DeepSavePointsApplied < points)
                Console.WriteLine($"[深存] 请求{points}点被示波器压到{DeepSavePointsApplied}点（当前时基存不下），按实际点数读取");

            for (int ch = 1; ch <= 4; ch++)
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN" + ch + "\n"));
                Thread.Sleep(2);
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE RAW" + "\n"));
                Thread.Sleep(2);
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + DeepSavePointsApplied + "\n"));
                Thread.Sleep(2);
            }
            //立即触发一轮读取（不等1秒定时器），Chnumber保持当前启用通道不变
            translateClass.EquipDataRead = true;
            ThisEquitRequestTime_Event(null, null);
        }

        //同步查询小工具：带1秒超时，查不到返回null（BeginDeepSave的等待循环用）
        private string QuerySync(string scpiCmd)
        {
            try { return Task.Run(async () => await Query(scpiCmd, 1000)).Result; }
            catch { return null; }
        }

        //结束深存储采集：RUN恢复采集 → 四通道切回NORMal并恢复原点数 → 存储深度AUTO
        public void EndDeepSave()
        {
            DeepSaveMode = false;
            Start();                                //:RUN
            for (int ch = 1; ch <= 4; ch++)
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN" + ch + "\n"));
                Thread.Sleep(2);
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:MODE NORMal" + "\n"));
                Thread.Sleep(2);
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:POINts " + WavePointCount + "\n"));
                Thread.Sleep(2);
            }
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":ACQuire:MDEPth AUTO" + "\n"));
            translateClass.EquipDataRead = true;
        }

        //=====================================================
        //以下各区为2026-10-17新增：把《MHO900编程手册》第3章命令系统逐子系统封装为函数
        //命名规律：SetXxx=设置类（只发不收），GetXxx=查询类（发?并等回答，返回值可能为null）
        //=====================================================

        #region 通用指令通道（基础设施，也是换新示波器时的逃生门）

        //原始指令直通：手册里任何命令都能从这里发。
        //将来换新示波器，新命令不用等我封装，先靠它顶上去。
        public void SendCmd(string scpiCmd)
        {
            _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(scpiCmd + "\n"));
        }

        //通用查询：发一条带?的命令，等示波器回一整行（默认5秒超时，超时返回null）。
        //查询瞬间会暂停波形轮转（约零点几秒），属于正常现象。
        public async Task<string> Query(string scpiCmd, int timeoutMs = 5000)
        {
            translateClass.EquipDataRead = false;
            try
            {
                return await translateClass.QueryAsync(scpiCmd, timeoutMs);
            }
            finally
            {
                //2026-10-31 修复22：无论查询成功还是抛异常都必须恢复轮转标志，
                //否则中途异常会让波形刷新永久停止且无任何提示
                translateClass.EquipDataRead = true;
            }
        }

        #endregion
        #region 运行与系统控制（手册3.1根命令、3.2自动设置、3.12 IEEE488.2通用命令）

        //:CLEar 清除波形（手册3.1.1）
        public void ClearWave()
        {
            SendCmd(":CLEar");
        }

        //:TFORce 强制产生一次触发（手册3.1.5）
        public void ForceTrigger()
        {
            SendCmd(":TFORce");
        }

        //:AUToset:PEAK 按峰值自动设置（手册3.2.2）
        public void AutosetPeak()
        {
            SendCmd(":AUToset:PEAK");
        }

        //*IDN? 查询仪器型号（连接成功后体检用，返回如"RIGOL,MHO984,..."）
        public async Task<string> GetIDN()
        {
            return await Query("*IDN?");
        }

        //*RST 恢复出厂默认设置（手册3.12）
        public void ResetInstrument()
        {
            SendCmd("*RST");
        }

        //*CLS 清除状态寄存器（手册3.12）
        public void ClearStatusReg()
        {
            SendCmd("*CLS");
        }

        //*OPC? 查询操作是否完成（返回"1"=完成）
        public async Task<string> GetOpcState()
        {
            return await Query("*OPC?");
        }

        //*STB? 查询状态字节（排查"远程指令错误"时有用）
        public async Task<string> GetStatusByte()
        {
            return await Query("*STB?");
        }

        #endregion
        #region 采样设置（手册3.3）

        //:ACQuire:TYPE 获取方式（手册3.3.3）。type例："NORMal"普通、"AVERages"平均。
        public void AcquireType(string type)
        {
            SendCmd(":ACQuire:TYPE " + type);
        }

        //:ACQuire:AVERages 平均次数（手册3.3.1），type为AVERages时生效，如128
        public void AcquireAverages(int count)
        {
            SendCmd(":ACQuire:AVERages " + count);
        }

        //:ACQuire:MDEPth 存储深度（手册3.3.2），mdep如"AUTO"、"10000"、"1M"
        public void AcquireMemDepth(string mdep)
        {
            SendCmd(":ACQuire:MDEPth " + mdep);
        }

        //:ACQuire:SRATe? 查询当前采样率（返回科学计数字符串）
        public async Task<string> GetSampleRate()
        {
            return await Query(":ACQuire:SRATe?");
        }

        //:ACQuire:BITS 高分辨率位数（手册3.3.5），只能14或16
        public void AcquireBits(int bits)
        {
            SendCmd(":ACQuire:BITS " + bits);
        }

        #endregion
        #region 通道设置扩展（手册3.6，刻度/偏移/阻抗/探头在上方坐标轴变换区）

        //:CHANnel<n>:BWLimit 带宽限制（手册3.6）。val如"OFF"、"20M"
        public void ChanBWLimit(string ch, string val)
        {
            SendCmd(":" + ch + ":BWLimit " + val);
        }

        //:CHANnel<n>:COUPling 耦合方式（手册3.6）："AC"或"DC"
        public void ChanCoupling(string ch, string coupling)
        {
            SendCmd(":" + ch + ":COUPling " + coupling);
        }

        //:CHANnel<n>:DISPlay 通道开关（手册3.6）
        public void ChanDisplay(string ch, bool on)
        {
            SendCmd(":" + ch + ":DISPlay " + (on ? "ON" : "OFF"));
        }

        //:CHANnel<n>:INVert 波形反相（手册3.6）
        public void ChanInvert(string ch, bool on)
        {
            SendCmd(":" + ch + ":INVert " + (on ? "ON" : "OFF"));
        }

        //:CHANnel<n>:LABel:CONTent 通道标签文字（手册3.6）
        public void ChanLabel(string ch, string text)
        {
            SendCmd(":" + ch + ":LABel:CONTent \"" + text + "\"");
        }

        //:CHANnel<n>:LABel:SHOW 标签显示开关（手册3.6）
        public void ChanLabelShow(string ch, bool on)
        {
            SendCmd(":" + ch + ":LABel:SHOW " + (on ? "ON" : "OFF"));
        }

        //:CHANnel<n>:TCALibrate 延时校正（手册3.6），单位秒，如20ns=0.00000002
        public void ChanDelayCal(string ch, double seconds)
        {
            SendCmd(":" + ch + ":TCALibrate " + seconds);
        }

        #endregion
        #region 时基扩展（手册3.26，主时基SCALe/OFFSet在上方坐标轴变换区）

        //:TIMebase:DELay:ENABle 延迟扫描开关（手册3.26）
        public void TimebaseDelayEnable(bool on)
        {
            SendCmd(":TIMebase:DELay:ENABle " + (on ? "ON" : "OFF"));
        }

        //:TIMebase:DELay:SCALe 延迟时基档位（手册3.26）
        public void TimebaseDelayScale(double secondsPerDiv)
        {
            SendCmd(":TIMebase:DELay:SCALe " + secondsPerDiv);
        }

        //:TIMebase:DELay:OFFSet 延迟时基偏移（手册3.26）
        public void TimebaseDelayOffset(double seconds)
        {
            SendCmd(":TIMebase:DELay:OFFSet " + seconds);
        }

        //:TIMebase:MAIN:SCALe? 查询当前主时基档位
        public async Task<string> GetTimebaseScale()
        {
            return await Query(":TIMebase:MAIN:SCALe?");
        }

        //:TIMebase:MAIN:OFFSet? 查询当前主时基偏移
        public async Task<string> GetTimebaseOffset()
        {
            return await Query(":TIMebase:MAIN:OFFSet?");
        }

        #endregion
        #region 触发扩展（手册3.27，边沿SLOPe/SOURce/LEVel在上方坐标轴变换区）

        //:TRIGger:SWEep 触发方式（手册3.27.4）："AUTO"、"NORMal"、"SINGle"
        public void TriggerSweep(string sweep)
        {
            SendCmd(":TRIGger:SWEep " + sweep);
        }

        //:TRIGger:STATus? 查询触发状态（返回TD/WAIT/RUN/AUTO/STOP）
        public async Task<string> GetTriggerStatus()
        {
            return await Query(":TRIGger:STATus?");
        }

        //:TRIGger:COUPling 触发耦合（手册3.27）："DC"、"AC"、"HFReject"、"LFReject"
        public void TriggerCoupling(string coupling)
        {
            SendCmd(":TRIGger:COUPling " + coupling);
        }

        //:TRIGger:HOLDoff 触发释抑时间（手册3.27.5），单位秒，8ns~10s
        public void TriggerHoldoff(double seconds)
        {
            SendCmd(":TRIGger:HOLDoff " + seconds);
        }

        #endregion
        #region 自动测量（手册3.17）

        //:MEASure:ITEM 添加测量项（手册3.17.2），item如"VPP"、"FREQuency"，ch如"CHANnel1"
        public void MeasureAddItem(string item, string ch)
        {
            SendCmd(":MEASure:ITEM " + item + "," + ch);
        }

        //:MEASure:DELete 删除全部测量项（手册3.17.3）
        public void MeasureClearAll()
        {
            SendCmd(":MEASure:DELete");
        }

        //:MEASure:SOURce 设置参数测量信源（手册3.17.1）
        public void MeasureSetSource(string ch)
        {
            SendCmd(":MEASure:SOURce " + ch);
        }

        //测量值通用读取：返回科学计数字符串（如"2.500000E+0"），测不到返回null
        public async Task<string> MeasureItemValue(string item, string ch)
        {
            return await Query(":MEASure:ITEM? " + item + "," + ch);
        }

        //测量值通用读取（float版）：测不到或解析失败返回NaN
        public async Task<float> MeasureValue(string item, string ch)
        {
            string ans = await MeasureItemValue(item, ch);
            float v;
            if (ans != null && float.TryParse(ans, out v)) return v;
            return float.NaN;
        }

        //常用测量项快捷函数（通道传"CHANnel1"~"CHANnel4"）
        public async Task<float> GetVpp(string ch)        { return await MeasureValue("VPP", ch); }        //峰峰值
        public async Task<float> GetVmax(string ch)       { return await MeasureValue("VMAX", ch); }       //最大值
        public async Task<float> GetVmin(string ch)       { return await MeasureValue("VMIN", ch); }       //最小值
        public async Task<float> GetVavg(string ch)       { return await MeasureValue("VAVG", ch); }       //平均值
        public async Task<float> GetVrms(string ch)       { return await MeasureValue("VRMS", ch); }       //有效值
        public async Task<float> GetFrequency(string ch)  { return await MeasureValue("FREQuency", ch); }  //频率
        public async Task<float> GetPeriod(string ch)     { return await MeasureValue("PERiod", ch); }     //周期
        public async Task<float> GetRiseTime(string ch)   { return await MeasureValue("RISe", ch); }       //上升时间
        public async Task<float> GetFallTime(string ch)   { return await MeasureValue("FALL", ch); }       //下降时间
        public async Task<float> GetPosDuty(string ch)    { return await MeasureValue("PDUTy", ch); }      //正占空比
        public async Task<float> GetNegDuty(string ch)    { return await MeasureValue("NDUTy", ch); }      //负占空比
        public async Task<float> GetOvershoot(string ch)  { return await MeasureValue("OVERshoot", ch); }  //过冲

        //频率计（手册3.7）：使能开关、信源、读值
        public void CounterEnable(bool on)
        {
            SendCmd(":MEASure:COUNter:ENABle " + (on ? "ON" : "OFF"));
        }
        public void CounterSource(string ch)
        {
            SendCmd(":MEASure:COUNter:SOURce " + ch);
        }
        public async Task<string> CounterValue()
        {
            return await Query(":MEASure:COUNter:VALue?");
        }

        #endregion
        #region 光标（手册3.8）

        //:CURSor:MODE 光标模式（手册3.8）："OFF"、"MANual"、"TRACk"、"AUTO"
        public void CursorMode(string mode)
        {
            SendCmd(":CURSor:MODE " + mode);
        }

        //:CURSor:MANual:TYPE 手动光标类型（手册3.8）："AMPLitude"、"TIME"、"BOTH"
        public void CursorManualType(string type)
        {
            SendCmd(":CURSor:MANual:TYPE " + type);
        }

        #endregion
        #region 波形读取参数（手册3.28，实时读波流程在TcpTranslateClass的轮转里）

        //:WAVeform:SOURce 读波信源
        public void WaveSource(string ch)
        {
            SendCmd(":WAVeform:SOURce " + ch);
        }

        //:WAVeform:MODE 读波模式："NORMal"屏幕压缩点、"RAW"真实采样点（需先STOP）、"MAXimum"自动
        public void WaveMode(string mode)
        {
            SendCmd(":WAVeform:MODE " + mode);
        }

        //:WAVeform:FORMat 数据格式："ASCii"、"BYTE"、"WORD"
        public void WaveFormat(string format)
        {
            SendCmd(":WAVeform:FORMat " + format);
        }

        //:WAVeform:POINts 读波点数
        public void WavePoints(int points)
        {
            SendCmd(":WAVeform:POINts " + points);
        }

        //:WAVeform:STARt / :STOP 读取起止点（RAW模式配合，分段读大数据时用）
        public void WaveRange(int startPoint, int stopPoint)
        {
            SendCmd(":WAVeform:STARt " + startPoint);
            SendCmd(":WAVeform:STOP " + stopPoint);
        }

        //:WAVeform:PREamble? 一次取回全部波形参数（格式、点数、增量、原点等）
        public async Task<string> WavePreamble()
        {
            return await Query(":WAVeform:PREamble?");
        }

        #endregion
        #region 存储与截图（手册3.21，含SMB网络存储——截图直存电脑靠它）

        //:SAVE:IMAGe <path> 截图存到示波器外部存储（手册3.21.7），如"D:/123.png"
        public void SaveImage(string path)
        {
            SendCmd(":SAVE:IMAGe " + path);
        }

        //:SAVE:IMAGe:FORMat 图片格式（手册3.21.3）："PNG"、"JPG"、"BMP"
        public void SaveImageFormat(string format)
        {
            SendCmd(":SAVE:IMAGe:FORMat " + format);
        }

        //:SAVE:IMAGe:INVert 反色开关（手册3.21.1）
        public void SaveImageInvert(bool on)
        {
            SendCmd(":SAVE:IMAGe:INVert " + (on ? "ON" : "OFF"));
        }

        //:SAVE:IMAGe:HEADer 页眉开关（手册3.21.4）
        public void SaveImageHeader(bool on)
        {
            SendCmd(":SAVE:IMAGe:HEADer " + (on ? "ON" : "OFF"));
        }

        //:SAVE:PATHname 设置文件保存路径（手册3.21.6），如"C:/new"
        public void SavePathSet(string path)
        {
            SendCmd(":SAVE:PATHname " + path);
        }

        //:SAVE:SETup <path> 保存示波器设置（手册3.21.8），如"D:/123.stp"
        public void SaveSetupFile(string path)
        {
            SendCmd(":SAVE:SETup " + path);
        }

        //:SAVE:WAVeform <path> 屏幕波形存CSV（手册3.21.9），如"D:/123.csv"
        public void SaveWaveformFile(string path)
        {
            SendCmd(":SAVE:WAVeform " + path);
        }

        //:SAVE:MASK <path> 保存通过/失败模板（手册3.21.10），如"D:/123.pf"
        public void SaveMaskFile(string path)
        {
            SendCmd(":SAVE:MASK " + path);
        }

        //:SAVe:SMB:SERVerpath 设置SMB共享路径（手册3.21.15），如"\\\\172.16.25.77\\Share"
        public void SmbServerPath(string path)
        {
            SendCmd(":SAVe:SMB:SERVerpath " + path);
        }

        //:SAVe:SMB:USERname 共享用户名（手册3.21.16）
        public void SmbUser(string name)
        {
            SendCmd(":SAVe:SMB:USERname " + name);
        }

        //:SAVe:SMB:PASSword 共享密码（手册3.21.17）
        public void SmbPassword(string password)
        {
            SendCmd(":SAVe:SMB:PASSword " + password);
        }

        //:SAVe:SMB:AUToconnect 开机自动连接开关（手册3.21.18）
        public void SmbAutoConnect(bool on)
        {
            SendCmd(":SAVe:SMB:AUToconnect " + (on ? "ON" : "OFF"));
        }

        //:SAVe:SMB:CONNect 立即连接SMB共享（手册3.21.19）
        public void SmbConnect()
        {
            SendCmd(":SAVe:SMB:CONNect");
        }

        //:SAVe:SMB:DISConnect 断开SMB共享（手册3.21.20）
        public void SmbDisconnect()
        {
            SendCmd(":SAVe:SMB:DISConnect");
        }

        //:SAVe:SMB:CONState? 查询SMB连接状态
        public async Task<string> SmbState()
        {
            return await Query(":SAVe:SMB:CONState?");
        }

        //:SAVE:IMAGe:DATA? 把截图二进制流直接吐回电脑（手册3.21.5）。
        //注意：返回的是二进制块不是文本行，会冲垮现有按行解析，暂未封装自动接收，需要时单独做接收分支。
        public void SaveImageDataRaw()
        {
            SendCmd(":SAVE:IMAGe:DATA?");
        }

        #endregion

        #endregion
        #region 示波器数据解析
        public void ThisEquitRequestTime_Event(object source, System.Timers.ElapsedEventArgs e)
        {
            //2026-10-17 通用查询期间暂停常规轮转（Query方法会把EquipDataRead置false），避免查询与轮读抢道
            if (!translateClass.EquipDataRead) return;
            if ((translateClass.Chnumber == 0) && (translateClass.EquipDataRead) &&((translateClass.CHANNum & 0x01) == 0x01))
            {
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:SOURce CHAN1" + "\n"));
                Thread.Sleep(2);
                //2026-10 真实时间轴：先问时间参数，由接收线程按状态机接力发 DATA?
                translateClass.ExpectQuery = 1;
                _ = translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":WAVeform:XINCrement?" + "\n"));
                translateClass.EquipDataRead = false;
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
