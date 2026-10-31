using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;   //2026-10-31 DispatcherTimer（滑杆防抖）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ValueSourceN2410ClassLibrary;
using OscilloscopeClassLibrary;
using SingalClassLibrary;
using InstrumentInterfaceLibrary;   //2026-10-30 设备槽位统一接口（方案A·接口隔离）
using System.IO;
using System.Reflection;
using System.IO;
using System.Reflection;
using OfficeOpenXml;
using LicenseContext = OfficeOpenXml.LicenseContext;
using OfficeOpenXml.Drawing.Slicer.Style;
using OxyPlot.Wpf;
using OxyPlot;
using System.Windows.Media.Media3D;
using Microsoft.Win32;

namespace MainSoftwareV100
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 变量定义
        //2026-10-30 方案A·接口隔离：四个槽位只认接口，具体机型由连接窗口选好、
        //经 DeviceFactory 创建后，由 Attach* 方法挂进来（接线+机型专属初始化都在那里）。
        IPowerSource powerSourceA = null;   //电流源A（主界面左侧电源区块）
        IPowerSource powerSourceB = null;   //电流源B（主界面右侧电源区块）
        IScope oscilloscopeClass = null;
        ISignalSource signalClass = null;

        //记录每个槽位当前的机型名（用于状态提示与机型专属按钮的可用判断）
        string powerAModel = "", powerBModel = "", scopeModel = "", signalModel = "";

        ConnectInit connectInit = null;

        //2026-10-17 AI助手窗口实例（非模态，只能开一个）
        AiAssistantWindow aiAssistantWindow = null;

        //2026-10-30 参数提取窗口实例（非模态，只能开一个）
        DptAnalysisWindow dptAnalysisWindow = null;

        //2026-10-17 暴露给AI助手的只读入口（接口类型，任何机型都能用）
        public IScope Oscilloscope { get { return oscilloscopeClass; } }

        public ISignalSource SignalDev { get { return signalClass; } }   //2026-10-31 修复24：连接失败回滚时核对槽位用
        //AI电源工具默认操作电流源A；A没连就退到B
        public IPowerSource AiPower { get { return powerSourceA ?? powerSourceB; } }
        public IPowerSource PowerA { get { return powerSourceA; } }
        public IPowerSource PowerB { get { return powerSourceB; } }
        
        ExcelPackage EquipDataExcelPack = null;
        ExcelWorksheet EquipDataExcelSheet = null;
        string logFilePath;
        int WriteDataUpLimit = 0, WriteDataCount = 0;
        object[,] WriteDataGroupCh1 = new object[1000, 2], WriteDataGroupCh2 = new object[1000, 2], WriteDataGroupCh3 = new object[1000, 2], WriteDataGroupCh4 = new object[1000, 2];
        #endregion
        //2026-10-31 修复15：滑杆防抖。拖动滑杆时 ValueChanged 每步都触发，直接发指令会形成 SCPI 风暴
        //冲垮仪器指令队列；BAML 初始化赋值也会触发一次 ValueChanged（此时不应发指令）。
        //共用一只 DispatcherTimer：拖动中只重置计时器，停稳 200ms 后才真正发最后一条指令。
        private readonly DispatcherTimer _sliderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private Action _pendingSliderCmd = null;

        private void SliderDebounce_Tick(object sender, EventArgs e)
        {
            _sliderDebounce.Stop();
            var cmd = _pendingSliderCmd;
            _pendingSliderCmd = null;
            cmd?.Invoke();
        }

        /// <summary>把一条示波器指令挂到防抖计时器上：拖动中反复调用只保留最后一次。</summary>
        private void DispatchSliderCmd(Action cmd)
        {
            _pendingSliderCmd = cmd;
            _sliderDebounce.Stop();
            _sliderDebounce.Start();
        }

        public MainWindow()
        {
            InitLogs();
            //2026-10-30 设备不再在启动时创建，改由连接窗口选好机型后 Attach 进来。
            //启动只创建连接窗口本身，并告诉它"连好了交给谁"。
            connectInit = new ConnectInit();
            connectInit.OwnerMain = this;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
           // CreateExcelFile();
            InitializeComponent();
            _sliderDebounce.Tick += SliderDebounce_Tick;   //2026-10-31 修复15：滑杆防抖计时器

        }

        //===== 2026-10-30 设备挂载入口（连接窗口回调这四个方法）=====

        /// <summary>挂接电流源A：关旧设备 → 存字段、接状态回调、做机型专属初始化。dev 传 null 表示回滚为"未连接"。</summary>
        public void AttachPowerA(IPowerSource dev, string model)
        {
            if (powerSourceA != null && !ReferenceEquals(powerSourceA, dev))
            {
                try { powerSourceA.CloseTranslate(); } catch { }   //2026-10-31 换设备/回滚时先关旧连接
            }
            powerSourceA = dev;
            powerAModel = model;
            if (dev is N2410Class n10)               //NGI 机型：接状态显示与日志回调
            {
                n10.translateClass.thisNetPublicTool = N3416InfDisplay;
                n10.translateClass.reciveState = WriteDataLog;
            }
            else if (dev is N36200Class n36)
            {
                n36.translateClass.thisNetPublicTool = N3416InfDisplay;
                n36.translateClass.reciveState = WriteDataLog;
                n36.SetChannelMode();                //原连接窗口里的机型专属初始化搬到这里
                n36.SetChannelCvOrCc();
            }
            UpdatePowerBlockAHeader();
        }

        /// <summary>挂接电流源B。dev 传 null 表示回滚为"未连接"。</summary>
        public void AttachPowerB(IPowerSource dev, string model)
        {
            if (powerSourceB != null && !ReferenceEquals(powerSourceB, dev))
            {
                try { powerSourceB.CloseTranslate(); } catch { }   //2026-10-31 换设备/回滚时先关旧连接
            }
            powerSourceB = dev;
            powerBModel = model;
            if (dev is N2410Class n10)
            {
                n10.translateClass.thisNetPublicTool = N3621InfDisplay;
                n10.translateClass.reciveState = WriteDataLog;
            }
            else if (dev is N36200Class n36)
            {
                n36.translateClass.thisNetPublicTool = N3621InfDisplay;
                n36.translateClass.reciveState = WriteDataLog;
                n36.SetChannelMode();
                n36.SetChannelCvOrCc();
            }
            UpdatePowerBlockBHeader();
        }

        /// <summary>挂接示波器：关旧设备 → 接波形数据回调。dev 传 null 表示回滚为"未连接"。</summary>
        public void AttachScope(IScope dev, string model)
        {
            if (oscilloscopeClass != null && !ReferenceEquals(oscilloscopeClass, dev))
            {
                try { oscilloscopeClass.CloseTranslate(); } catch { }   //2026-10-31 关掉旧设备的 TCP 与轮询线程
            }
            oscilloscopeClass = dev;
            scopeModel = model;
            if (dev is OscilloscopeClass rigol)      //只有 RIGOL 有实时波形轮询回调
            {
                rigol.translateClass.reciveState += WriteDataLog;
                rigol.translateClass.thisNetPublicTool = SomeActMethod;
                rigol.FindChannel();
            }
            else if (dev is LeCroyScopeLibrary.LeCroyScope lecroy)   //力科：轮询线程主动推波形
            {
                lecroy.WaveformReceived += SomeActMethod;   //签名与 RIGOL 回调一致
            }
            //LeCroy 等暂无实时波形链路：主界面曲线区域保持空白（见《未实现清单》）
        }

        /// <summary>挂接信号源：关旧设备 → 存字段。dev 传 null 表示回滚为"未连接"。</summary>
        public void AttachSignal(ISignalSource dev, string model)
        {
            if (signalClass != null && !ReferenceEquals(signalClass, dev))
            {
                try { signalClass.CloseTranslate(); } catch { }   //2026-10-31 换设备/回滚时先关旧连接
            }
            signalClass = dev;
            signalModel = model;
            if (dev is SignalClass old)              //原机型：初始化挪到 OpenTranslate 的 AfterConnected（TCP真正建链后）
            {
                old.translateClass.reciveState += WriteDataLog;
            }
        }

        /// <summary>更新主界面左侧电源区块标题（电流源A + 机型）。</summary>
        private void UpdatePowerBlockAHeader()
        {
            Dispatcher.Invoke(() =>
            {
                mEleN2410Status.Text = (powerSourceA == null)
                    ? "电流源A未连接"
                    : "电流源A（" + (powerAModel ?? "") + "）已连接";
            });
        }

        /// <summary>更新主界面右侧电源区块标题（电流源B + 机型）。</summary>
        private void UpdatePowerBlockBHeader()
        {
            Dispatcher.Invoke(() =>
            {
                mEleN3620State.Text = (powerSourceB == null)
                    ? "电流源B未连接"
                    : "电流源B（" + (powerBModel ?? "") + "）已连接";
            });
        }


        private void CreateExcelFile(string TxtPath)
        {
            //局放数据
            //  string TxtPath = ".\\WriteData\\" + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器数据.xlsx";
            EquipDataExcelPack = new ExcelPackage(TxtPath);
            EquipDataExcelSheet = EquipDataExcelPack.Workbook.Worksheets.Add("数据记录");
            EquipDataExcelSheet.DefaultColWidth = 12;

            //for (int i = 1; i < 30000; i+=5)
            //{
            //    WriteDataUpLimit++;
            //    EquipDataExcelSheet.Cells[i, 1].Value = "第 " + (i / 5 + 1).ToString() + " 包";
                EquipDataExcelSheet.Cells[1, 1].Value = "CH1时间";
                EquipDataExcelSheet.Cells[1, 2].Value = "CH1数据";
                EquipDataExcelSheet.Cells[1, 3].Value = "CH2时间";
                EquipDataExcelSheet.Cells[1, 4].Value = "CH2数据";
                EquipDataExcelSheet.Cells[1, 5].Value = "CH3时间";
                EquipDataExcelSheet.Cells[1, 6].Value = "CH3数据";
                EquipDataExcelSheet.Cells[1, 7].Value = "CH4时间";
                EquipDataExcelSheet.Cells[1, 8].Value = "CH4数据";
            //  }
            //WriteDataLog("本次可写入 "+ WriteDataUpLimit.ToString()+"组数据");
        }

        void InitLogs()
        {
            // 获取当前执行文件的目录
            string exeDirectory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine($"执行文件所在目录: {exeDirectory}");

            // 指定日志文件夹名称
            string logFolderName = "Logs";
            // 拼接日志文件夹的完整路径
            string logFolderPath = System.IO.Path.Combine(exeDirectory, logFolderName);

            // 检查日志文件夹是否存在，如果不存在则创建
            if (!Directory.Exists(logFolderPath))
            {
                Directory.CreateDirectory(logFolderPath);
                Console.WriteLine($"日志文件夹 {logFolderName} 创建成功。");
            }
            else
            {
                Console.WriteLine($"日志文件夹 {logFolderName} 已存在。");
            }

            // 根据当前日期生成日志文件名
            string logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
            // 拼接日志文件的完整路径
            logFilePath = System.IO.Path.Combine(logFolderPath, logFileName);
            WriteDataLog("软件再次被打开",0,false);
        }

        //2026-10-31 修复20：日志文件并发写锁（多设备回读线程共用，静态即可）
        private static readonly object _logFileLock = new object();

        void WriteDataLog(string WriteData, Byte EquipAddress,bool Connectflag) // 0 默认;1 N2410;2 N36200;3 示波器;4 信号源
        {
            // 要写入的日志内容
            string logMessage = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss.fff") + " " + WriteData;
            //2026-10-31 修复20：多设备回读线程并发写同一日志文件，原来 Task.Run 无锁会行交错/ IOException。
            //改为静态锁内同步追加（每次开写完即关），日志量小，同步写无感。
            lock (_logFileLock)
            {
                try { File.AppendAllText(logFilePath, logMessage + Environment.NewLine); }
                catch (Exception ex) { Console.WriteLine($"写日志失败：{ex.Message}"); }
            }

            if(EquipAddress == 1)
            {
                if(Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mEleN2410Status.Text = "电流源A已连接";
                        mEleN2410Status.Foreground  = Brushes.Green;
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mEleN2410Status.Text = "电流源A未连接";
                        mEleN2410Status.Foreground = Brushes.Gray;
                    }));
                }
            }
            if (EquipAddress == 2)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mEleN3620State.Text = "电流源B已连接";
                        mEleN3620State.Foreground = Brushes.Green;
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        mEleN3620State.Text = "电流源B未连接";
                        mEleN3620State.Foreground = Brushes.Gray;
                    }));
                }
            }
            if (EquipAddress == 3)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShiBoState.Text = "示波器已连接";
                        ShiBoState.Foreground = Brushes.Green;
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShiBoState.Text = "示波器未连接";
                        ShiBoState.Foreground = Brushes.Gray;
                    }));
                }
            }
            if (EquipAddress == 4)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SourceState.Text = "信号源已连接";
                        SourceState.Foreground = Brushes.Green;
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SourceState.Text = "信号源未连接";
                        SourceState.Foreground = Brushes.Gray;
                    }));
                }
            }
        }

        void N3416InfDisplay(float[] data)
        {
            //2026-10-31 修复11：floats 是库里的共享数组，接收线程下一帧会覆盖它。
            //Dispatcher 排队期间数组可能已被改写，必须先克隆再闭包，否则电压/电流显示张冠李戴。
            float[] snapshot = (float[])data.Clone();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                mN2410Value.Text = snapshot[0].ToString("0.000");
                mN2410Current.Text = snapshot[1].ToString("0.000");
            }));
        }

        void N3621InfDisplay(float[] data)
        {
            //2026-10-17 给AI助手留一份最近回读（纯赋值，无开销）
            AiTools.LastPowerVoltage = data[0];
            AiTools.LastPowerCurrent = data[1];
            AiTools.LastPowerTime = DateTime.Now;

            //2026-10-31 修复11：同 N3416InfDisplay，共享数组先克隆再进 Dispatcher
            float[] snapshot = (float[])data.Clone();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                mN3620Value.Text = snapshot[0].ToString("0.000");
                mN3620Current.Text = snapshot[1].ToString("0.000");
            }));
        }

        //2026-10-17 打开AI助手窗口（非模态，重复点击只聚焦已有窗口）
        private void OpenAiAssistant_Click(object sender, RoutedEventArgs e)
        {
            //每次打开都重新把仪器引用递给AI工具——窗口可能开在连接仪器之前，
            //递的是当时的null，不重递的话连上仪器AI也用不了
            AiTools.Init(Oscilloscope, AiPower);
            if (aiAssistantWindow == null || !aiAssistantWindow.IsVisible)
            {
                aiAssistantWindow = new AiAssistantWindow(this);
                aiAssistantWindow.Owner = this;
                aiAssistantWindow.Closed += (s, args) => aiAssistantWindow = null;
                aiAssistantWindow.Show();
            }
            else
            {
                aiAssistantWindow.Activate();
            }
        }

        //2026-10-30 打开参数提取窗口（非模态，重复点击只聚焦已有窗口）
        private void OpenDptAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (dptAnalysisWindow == null || !dptAnalysisWindow.IsVisible)
            {
                dptAnalysisWindow = new DptAnalysisWindow(this);
                dptAnalysisWindow.Owner = this;
                dptAnalysisWindow.Closed += (s, args) => dptAnalysisWindow = null;
                dptAnalysisWindow.Show();
            }
            else
            {
                dptAnalysisWindow.Activate();
            }
        }

        //2026-10-31 参数提取取数入口：WaveSnapshot 内部已做双槽位（深存优先），
        //这里直接转调即可。ch 取值 0~3（对应 CH1~CH4）。
        public WaveSnapshot.ChannelFrame GetAnalysisFrame(int ch)
        {
            return WaveSnapshot.Get(ch);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            #region 设置全屏
            //设置全屏
            this.WindowState = System.Windows.WindowState.Normal;
            this.WindowStyle = System.Windows.WindowStyle.None;
            this.ResizeMode = System.Windows.ResizeMode.NoResize;
            this.Topmost = false;

            this.Left = 0.0;
            this.Top = 0.0;
            this.Width = System.Windows.SystemParameters.PrimaryScreenWidth;
            this.Height = System.Windows.SystemParameters.PrimaryScreenHeight;
            #endregion
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            System.Windows.Point position = e.GetPosition(this);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (position.X >= 0 && position.X < this.ActualWidth && position.Y >= 0 && position.Y <= this.ActualHeight)
                {
                    this.DragMove();
                }

                if (this.WindowState == WindowState.Maximized)
                {
                    this.Width = this.ActualWidth;
                    this.Height = this.ActualHeight;
                    this.Left = 0;
                    this.Top = 0;
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.WindowState = WindowState.Normal;
                }
            }
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //CloseLog();
            //2026-10-31 修复退出安全隐患：原实现 fire-and-forget 发 :OUTP1 OFF 后立即 Kill()，
            //指令大概率没写出 socket，信号源输出保持 ON 退出。现在同步等发送完成并给 socket
            //刷出窗口；两路电源输出也尽力关闭。这是尽力而为的修法，正解是注册窗口关闭事件
            //做优雅关闭（本轮不动退出流程结构）。
            try
            {
                if (signalClass is SignalClass old)
                {
                    try { old.translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 OFF" + "\n")).Wait(800); } catch { }
                }
                try { powerSourceA?.SetOutput(false); } catch { }
                try { powerSourceB?.SetOutput(false); } catch { }
            }
            catch { }
            Thread.Sleep(300);
            Process.GetCurrentProcess().Kill();
        }

        private void IP_Connect(object sender, MouseButtonEventArgs e)
        {
            connectInit.Show();
        }

        #region 测试代码
        private async void SomeMethod()
        {
            // 创建Random对象
            Random random = new Random();
            List<Double> randomNumbers = new List<Double>(), randomValues = new List<Double>(), randomValues1 = new List<double>();
            // 循环生成1000个随机数
            for (int i = 0; i < 1000; i++)
            {
                randomValues.Add(Convert.ToDouble(i));
                // 生成一个1以内的随机数
                Double randomNumber = random.NextDouble();
                WriteDataGroupCh1[0, i] = i;
                WriteDataGroupCh1[1, i] = randomNumber;
                WriteDataGroupCh2[0, i] = i;
                WriteDataGroupCh2[1, i] = randomNumber + 0.2;
                // 将随机数添加到集合中
                randomNumbers.Add(randomNumber);
                randomValues1.Add(randomNumber + 0.2);
            }
            var viewModel = (MainViewModel)DataContext;
            await viewModel.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 0);
            await viewModel.UpDataView1(randomValues.ToArray(), randomValues1.ToArray(), 1);
            //WriteDataCount++;
            //await WriteDataToExcel(WriteDataGroupCh1, WriteDataCount, true);
            //await WriteDataToExcel(WriteDataGroupCh2, WriteDataCount, false);
            // EquipDataExcelPack.Save();
        }
        #endregion

        //2026-10 真实时间轴：回调签名增加 xIncrement/xOrigin，横坐标改为真实时间（秒）
        private async void SomeActMethod(float[] data,byte Ch1, float xIncrement, float xOrigin)
        {
            //2026-10-17 深存储保存模式：只收集数据，不刷新界面曲线
            if (deepSaveMode)
            {
                DeepSaveCollect(data, Ch1, xIncrement, xOrigin);
                return;
            }
            //2026-10-17 给AI助手存一份波形副本（纯内存，不影响刷图）
            WaveSnapshot.Store(Ch1, data, xIncrement, xOrigin);
            // 创建Random对象

            List<Double> randomNumbers = new List<Double>(), randomValues = new List<Double>(), randomValues1 = new List<double>();
            //2026-10-31 修复10：不再硬编码 1000 点。RIGOL 普通帧 5000 点、LeCroy 实时帧最少 500 点，
            //原写法对 500 点帧直接数组越界（每帧丢失），对 5000 点帧只显示 1/5 时间窗。
            //现按实际点数自适应：超过 1000 点按步长抽样，不足 1000 点整条铺开并放大时间轴，
            //保证任何机型都恰好铺满固定的 1000 点时间窗、不越界。
            int totalPoints = data.Length;
            int step = Math.Max(1, (totalPoints + 999) / 1000);   //抽样步长：确保显示点数 ≤ 1000
            int shown = (totalPoints + step - 1) / step;          //实际显示点数
            double xScale = step * (1000.0 / shown);              //时间轴缩放：铺满整条波形
            for (int i = 0; i < shown; i++)
            {
                int idx = i * step;
                // 保险：若仪器不支持XINCrement查询（解析失败为0），退回用序号当横轴，避免波形挤成竖线
                double t = (xIncrement > 0) ? (xOrigin + i * xIncrement * xScale) : i;
                if(Ch1 == 0)
                {
                    WriteDataGroupCh1[i, 0] = t;
                    WriteDataGroupCh1[i, 1] = data[idx];
                }
                else if(Ch1 == 1)
                {
                    WriteDataGroupCh2[i, 0] = t;
                    WriteDataGroupCh2[i, 1] = data[idx];
                }
                else if (Ch1 == 2)
                {
                    WriteDataGroupCh3[i, 0] = t;
                    WriteDataGroupCh3[i, 1] = data[idx];
                }
                else
                {
                    WriteDataGroupCh4[i, 0] = t;
                    WriteDataGroupCh4[i, 1] = data[idx];
                }
                randomValues.Add(t);

                // 将随机数添加到集合中
                randomNumbers.Add(data[idx]);
            }

            await this.Dispatcher.InvokeAsync(new Action(() =>
            {
                var viewMode1 = (MainViewModel)DataContext;
                if (Ch1 == 1)
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 1);
                }
                else if(Ch1 == 0)
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 0);
                }
                else if(Ch1 == 2)
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 2);
                }
                else
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 3);
                }
                
            }));
        }

        #region 数据存入
        public Task WriteDataToExcel(object[,] Data, int Rows,byte Ch1Data)
        {
            if (Ch1Data == 0)
            {
                string Address = "A2";
                EquipDataExcelSheet.Cells[Address].LoadFromArrays(ConvertToJaggedArray(Data));
            }
            else if (Ch1Data == 1)
            {
                string Address = "C2";
                EquipDataExcelSheet.Cells[Address].LoadFromArrays(ConvertToJaggedArray(Data));
            }
            else if (Ch1Data == 2)
            {
                string Address = "E2";
                EquipDataExcelSheet.Cells[Address].LoadFromArrays(ConvertToJaggedArray(Data));
            }
            else
            {
                string Address = "G2";
                EquipDataExcelSheet.Cells[Address].LoadFromArrays(ConvertToJaggedArray(Data));
            }

            return Task.CompletedTask;
        }

        private void SaveImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }
            try
            {
                Console.WriteLine("[保存] 发送截图保存命令到示波器");
                oscilloscopeClass.SaveimageToUsb();
                Toast.Show(this, "指令设置成功");
            }
            catch (NotSupportedException)
            {
                //力科等机型手册确认无 U 盘目标（HCSU DEST 只有 PRINTER/CLIPBOARD/EMAIL/FILE/REMOTE）：明确提示而不是崩
                Toast.Show(this, "当前示波器不支持存U盘，可改存电脑（见未实现清单第7项）", true);
            }
            catch (Exception ex)
            {
                Toast.Show(this, "截图指令发送失败：" + ex.Message, true);
            }
        }
        static object[][] ConvertToJaggedArray(object[,] multiDimArray)
        {
            int rows = multiDimArray.GetLength(0);
            int cols = multiDimArray.GetLength(1);
            object[][] jaggedArray = new object[rows][];

            for (int i = 0; i < rows; i++)
            {
                jaggedArray[i] = new object[cols];
                for (int j = 0; j < cols; j++)
                {
                    jaggedArray[i][j] = multiDimArray[i, j];
                }
            }
            return jaggedArray;
        }
        #endregion
        #region 信号源设置
        //2026-10-30 原"N2410区块"改为通用的电流源A区块：任何电源机型都走 IPowerSource 接口，
        //只有"通道1/通道2"是 N2410 专属功能，用 as 判断后提示。
        private void Bt24CurrentSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            float value = (float)CurrentValue.Value;
            powerSourceA.SetCurrent(value);
            Toast.Show(this, "指令设置成功");
        }

        private void Bt24LimitCurrentSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            float value = (float)LimitCurrentValue.Value;
            powerSourceA.SetOverLoadCurrent(value);
            Toast.Show(this, "指令设置成功");
        }

        private void Bt24ValueSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            float value = (float)ValueValue.Value;
            powerSourceA.SetValue(value);
            Toast.Show(this, "指令设置成功");
        }

        private void BtnCh1_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;   //BAML 加载阶段会触发 Checked，此时窗口未显示、设备未连接，直接忽略
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            if (powerSourceA is N2410Class n10) n10.SetChannel(1);
            else Toast.Show(this, "通道选择是 N2410 专属功能", true);
        }

        private void BtnCh2_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;   //BAML 加载阶段会触发 Checked，此时窗口未显示、设备未连接，直接忽略
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            if (powerSourceA is N2410Class n10) n10.SetChannel(2);
            else Toast.Show(this, "通道选择是 N2410 专属功能", true);
        }

        private void Btn24On_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            powerSourceA.SetOutput(true);
            Toast.Show(this, "指令设置成功");
        }

        private void Btn24Off_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceA == null) { Toast.Show(this, "电流源A未连接", true); return; }
            powerSourceA.SetOutput(false);
            Toast.Show(this, "指令设置成功");
        }

        //2026-10-30 原"N36200区块"改为通用的电流源B区块。
        private void Bt36CurrentSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceB == null) { Toast.Show(this, "电流源B未连接", true); return; }
            float value = (float)N36CurrentValue.Value;
            powerSourceB.SetCurrent(value);
            Toast.Show(this, "指令设置成功");
        }

        private void Bt36LimitCurrentSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceB == null) { Toast.Show(this, "电流源B未连接", true); return; }
            float value = (float)N36LimitCurrentValue.Value;
            powerSourceB.SetOverLoadCurrent(value);
            Toast.Show(this, "指令设置成功");
        }

        private void Bt36ValueSet_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceB == null) { Toast.Show(this, "电流源B未连接", true); return; }
            float value = (float)N36ValueValue.Value;
            powerSourceB.SetValue(value);
            Toast.Show(this, "指令设置成功");
        }

        private void Image_MouseDown_2(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }   //2026-10-31 修复16
            oscilloscopeClass.Start();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }   //2026-10-31 修复16
            oscilloscopeClass.Stop();
        }

        private void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }   //2026-10-31 修复16
            oscilloscopeClass.Auto();
        }

        private void ValueChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;   //2026-10-31 修复15：BAML 初始化赋值也触发 ValueChanged，此时不发指令
            if (oscilloscopeClass == null) return;
            double v = ValueChange.Value;
            var dev = oscilloscopeClass;   //2026-10-31 审查修订：捕获设备引用，防抖执行瞬间设备被回滚也不NRE
            DispatchSliderCmd(() => dev?.ChanScale(GetSelectChannel(), v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        private void ValueOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;   //2026-10-31 修复15：同上
            if (oscilloscopeClass == null) return;
            double v = ValueOffset.Value;
            var dev = oscilloscopeClass;
            DispatchSliderCmd(() => dev?.ChanOffset(GetSelectChannel(), v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        private void TimeChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;   //2026-10-31 修复15：同上
            if (oscilloscopeClass == null) return;
            double v = TimeChange.Value * 0.000001;
            var dev = oscilloscopeClass;
            DispatchSliderCmd(() => dev?.TimeScale(v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        private void TimeOffsetChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;   //2026-10-31 修复15：同上
            if (oscilloscopeClass == null) return;
            double v = TimeOffsetChange.Value * 0.000001;
            var dev = oscilloscopeClass;
            DispatchSliderCmd(() => dev?.TimeOffset(v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        private string GetSelectChannel()
        {
            if (Ch2Radio?.IsChecked == true) return "CHAN2";
            if (Ch3Radio?.IsChecked == true) return "CHAN3";
            if (Ch4Radio?.IsChecked == true) return "CHAN4";
            return "CHAN1";
        }
        private void ProbeChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;   //2026-10-31 修复15：同上
            if (oscilloscopeClass == null) return;
            double v = ProbeChange.Value;
            var dev = oscilloscopeClass;
            DispatchSliderCmd(() => dev?.ChanProbe(GetSelectChannel(), v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        private void ImpedRadio_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }
            string imp = (Imped50Radio.IsChecked == true) ? "FIFTy" : "OMEG";   //普源机型字典：1MΩ参数为OMEG（MEG会报远程指令错误）
            oscilloscopeClass.ChanImpedance(GetSelectChannel(), imp);
            Toast.Show(this, "指令设置成功");
        }
        //2026-10-30 波形数组改为主窗口本地生成（公式与原 SignalClass 完全一致），
        //不再依赖具体信号源类型。
        private static double[] WaveSine(int points)
        {
            double[] data = new double[points];
            for (int i = 0; i < points; i++)
            {
                data[i] = (Math.Sin(2 * Math.PI * i / points) + 1) / 2;
            }
            return data;
        }

        private static double[] WaveSquare(int points, int dutyPercent)
        {
            double[] data = new double[points];
            int highEnd = points * dutyPercent / 100;
            for (int i = 0; i < points; i++)
            {
                data[i] = i < highEnd ? 1.0 : 0.0;
            }
            return data;
        }

        private void BtnTestff_Click(object sender, RoutedEventArgs e)
        {
            if (signalClass == null) { Toast.Show(this, "信号源未连接", true); return; }
            try
            {
                double[] wave = WaveSine(1000);
                signalClass.DownloadWave(1, wave);
                signalClass.Apply();
                Toast.Show(this, "指令设置成功");
            }
            catch (NotSupportedException)
            {
                //SDG6000X 等未实现波形下载的机型：明确提示而不是崩（见未实现清单第三节第1项）
                Toast.Show(this, "当前信号源不支持波形下载", true);
            }
            catch (Exception ex)
            {
                Toast.Show(this, "波形下载失败：" + ex.Message, true);
            }
        }

        private void WrapPanel_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
           
        }

        private void BtnTestFanff_Click(object sender, RoutedEventArgs e)
        {
            if (signalClass == null) { Toast.Show(this, "信号源未连接", true); return; }
            try
            {
                double[] wave = WaveSquare(2000, 50);
                signalClass.DownloadWave(1, wave);
                signalClass.Apply();
                Toast.Show(this, "指令设置成功");
            }
            catch (NotSupportedException)
            {
                //SDG6000X 等未实现波形下载的机型：明确提示而不是崩（见未实现清单第三节第1项）
                Toast.Show(this, "当前信号源不支持波形下载", true);
            }
            catch (Exception ex)
            {
                Toast.Show(this, "波形下载失败：" + ex.Message, true);
            }
        }

        private void t1TimeValue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //double[] wave = signalClass.Square(10000, t1TimeValue.Value);
            //signalClass.DownloadWave(1, wave);
        }

        private void CH1Display_Checked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x01) == 0x00)
             {
                  oscilloscopeClass.ChannelMask |= 0x01;
             }
        }

        private void CH1Display_Unchecked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x01) == 0x01)
            {
                oscilloscopeClass.ChannelMask &= 0x0E;
            }
        }

        private void CH2Display_Checked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;

            if ((oscilloscopeClass.ChannelMask & 0x02) == 0x00)
                {
                    oscilloscopeClass.ChannelMask |= 0x02;
                }

        }

        private void CH2Display_Unchecked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x02) == 0x02)
            {
                oscilloscopeClass.ChannelMask &= 0x0D;
            }
        }

        private void CH3Display_Checked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x04) == 0x00)
             {
                oscilloscopeClass.ChannelMask |= 0x04;
             }

        }

        private void CH3Display_Unchecked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x04) == 0x04)
            {
                oscilloscopeClass.ChannelMask &= 0x0B;
            }
        }

        private void CH4Display_Checked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x08) == 0x00)
            {
                oscilloscopeClass.ChannelMask |= 0x08;
            }
        }

        private void CH4Display_Unchecked(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //窗口加载阶段/示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
            if ((oscilloscopeClass.ChannelMask & 0x08) == 0x08)
            {
                oscilloscopeClass.ChannelMask &= 0x07;
            }
        }

        //2026-10-31 接口单位已统一为微秒：界面输入框标的就是 us，这里把原值直传，
        //"µs→任意波点数"的换算挪进了各机型适配类（系数 PulsePointsPerUs 在 SignalClass 里），
        //换信号源设备时本方法零改动。
        private void PlaseSet_Click(object sender, RoutedEventArgs e)
        {
            if (signalClass == null) { Toast.Show(this, "信号源未连接", true); return; }
            if (SingleRadio.IsChecked == true)
            {
                signalClass.OutputOneDifferentPulses(t1TimeValue.Value);
            }
            if (DoubleRadio.IsChecked == true)
            {
                signalClass.OutputTwoDifferentPulses(t1TimeValue.Value, t3TimeValue.Value, t2TimeValue.Value);
            }
            Toast.Show(this, "指令设置成功");
        }

        private void StartUpDate_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }   //2026-10-31 修复16
            oscilloscopeClass.DataReadEnabled = true;
            if((oscilloscopeClass.ChannelMask & 0x01) == 0x01)
            {
                oscilloscopeClass.ReadChannelIndex = 0;
            }
            else if((oscilloscopeClass.ChannelMask & 0x02) == 0x02)
            {
                oscilloscopeClass.ReadChannelIndex = 1;
            }
            else if ((oscilloscopeClass.ChannelMask & 0x04) == 0x04)
            {
                oscilloscopeClass.ReadChannelIndex = 2;
            }
            else if ((oscilloscopeClass.ChannelMask & 0x08) == 0x08)
            {
                oscilloscopeClass.ReadChannelIndex = 3;
            }
        }

        private void ValueSet_Click(object sender, RoutedEventArgs e)
        {
            if (signalClass == null) { Toast.Show(this, "信号源未连接", true); return; }
            signalClass.ValueSet((byte)DianYaValue.Value);
            Toast.Show(this, "指令设置成功");
        }

        private void BtnSingle_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }   //2026-10-31 修复16
            oscilloscopeClass.Single();
        }

        private void SingleDegSet_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }
            if(UpRadio.IsChecked == true)
            {
                oscilloscopeClass.TriggerMode(true);
            }
            else
            {
                oscilloscopeClass.TriggerMode(false);
            }
            Toast.Show(this, "指令设置成功");
        }

        private void StoptUpDate_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;   //示波器未连接时忽略
            oscilloscopeClass.DataReadEnabled = false;
        }

        private async void SavetUpDate_Click(object sender, RoutedEventArgs e)
        {
            // ===== ① 确保默认文件夹存在 =====
            string defaultDir = System.IO.Path.GetFullPath(".\\WriteData");
            if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);

            // ===== ② 弹出保存对话框，让用户选位置 =====
            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Title = "选择数据保存位置";
            saveDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx";
            saveDialog.FileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器数据";
            saveDialog.InitialDirectory = defaultDir;
            saveDialog.DefaultExt = ".xlsx";

            // 用户点"取消"就不保存，方法直接结束
            if (saveDialog.ShowDialog() != true) return;

            string excelPath = saveDialog.FileName;   // 用户选定的完整路径

            // ===== ③ 按用户选的路径生成 Excel =====
            CreateExcelFile(excelPath);
            await WriteDataToExcel(WriteDataGroupCh1, WriteDataCount, 0);
            await WriteDataToExcel(WriteDataGroupCh2, WriteDataCount, 1);
            await WriteDataToExcel(WriteDataGroupCh3, WriteDataCount, 2);
            await WriteDataToExcel(WriteDataGroupCh4, WriteDataCount, 3);
            try
            {
                EquipDataExcelPack.Save();
            }
            finally
            {
                //2026-10-31 修复13：保存完（或保存抛异常时）都释放句柄。原来不 Dispose，每次"保存数据"
                //都泄漏一个 ExcelPackage，文件被进程占用，用户保存后无法立即打开/移动该 xlsx
                EquipDataExcelPack.Dispose();
                EquipDataExcelPack = null;
            }

            // ===== ④ 波形截图存到 Excel 同一个文件夹 =====
            var vm = this.DataContext as MainViewModel;
            if (vm == null || vm.PlotModel1 == null) return;

            string picturePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(excelPath),
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器数据.png");
            PngExporter.Export(vm.PlotModel1, picturePath, 1920, 1080);
        }

        #region 深存储保存（2026-10-17 新增）
        private bool deepSaveMode = false;          //深存储收集模式开关
        private int deepSavePoints = 0;             //本次保存点数（1000/10000/100000）
        private string deepSavePath = "";           //用户选定的Excel路径
        private float[][] deepSaveData = new float[4][];   //四通道原始数据
        private float[] deepSaveXInc = new float[4];       //各通道时间间隔
        private float[] deepSaveXOrg = new float[4];       //各通道第0点时刻
        private bool[] deepSaveGot = new bool[4];          //各通道是否已收到
        private bool deepSaveAborted = false;              //2026-10-31 中止幂等标志：异常路径与看门狗/收齐回调互斥
        private Window deepSaveBusyWindow = null;          //"请勿操作"提示框

        //深度保存按钮：问深度 → 问路径 → 显示请勿操作框 → 开始深存储采集
        private void DeepSave_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) return;

            // ===== ① 深度询问框（纯代码创建的小窗口）=====
            ComboBox depthCombo = new ComboBox();
            depthCombo.Items.Add("1000 点（约1倍屏幕）");
            depthCombo.Items.Add("10000 点");
            depthCombo.Items.Add("100000 点（最精细）");
            depthCombo.SelectedIndex = 1;
            depthCombo.Width = 200;
            depthCombo.Height = 28;
            depthCombo.Margin = new Thickness(10);

            Button okBtn = new Button() { Content = "确定", Width = 70, Height = 28, Margin = new Thickness(5) };
            Button cancelBtn = new Button() { Content = "取消", Width = 70, Height = 28, Margin = new Thickness(5) };
            StackPanel btnPanel = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);

            Label tipLabel = new Label() { Content = "选择保存深度（点越多波形越精细）：", FontSize = 14, Margin = new Thickness(10, 10, 10, 0) };

            StackPanel panel = new StackPanel();
            panel.Children.Add(tipLabel);
            panel.Children.Add(depthCombo);
            panel.Children.Add(btnPanel);

            Window depthWin = new Window();
            depthWin.Title = "深度保存";
            depthWin.Width = 280;
            depthWin.Height = 170;
            depthWin.ResizeMode = ResizeMode.NoResize;
            depthWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            depthWin.Owner = this;
            depthWin.Content = panel;
            okBtn.Click += (s, ev) => { depthWin.DialogResult = true; };
            cancelBtn.Click += (s, ev) => { depthWin.DialogResult = false; };
            if (depthWin.ShowDialog() != true) return;

            deepSavePoints = 1000 * (int)Math.Pow(10, depthCombo.SelectedIndex);   //1000/10000/100000

            // ===== ② 路径选择（与"保存数据"同款逻辑）=====
            string defaultDir = System.IO.Path.GetFullPath(".\\WriteData");
            if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);
            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Title = "选择深度数据保存位置";
            saveDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx";
            saveDialog.FileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器深度数据";
            saveDialog.InitialDirectory = defaultDir;
            saveDialog.DefaultExt = ".xlsx";
            if (saveDialog.ShowDialog() != true) return;
            deepSavePath = saveDialog.FileName;

            // ===== ③ "请勿操作示波器"提示框（保存完毕由回调 DeepSaveFinish 关闭）=====
            TextBlock busyText = new TextBlock();
            busyText.Text = "正在读取波形数据，请勿操作示波器！\n（示波器画面会暂时停止，属正常现象）";
            busyText.FontSize = 22;
            busyText.Foreground = Brushes.White;
            busyText.TextAlignment = TextAlignment.Center;
            busyText.VerticalAlignment = System.Windows.VerticalAlignment.Center;   //写全名：同上，避免与Window自带属性冲突
            busyText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;   //写全名：Window类自带同名属性，简写会冲突
            Border busyBorder = new Border();
            busyBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x2D, 0x50));
            busyBorder.Child = busyText;
            deepSaveBusyWindow = new Window();
            deepSaveBusyWindow.WindowStyle = WindowStyle.None;
            deepSaveBusyWindow.ResizeMode = ResizeMode.NoResize;
            deepSaveBusyWindow.Topmost = true;
            deepSaveBusyWindow.Width = 560;
            deepSaveBusyWindow.Height = 140;
            deepSaveBusyWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            deepSaveBusyWindow.Content = busyBorder;
            deepSaveBusyWindow.Show();

            // ===== ④ 初始化缓冲，后台线程开始深存储采集 =====
            //2026-10-31 修复 UI 冻结：BeginDeepSave 在 UI 线程同步执行会冻结界面最长约35秒
            //（力科 *OPC? 20秒长等 + 各家 Sleep），看门狗还可能在采集正常进行时误触发。
            //现在整体挪进后台线程，UI 全程可响应；所有 UI 状态变更经 Dispatcher 回主线程。
            deepSaveData = new float[4][];
            deepSaveXInc = new float[4];
            deepSaveXOrg = new float[4];
            deepSaveGot = new bool[4];
            deepSaveMode = true;
            deepSaveAborted = false;   //新一轮保存：清掉上一轮可能遗留的中止标志
            Console.WriteLine($"[深度保存] 开始：点数={deepSavePoints} 路径={deepSavePath}");
            Task.Run(() =>
            {
                try
                {
                    oscilloscopeClass.BeginDeepSave(deepSavePoints);
                }
                catch (Exception ex)
                {
                    //采集超时/未触发：中止保存，不把半成品写进Excel。UI 收尾回主线程执行
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        deepSaveAborted = true;
                        deepSaveMode = false;
                        deepSaveBusyWindow?.Close();
                        deepSaveBusyWindow = null;
                        MessageBox.Show("深度保存中止：" + ex.Message);
                    }));
                }
                //正常路径无需动作：数据收齐由 DeepSaveCollect → DeepSaveFinish 回调驱动
            });

            // ===== ⑤ 45秒看门狗：仅做"真卡死"的 UI 兜底，不再碰仪器 =====
            //BeginDeepSave 内部自带超时与重同步（力科：*OPC? 超时重建会话恢复 AUTO；RIGOL：自有轮询超时），
            //仪器状态由它负责回收；看门狗只在后台任务彻底卡死时关提示框+提示用户。
            Task.Run(async () =>
            {
                await Task.Delay(45000);
                if (deepSaveBusyWindow != null && !deepSaveAborted)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (deepSaveAborted) return;   //双保险：BeginInvoke 排队期间可能已中止
                        deepSaveBusyWindow?.Close();
                        deepSaveBusyWindow = null;
                        MessageBox.Show("深度保存超时（45秒无响应）。示波器状态可能未恢复，请确认连接后重新连接设备。");
                    }));
                    deepSaveAborted = true;
                }
            });
        }

        //接收线程回调：收集各通道数据，收齐后回调本方法完成收尾
        private void DeepSaveCollect(float[] data, byte ch, float xInc, float xOrg)
        {
            if (deepSaveAborted || !deepSaveMode) return;   //已中止/已收尾：迟到的数据直接丢弃（幂等）
            //验收按"示波器实际生效的深度"来——请求值可能被快时基压小，压小后按实际值读
            int applied = oscilloscopeClass.DeepSavePointsApplied > 0 ? oscilloscopeClass.DeepSavePointsApplied : deepSavePoints;
            if (data.Length < applied) return;   //旧配置的残留数据（点数不够），丢弃
            deepSaveData[ch] = (float[])data.Clone();
            deepSaveXInc[ch] = xInc;
            deepSaveXOrg[ch] = xOrg;
            deepSaveGot[ch] = true;

            byte mask = oscilloscopeClass.ChannelMask;
            for (byte c = 0; c < 4; c++)
            {
                if ((mask & (1 << c)) != 0 && !deepSaveGot[c]) return;   //还有启用的通道没收齐
            }

            deepSaveMode = false;
            Console.WriteLine("[深度保存] 四通道数据收齐，正在写Excel...");
            try { oscilloscopeClass.EndDeepSave(); } catch { }
            Dispatcher.BeginInvoke(new Action(() => DeepSaveFinish()));   //保存完毕回调：关提示框
        }

        //保存完毕回调：写Excel → 关提示框 → 弹完成提示
        private void DeepSaveFinish()
        {
            if (deepSaveAborted) return;   //已中止：看门狗/异常路径已处理过提示，不再重复收尾
            //2026-10-31 修复13：tmpPath 提到 try 外声明，catch 路径也要用它清理临时文件
            string tmpPath = deepSavePath + ".tmp.xlsx";
            try
            {
                //评审意见：先写临时文件，全部通道收齐并保存成功后再改名为正式文件，
                //避免写入中途异常在用户选定的路径留下半成品 Excel
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

                CreateExcelFile(tmpPath);   //复用现有方法：新建Excel并写表头
                for (int c = 0; c < 4; c++)
                {
                    if (deepSaveData[c] == null) continue;
                    int n = deepSaveData[c].Length;
                    object[][] rows = new object[n][];
                    for (int i = 0; i < n; i++)
                    {
                        //时间轴：XINC解析成功用真实时间，失败退回序号
                        double t = (deepSaveXInc[c] > 0) ? (deepSaveXOrg[c] + i * deepSaveXInc[c]) : i;
                        rows[i] = new object[2] { t, (object)deepSaveData[c][i] };
                    }
                    string address = (c == 0) ? "A2" : (c == 1) ? "C2" : (c == 2) ? "E2" : "G2";
                    EquipDataExcelSheet.Cells[address].LoadFromArrays(rows);
                }
                EquipDataExcelPack.Save();
                EquipDataExcelPack.Dispose();          //释放文件锁，否则 Move 会报"文件被占用"
                EquipDataExcelPack = null;
                //提交：临时文件 → 正式文件（.NET Framework 的 Move 无 overwrite 重载，先删后移）
                try { if (File.Exists(deepSavePath)) File.Delete(deepSavePath); } catch { }
                File.Move(tmpPath, deepSavePath);
                Console.WriteLine("[深度保存] Excel写入完成");
                //深存储波形写入专用深存槽（与普通刷新快照分离，互不覆盖）：
                //参数提取界面和AI助手通过 WaveSnapshot.Get 自动优先拿到深存储数据
                for (int c = 0; c < 4; c++)
                {
                    if (deepSaveData[c] == null) continue;
                    WaveSnapshot.StoreDeep(c, deepSaveData[c], deepSaveXInc[c], deepSaveXOrg[c]);
                }
                MessageBox.Show("深度保存完成！波形已载入参数提取界面（深存储数据优先）。");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[深度保存] 保存失败：" + ex.Message);
                //2026-10-31 修复13：失败路径也要释放 Excel 句柄并删除临时文件，
                //否则文件锁一直占用（用户打不开/删不掉 xlsx），.tmp.xlsx 残留
                try { EquipDataExcelPack?.Dispose(); } catch { }
                EquipDataExcelPack = null;
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                MessageBox.Show("深度保存失败：" + ex.Message);
            }
            finally
            {
                deepSaveBusyWindow?.Close();   //回调关闭"请勿操作"提示框
                deepSaveBusyWindow = null;
            }
        }
        #endregion

        private void SinglSourceSet_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }
            if(Ch1Radio.IsChecked == true)
            {
                oscilloscopeClass.TriggerSource("CHAN1");
            }
            else if(Ch2Radio.IsChecked == true)
            {
                oscilloscopeClass.TriggerSource("CHAN2");
            }
            else if (Ch3Radio.IsChecked == true)
            {
                oscilloscopeClass.TriggerSource("CHAN3");
            }
            else if (Ch4Radio.IsChecked == true)
            {
                oscilloscopeClass.TriggerSource("CHAN4");
            }
            Toast.Show(this, "指令设置成功");
        }

        private void SinglLevelSet_Click(object sender, RoutedEventArgs e)
        {
            if (oscilloscopeClass == null) { Toast.Show(this, "示波器未连接", true); return; }
            oscilloscopeClass.TriggerLevel((double)ValueTrigger.Value);
            Toast.Show(this, "指令设置成功");
        }

        //2026-10-30 电流源B的输出开关（原 N36200 专属，现走通用接口）
        private void Btn36On_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceB == null) { Toast.Show(this, "电流源B未连接", true); return; }
            powerSourceB.SetOutput(true);
            Toast.Show(this, "指令设置成功");
        }

        private void Btn36Off_Click(object sender, RoutedEventArgs e)
        {
            if (powerSourceB == null) { Toast.Show(this, "电流源B未连接", true); return; }
            powerSourceB.SetOutput(false);
            Toast.Show(this, "指令设置成功");
        }
        #endregion
    }
}
