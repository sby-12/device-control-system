using System;
using System.Collections.Generic;
using System.Diagnostics;               // Process：用于程序退出时杀掉自身进程
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ValueSourceN2410ClassLibrary;     // 两台电压源（Modbus-RTU 协议）
using OscilloscopeClassLibrary;         // 示波器（SCPI 文本协议）
using SingalClassLibrary;               // 信号源（SCPI 文本协议）
using System.IO;                        // 文件操作（日志、Excel、PNG 路径）
using System.Reflection;                // Assembly：获取 exe 所在目录
using System.IO;
using System.Reflection;
using OfficeOpenXml;                    // EPPlus：读写 Excel（NuGet 包）
using LicenseContext = OfficeOpenXml.LicenseContext;
using OfficeOpenXml.Drawing.Slicer.Style;
using OxyPlot.Wpf;                      // OxyPlot 的 WPF 组件（PngExporter 截图导出用）
using OxyPlot;
using System.Windows.Media.Media3D;
using Microsoft.Win32;                  // SaveFileDialog 保存对话框

namespace MainSoftwareV100
{
    /// <summary>
    /// 主窗口后台代码：整个软件的大脑。
    /// 职责：①创建四台仪器的驱动对象并把界面回调"挂"上去；②处理全部按钮事件；
    ///       ③把示波器数据送进图表和Excel；④写运行日志。
    /// 架构关系：MainWindow 引用三个类库（单向），类库通过委托回调通知这里。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 变量定义
        N2410Class n2410Class = null;       // N2410 电压源（地址1，Modbus 协议）
        N36200Class n36200Class = null;     // N36200 电压源（地址2，Modbus 协议）
        ConnectInit connectInit = null;     // 连接设置弹窗
        OscilloscopeClass oscilloscopeClass = null;  // 示波器（SCPI 协议）
        SignalClass signalClass  = null;    // 信号源（SCPI 协议）

        ExcelPackage EquipDataExcelPack = null;      // EPPlus 的 Excel 文件对象
        ExcelWorksheet EquipDataExcelSheet = null;   // Excel 里的工作表对象
        string logFilePath;                 // 日志文件完整路径（含文件名）
        int WriteDataUpLimit = 0, WriteDataCount = 0;   // ⚠ 基本未使用，历史遗留
        // 四个通道的数据仓库：每张 1000行×2列 的表，[i,0]=时间，[i,1]=电压值
        // ⚠ 潜在问题：1000 是写死的，若示波器点数(:WAVeform:POINts)改了，这里会存不下/越界
        object[,] WriteDataGroupCh1 = new object[1000, 2], WriteDataGroupCh2 = new object[1000, 2], WriteDataGroupCh3 = new object[1000, 2], WriteDataGroupCh4 = new object[1000, 2];
        #endregion
        public MainWindow()
        {
            InitLogs();                     // ① 先建日志系统（要在第一行，记录"软件再次被打开"）

            n2410Class = new N2410Class(1); // ② 创建 N2410，仪器地址 1
            n2410Class.translateClass.thisNetPublicTool = N3416InfDisplay;
            // ↑ 挂回调：N2410 周期上报电压/电流时，调用 N3416InfDisplay 刷新界面显示
            n2410Class.translateClass.reciveState = WriteDataLog;
            // ↑ 挂回调：连接状态/异常时调用 WriteDataLog 写日志+更新状态栏

            n36200Class = new N36200Class(2);  // ③ 创建 N36200，仪器地址 2（同款接线方式）
            n36200Class.translateClass.thisNetPublicTool = N3621InfDisplay;
            n36200Class.translateClass.reciveState = WriteDataLog;

            oscilloscopeClass = new OscilloscopeClass();   // ④ 创建示波器
            oscilloscopeClass.translateClass.reciveState += WriteDataLog;   // += 因为 MainWindow 构造前可能已有订阅？（此处实际只有这一处，用 = 亦可）
            oscilloscopeClass.translateClass.thisNetPublicTool=SomeActMethod;
            // ↑ 挂回调：示波器波形数据到达时调用 SomeActMethod（核心数据入口！）

            connectInit = new ConnectInit();  // ⑤ 创建连接弹窗
            signalClass = new SignalClass();  // ⑥ 创建信号源
            signalClass.translateClass.reciveState += WriteDataLog;

            // ⑦ 把自己持有的仪器对象"交给"连接弹窗用（弹窗里点连接时要用）
            connectInit.m_n2410Class = n2410Class;
            connectInit.m_n36200Class = n36200Class;
            connectInit.m_oscilloscopeClass = oscilloscopeClass;
            connectInit.m_singalClass = signalClass;

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            // ↑ EPPlus 5.x 要求显式声明许可类型，不写这一行直接抛异常
           // CreateExcelFile();
            // ↑ 原设计：启动就建 Excel 文件；已注释掉，改为点"保存"时才建（见 SavetUpDate_Click）
            InitializeComponent();          // ⑧ 加载 XAML 界面（WPF 规定必须在逻辑代码使用前调用）

        }


        /// <summary>
        /// 【2026-10 修改】创建 Excel 文件。原来路径写死，改为由调用者传入（配合保存对话框）。
        /// </summary>
        private void CreateExcelFile(string TxtPath)
        {
            //局放数据
            //（原版此处为：string TxtPath = ".\\WriteData\\" + 时间戳 + " 示波器数据.xlsx";）
            EquipDataExcelPack = new ExcelPackage(TxtPath);   // 在指定路径创建/打开 xlsx
            EquipDataExcelSheet = EquipDataExcelPack.Workbook.Worksheets.Add("数据记录");  // 新建工作表
            EquipDataExcelSheet.DefaultColWidth = 12;         // 默认列宽 12

            // 写表头：四个通道各占两列（时间+数据）
            // ⚠ 原版这里的时间列填的是"采样点序号"，名不副实；【2026-10 真实时间轴】修复后此处存的是真实秒数
                EquipDataExcelSheet.Cells[1, 1].Value = "CH1时间";
                EquipDataExcelSheet.Cells[1, 2].Value = "CH1数据";
                EquipDataExcelSheet.Cells[1, 3].Value = "CH2时间";
                EquipDataExcelSheet.Cells[1, 4].Value = "CH2数据";
                EquipDataExcelSheet.Cells[1, 5].Value = "CH3时间";
                EquipDataExcelSheet.Cells[1, 6].Value = "CH3数据";
                EquipDataExcelSheet.Cells[1, 7].Value = "CH4时间";
                EquipDataExcelSheet.Cells[1, 8].Value = "CH4数据";
        }

        /// <summary>
        /// 日志系统初始化：在 exe 所在目录建 Logs 文件夹，按日期命名日志文件。
        /// </summary>
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

            // 根据当前日期生成日志文件名（一天一个文件）
            string logFileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
            // 拼接日志文件的完整路径
            logFilePath = System.IO.Path.Combine(logFolderPath, logFileName);
            WriteDataLog("软件再次被打开",0,false);
        }

        /// <summary>
        /// 全局日志+状态栏方法（四台仪器共用的回调）。
        /// 每个仪器库里连接成功/失败、收发异常时都会调到这里。
        /// </summary>
        /// <param name="WriteData">日志内容</param>
        /// <param name="EquipAddress">仪器编号：1 N2410; 2 N36200; 3 示波器; 4 信号源</param>
        /// <param name="Connectflag">该仪器的连接状态（true=已连接）</param>
        void WriteDataLog(string WriteData, Byte EquipAddress,bool Connectflag) // 0 默认;1 N2410;2 N36200;3 示波器;4 信号源
        {
            // 要写入的日志内容（加毫秒级时间戳）
            string logMessage = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss.fff") + " " + WriteData;
            //将日志内容追加到日志文件中（Task.Run 放后台线程写，不阻塞界面）
            Task.Run(() => File.AppendAllText(logFilePath, logMessage + Environment.NewLine));

            // 按仪器编号更新主界面底部状态栏文字
            // ⚠ 潜在问题：连接"失败"时传的是 Connectflag=true（看上面仪器库 ConnectAsync 的调用），
            //    失败也显示"已连接"，是学长的笔误，属于已知缺陷
            if(EquipAddress == 1)
            {
                if(Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() => { mEleN2410Status.Text = "N2410已连接"; }));
                    // ↑ Dispatcher.BeginInvoke：把改界面的操作"拜托"给 UI 线程执行
                    //   （回调来自仪器库的后台线程，后台线程不能直接碰界面控件）
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() => { mEleN2410Status.Text = "N2410未连接"; }));
                }
            }
            if (EquipAddress == 2)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() => { mEleN3620State.Text = "N3620已连接"; }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() => { mEleN3620State.Text = "N3620未连接"; }));
                }
            }
            if (EquipAddress == 3)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() => { ShiBoState.Text = "示波器已连接"; }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() => { ShiBoState.Text = "示波器未连接"; }));
                }
            }
            if (EquipAddress == 4)
            {
                if (Connectflag)
                {
                    Dispatcher.BeginInvoke(new Action(() => { SourceState.Text = "信号源已连接"; }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() => { SourceState.Text = "信号源未连接"; }));
                }
            }
        }

        /// <summary>
        /// N2410 电压/电流显示回调（被 N2410 库周期调用，200ms 一次）。
        /// data[0]=电压值，data[1]=电流值。
        /// </summary>
        void N3416InfDisplay(float[] data)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                mN2410Value.Text = data[0].ToString("0.000");    // 显示三位小数
                mN2410Current.Text = data[1].ToString("0.000");
            }));
        }

        /// <summary>
        /// N36200 电压/电流显示回调（同上，N36200 版）。
        /// </summary>
        void N3621InfDisplay(float[] data)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                mN3620Value.Text = data[0].ToString("0.000");
                mN3620Current.Text = data[1].ToString("0.000");
            }));
        }

        /// <summary>
        /// 窗口加载完毕：把窗口设为全屏无边框（学长写死了全屏，不可调整）。
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            #region 设置全屏
            //设置全屏
            this.WindowState = System.Windows.WindowState.Normal;
            this.WindowStyle = System.Windows.WindowStyle.None;      // 去掉标题栏
            this.ResizeMode = System.Windows.ResizeMode.NoResize;    // 禁止调整大小
            this.Topmost = false;

            this.Left = 0.0;
            this.Top = 0.0;
            this.Width = System.Windows.SystemParameters.PrimaryScreenWidth;   // 宽=屏幕宽
            this.Height = System.Windows.SystemParameters.PrimaryScreenHeight; // 高=屏幕高
            #endregion
        }

        /// <summary>
        /// 按住左键拖动窗口（无边框窗口没有系统标题栏，要自己实现拖动）。
        /// 全屏状态下先还原成普通窗口再拖。
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            System.Windows.Point position = e.GetPosition(this);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (position.X >= 0 && position.X < this.ActualWidth && position.Y >= 0 && position.Y <= this.ActualHeight)
                {
                    this.DragMove();   // WPF 自带：跟着鼠标拖窗口
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

        /// <summary>
        /// 右上角退出按钮：关信号源输出 → 直接杀掉自己的进程。
        /// ⚠ 潜在问题：Process.Kill() 是强杀，不走过正常的窗口关闭流程——
        ///    如果此时 Excel 还没保存会损坏文件；仪器连接也不会优雅关闭。属于"能用但糙"的写法。
        /// </summary>
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //CloseLog();
            _ = signalClass.translateClass.SendAsync(System.Text.Encoding.Default.GetBytes(":OUTP1 OFF" + "\n"));
            Process.GetCurrentProcess().Kill();
        }

        /// <summary>
        /// 右上角插头图标：弹出连接设置窗口。
        /// </summary>
        private void Image_MouseDown_1(object sender, MouseButtonEventArgs e)
        {
            connectInit.Show();
        }

        #region 测试代码
        /// <summary>
        /// 【测试代码】生成随机波形喂给图表，验证绘图功能（从未被调用）。
        /// ⚠ 潜在问题：里面 WriteDataGroupCh1[0, i] 的索引写反了（应为 [i, 0]），是死代码里的笔误。
        /// </summary>
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
                WriteDataGroupCh1[0, i] = i;        // ⚠ 索引反了：数组是[1000, 2]，第一维最大999，i到999时越界
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

        /// <summary>
        /// ★★★ 核心数据入口 ★★★
        /// 示波器波形数据到达时被 OscilloscopeClassLibrary 回调（委托 thisNetPublicTool）。
        /// 做三件事：①把数据+真实时间存进四个通道的二维数组（供 Excel 用）；
        ///           ②把真实时间数组和电压数组交给图表模型刷新曲线。
        /// </summary>
        /// <param name="data">1000 个电压值</param>
        /// <param name="Ch1">通道号：0=CH1, 1=CH2, 2=CH3, 3=CH4</param>
        /// <param name="xIncrement">相邻点时间间隔（秒），来自 :WAVeform:XINCrement?</param>
        /// <param name="xOrigin">第 0 点的时刻（秒），来自 :WAVeform:XORigin?</param>
        //【2026-10 真实时间轴】回调签名增加 xIncrement/xOrigin，横坐标改为真实时间（秒）
        private async void SomeActMethod(float[] data,byte Ch1, float xIncrement, float xOrigin)
        {
            // 创建Random对象

            List<Double> randomNumbers = new List<Double>(), randomValues = new List<Double>(), randomValues1 = new List<double>();
            // 循环生成1000个随机数（注释是学长从测试代码复制来的，实际是处理1000个真实采样点）
            for (int i = 0; i < 1000; i++)
            {
                // 保险：若仪器不支持XINCrement查询（解析失败为0），退回用序号当横轴，避免波形挤成竖线
                double t = (xIncrement > 0) ? (xOrigin + i * xIncrement) : i;
                // ↑【原版】double t = ... 无此概念，直接 WriteDataGroupCh1[i, 0] = i;（序号当时间，是"假时间轴"的病根）
                if(Ch1 == 0)
                {
                    WriteDataGroupCh1[i, 0] = t;   // [i,0] 时间列
                    WriteDataGroupCh1[i, 1] = data[i];  // [i,1] 电压值
                }
                else if(Ch1 == 1)
                {
                    WriteDataGroupCh2[i, 0] = t;
                    WriteDataGroupCh2[i, 1] = data[i];
                }
                else if (Ch1 == 2)
                {
                    WriteDataGroupCh3[i, 0] = t;
                    WriteDataGroupCh3[i, 1] = data[i];
                }
                else
                {
                    WriteDataGroupCh4[i, 0] = t;
                    WriteDataGroupCh4[i, 1] = data[i];
                }
                randomValues.Add(t);            // X 数组（真实时间）
               
                // 将随机数添加到集合中
                randomNumbers.Add(data[i]);     // Y 数组（电压值）
            }

            // 切到 UI 线程刷新图表（接收回调来自后台线程）
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
                else if (Ch1 == 2)
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 2);
                }
                else
                {
                    Task task = viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), 3);
                }
                // ⚠ 潜在问题：上面四段 if 其实是同一种调用，只有通道号不同；
                //    直接写成 viewMode1.UpDataView1(randomValues.ToArray(), randomNumbers.ToArray(), Ch1) 即可（待优化项）
            }));
        }

        #region 数据存入
        /// <summary>
        /// 把一个通道的二维数组写入 Excel 固定位置（A2/C2/E2/G2 起，各两列）。
        /// ⚠ 注意：Rows 参数传进来没用上；每次都从第2行起写，会覆盖旧数据——
        ///    由于现在每次保存都新建文件，所以恰好没出毛病。
        /// </summary>
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

            return Task.CompletedTask;   // 方法没有真正的异步操作，返回"已完成"（Task 约定）
        }

        /// <summary>
        /// 把 object[1000, 2]（二维数组）转成 object[1000][2]（交错数组），
        /// 因为 EPPlus 的 LoadFromArrays 只接受交错数组。纯格式转换，无业务逻辑。
        /// </summary>
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
        // ===== N2410（电压源1）设置区 =====

        // 电流设置按钮：读取输入框数值 → 下发 SetCurrent
        private void Bt24CurrentSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)CurrentValue.Value;    // NumericUpDown 自定义控件的 .Value
            n2410Class.SetCurrent(value);
        }

        // 限电流（过流保护）设置
        private void Bt24LimitCurrentSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)LimitCurrentValue.Value;
            n2410Class.SetOverLoadCurrent(value);
        }

        // 电压设置
        private void Bt24ValueSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)ValueValue.Value;
            n2410Class.SetValue(value);
        }

        // 通道1/通道2 单选（N2410 是双通道机型）
        private void BtnCh1_Checked(object sender, RoutedEventArgs e)
        {
            n2410Class.SetChannel(1);
        }

        private void BtnCh2_Checked(object sender, RoutedEventArgs e)
        {
            n2410Class.SetChannel(2);
        }

        // N2410 输出开关
        private void Btn24On_Click(object sender, RoutedEventArgs e)
        {
            n2410Class.SetChannelOpen(true);
        }

        private void Btn24Off_Click(object sender, RoutedEventArgs e)
        {
            n2410Class.SetChannelOpen(false);
        }

        // ===== N36200（电压源2）设置区：与上面完全同款 =====

        private void Bt36CurrentSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)N36CurrentValue.Value;
            n36200Class.SetCurrent(value);
        }

        private void Bt36LimitCurrentSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)N36LimitCurrentValue.Value;
            n36200Class.SetOverLoadCurrent(value);
        }

        private void Bt36ValueSet_Click(object sender, RoutedEventArgs e)
        {
            float value = (float)N36ValueValue.Value;
            n36200Class.SetValue(value);
        }

        // 窗口最小化（右上角"—"）
        private void Image_MouseDown_2(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // ===== 示波器控制区 =====

        // RUN：示波器开始采集
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.Start();
        }

        // STOP：停止采集
        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.Stop();
        }

        // AUTO：自动设置量程
        private void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.Auto();
        }

        /// <summary>
        /// 电压变换（V/div）滑块变化：给"当前选中通道"发 :CHANnelx:SCALe 指令。
        /// ⚠ 潜在问题：事件绑定在数值输入框上，每敲一位数字/每点一下箭头都会触发一次发指令，
        ///    没有"回车/失焦才生效"的节流，会往仪器刷一串指令。
        /// </summary>
        private void ValueChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (oscilloscopeClass != null)
            {
                if (Ch1Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanScale("CHANnel1", (ValueChange.Value * 1).ToString());
                }
                else if (Ch2Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanScale("CHANnel2", (ValueChange.Value * 1).ToString());
                }
                else if(Ch3Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanScale("CHANnel3", (ValueChange.Value * 1).ToString());
                }
                else
                {
                    oscilloscopeClass.ChanScale("CHANnel4", (ValueChange.Value * 1).ToString());
                }
            }
           

        }

        /// <summary>
        /// 电压偏移滑块：发 :CHANnelx:OFFSet 指令（同款逻辑，同样无节流）。
        /// </summary>
        private void ValueOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (oscilloscopeClass != null)
            {
                if (Ch1Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanOffset("CHANnel1", (ValueOffset.Value * 1).ToString());
                }
                else if(Ch2Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanOffset("CHANnel2", (ValueOffset.Value * 1).ToString());
                }
                else if (Ch3Radio?.IsChecked == true)
                {
                    oscilloscopeClass.ChanOffset("CHANnel3", (ValueOffset.Value * 1).ToString());
                }
                else
                {
                    oscilloscopeClass.ChanOffset("CHANnel4", (ValueOffset.Value *1).ToString());
                }
            }
        }

        /// <summary>
        /// 时间档位（us/div）滑块：发 :TIMebase:MAIN:SCALe 指令。
        /// ⚠ 注意：if/else 两个分支内容完全一样（都是发指令），是无意义分支；
        ///    且没选通道时也会发（TimeScale 与通道无关，倒也无碍）。
        ///    ×0.000001 是把界面上的"us"换算成"秒"。
        /// </summary>
        private void TimeChange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (oscilloscopeClass != null)
            {
                if (Ch1Radio?.IsChecked == true)
                {
                    oscilloscopeClass.TimeScale((TimeChange.Value * 0.000001).ToString());
                }
                else
                {
                    oscilloscopeClass.TimeScale((TimeChange.Value * 0.000001).ToString());
                }

            }
        }

        // 【测试按钮】下载正弦波到信号源并触发一次
        private void BtnTestff_Click(object sender, RoutedEventArgs e)
        {
            double[] wave = signalClass.Sine(1000);
            signalClass.DownloadWave(1, wave);
            signalClass.Apply();
        }

        private void WrapPanel_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
           
        }

        // 【测试按钮】下载方波到信号源并触发一次
        private void BtnTestFanff_Click(object sender, RoutedEventArgs e)
        {
            double[] wave = signalClass.Square(2000,50);
            signalClass.DownloadWave(1, wave);
            signalClass.Apply();
        }

        // 空事件（预留）
        private void t1TimeValue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //double[] wave = signalClass.Square(10000, t1TimeValue.Value);
            //signalClass.DownloadWave(1, wave);
        }

        // ===== CH1~CH4 曲线显隐勾选框：维护位掩码 CHANNum =====
        // 位掩码约定：bit0=CH1, bit1=CH2, bit2=CH3, bit3=CH4
        // 勾选一个通道 → 对应位置1；取消 → 清0。同时暂停刷新（EquipDataRead=false），
        // 让正在进行的轮询停下来，避免"勾选瞬间读到一半"的状态错乱。
        private void CH1Display_Checked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x01) == 0x00)
             {
                  oscilloscopeClass.translateClass.CHANNum |= 0x01;
             }
        }

        private void CH1Display_Unchecked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x01) == 0x01)
            {
                oscilloscopeClass.translateClass.CHANNum &= 0x0E;   // 清bit0
            }
        }

        private void CH2Display_Checked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;

            if ((oscilloscopeClass.translateClass.CHANNum & 0x02) == 0x00)
                {
                    oscilloscopeClass.translateClass.CHANNum |= 0x02;
                }

        }

        private void CH2Display_Unchecked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x02) == 0x02)
            {
                oscilloscopeClass.translateClass.CHANNum &= 0x0D;   // 清bit1
            }
        }

        private void CH3Display_Checked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x04) == 0x00)
             {
                oscilloscopeClass.translateClass.CHANNum |= 0x04;
             }

        }

        private void CH3Display_Unchecked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x04) == 0x04)
            {
                oscilloscopeClass.translateClass.CHANNum &= 0x0B;   // 清bit2
            }
        }

        private void CH4Display_Checked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x08) == 0x00)
            {
                oscilloscopeClass.translateClass.CHANNum |= 0x08;
            }
        }

        private void CH4Display_Unchecked(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
            if ((oscilloscopeClass.translateClass.CHANNum & 0x08) == 0x08)
            {
                oscilloscopeClass.translateClass.CHANNum &= 0x07;   // 清bit3
            }
        }

        /// <summary>
        /// 脉冲设置按钮：单脉冲/双脉冲 两种模式，把时间换算成点数后下载波形并触发。
        /// ×8.12 是把时间换算成波形点数（基于 8.12 点/us 的采样率设定，属于实验调出来的系数）。
        /// </summary>
        private void PlaseSet_Click(object sender, RoutedEventArgs e)
        {
            if (SingleRadio.IsChecked == true)
            {
                signalClass.OutputOneDifferentPulses((int)(t1TimeValue.Value*8.12));
            }
            if (DoubleRadio.IsChecked == true)
            {
            //    signalClass.DoublePlaseSet((double)t1TimeValue.Value / 1000000, (double)t2TimeValue.Value / 1000000);
                signalClass.OutputTwoDifferentPulses((int)(t1TimeValue.Value*8.12), (int)(t3TimeValue.Value*8.12), (int)(t2TimeValue.Value*8.12));
            }
            
        }

        /// <summary>
        /// 启动刷新按钮：开始从示波器连续取波形。
        /// 做法：把 EquipDataRead 置 true（定时器看到它才会发问），
        /// 并确定从哪个通道开始（取勾选的通道里编号最小的）。
        /// ⚠ 潜在问题：启动后通道轮询顺序固定从小编号开始，不算bug但要心里有数。
        /// </summary>
        private void StartUpDate_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = true;
            if((oscilloscopeClass.translateClass.CHANNum & 0x01) == 0x01)
            {
                oscilloscopeClass.translateClass.Chnumber = 0;   // 从CH1开始
            }
            else if((oscilloscopeClass.translateClass.CHANNum & 0x02) == 0x02)
            {
                oscilloscopeClass.translateClass.Chnumber = 1;
            }
            else if ((oscilloscopeClass.translateClass.CHANNum & 0x04) == 0x04)
            {
                oscilloscopeClass.translateClass.Chnumber = 2;
            }
            else if ((oscilloscopeClass.translateClass.CHANNum & 0x08) == 0x08)
            {
                oscilloscopeClass.translateClass.Chnumber = 3;
            }
        }

        // 信号源电压设置（高电平电压/2 作为偏移）
        private void ValueSet_Click(object sender, RoutedEventArgs e)
        {
            signalClass.ValueSet((byte)DianYaValue.Value);
        }

        // 示波器单次触发
        private void BtnSingle_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.Single();
        }

        /// <summary>
        /// 边沿选择：上升沿/下降沿 单选 → 发 :TRIGger:EDGE:SLOPe POS/NEG
        /// </summary>
        private void SingleDegSet_Click(object sender, RoutedEventArgs e)
        {
            if(UpRadio.IsChecked == true)
            {
                oscilloscopeClass.TriggerMode(true);
            }
            else
            {
                oscilloscopeClass.TriggerMode(false);
            }
        }

        // 暂停刷新：把 EquipDataRead 置 false，定时器不再发问（正在进行的问答会走完）
        private void StoptUpDate_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.translateClass.EquipDataRead = false;
        }

        /// <summary>
        /// 【2026-10 重写】保存数据按钮。
        /// 原版：写死路径直接存 Excel + PNG；
        /// 新版：①弹保存对话框让用户选位置（取消则不保存）②Excel 存到所选位置
        ///      ③波形截图存到 Excel 同目录。
        /// </summary>
        private async void SavetUpDate_Click(object sender, RoutedEventArgs e)
        {
            // ===== ① 确保默认文件夹存在 =====
            string defaultDir = System.IO.Path.GetFullPath(".\\WriteData");
            // ↑ 写全名 System.IO.Path：因为本文件 using 了 System.Windows.Shapes，里面也有个 Path 类，光写 Path 会歧义
            if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);

            // ===== ② 弹出保存对话框，让用户选位置 =====
            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Title = "选择数据保存位置";
            saveDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx";   // 文件类型下拉框（显示名|通配符）
            saveDialog.FileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器数据";  // 默认文件名（无后缀）
            saveDialog.InitialDirectory = defaultDir;             // 弹窗初始目录
            saveDialog.DefaultExt = ".xlsx";

            // 用户点"取消"就不保存，方法直接结束
            if (saveDialog.ShowDialog() != true) return;

            string excelPath = saveDialog.FileName;   // 用户选定的完整路径

            // ===== ③ 按用户选的路径生成 Excel 并写入四个通道 =====
            CreateExcelFile(excelPath);
            await WriteDataToExcel(WriteDataGroupCh1, WriteDataCount, 0);
            await WriteDataToExcel(WriteDataGroupCh2, WriteDataCount, 1);
            await WriteDataToExcel(WriteDataGroupCh3, WriteDataCount, 2);
            await WriteDataToExcel(WriteDataGroupCh4, WriteDataCount, 3);
            EquipDataExcelPack.Save();

            // ===== ④ 波形截图存到 Excel 同一个文件夹 =====
            var vm = this.DataContext as MainViewModel;
            if (vm == null || vm.PlotModel1 == null) return;

            string picturePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(excelPath),
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " 示波器数据.png");
            PngExporter.Export(vm.PlotModel1, picturePath, 1920, 1080);   // OxyPlot 导出 1920×1080 PNG
        }

        /// <summary>
        /// 触发源按钮：按当前选中的通道单选发 :TRIGger:EDGE:SOURce CHANx
        /// </summary>
        private void SinglSourceSet_Click(object sender, RoutedEventArgs e)
        {
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
        }

        // 触发电压设置：发 :TRIGger:EDGE:LEVel
        private void SinglLevelSet_Click(object sender, RoutedEventArgs e)
        {
            oscilloscopeClass.TriggerLevel((double)ValueTrigger.Value);
        }

        // N36200 输出开关
        private void Btn36On_Click(object sender, RoutedEventArgs e)
        {
            n36200Class.SetChannelOpenOrOff(true);
        }

        private void Btn36Off_Click(object sender, RoutedEventArgs e)
        {
            n36200Class.SetChannelOpenOrOff(false);
        }
        #endregion
    }
}
