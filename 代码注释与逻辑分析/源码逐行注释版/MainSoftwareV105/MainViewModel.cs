using OxyPlot;                        // 图表核心库：PlotModel（图）、LineSeries（曲线）、DataPoint（点）
using OxyPlot.Axes;                   // 坐标轴：LinearAxis（线性轴）
using OxyPlot.Series;                 // 曲线系列
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;   // ObservableCollection：可被界面监听变化的集合
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;         // WPF 线程调度（跨线程刷新界面用）


namespace MainSoftwareV100
{
    /// <summary>
    /// 波形绘图的数据模型（ViewModel）。
    /// 注意：它叫 ViewModel 但没有继承 INotifyPropertyChanged，
    /// 是靠构造函数里手动填 300 个假点 + UpDataView1 里手动 InvalidatePlot 来驱动刷新的。
    /// 数据流向：MainWindow 收到示波器数据 → 调 UpDataView1 → 这里更新曲线 → OxyPlot 重绘。
    /// </summary>
    public class MainViewModel
    {
        public ObservableCollection<PlotSeriesViewModel1> SeriesList1 { get; set; }
        // ↑ 四条曲线的包装集合，XAML 左侧的 CH1~CH4 勾选框就是绑定到它（每个元素有 IsVisible1 属性）

        public PlotModel PlotModel1 { get; set; }
        // ↑ OxyPlot 的"图"对象：装坐标轴 + 曲线 + 标题。XAML 里 <oxy:PlotView Model="{Binding PlotModel1}"/> 绑定它

        public LineSeries newSeries = null;
        // ↑ 注意：仅为 InitQuXianView 内部使用而声明的公共字段，设计较随意，属于历史遗留

        public int _seriesCount1 = 0;          // 注意：加曲线后从未被真正用于判断，基本是摆设字段
        private int _seriesCount2 = 0;         // 从未使用（编译警告 CS0414 的来源之一）
        private int _seriesCount3 = 0;         // 从未使用
        private int _seriesCount4 = 0;         // 从未使用
        private int _seriesCount5 = 0;         // 从未使用

        public MainViewModel()
        {
            // ① 建图对象，设标题
            PlotModel1 = new PlotModel() { Title = "波形曲线" };

            // ② 加横轴：位置在底部，标题"时间"
            PlotModel1.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Bottom,
                Title = "时间",
                IsZoomEnabled = true,          // 允许滚轮缩放
                IsPanEnabled = true,           // 允许拖拽平移
                MajorGridlineStyle = LineStyle.Solid,  // 主网格实线
            });
            // ⚠ 潜在问题：全程序只有这一根 Y 轴，四条曲线共用 → 这就是"纵坐标无法独立调节"的根本原因

            // ③ 加纵轴：位置在左侧，标题"数值"
            PlotModel1.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Title = "数值",
                TextColor = OxyColors.Black,
                IsZoomEnabled = true,
                IsPanEnabled = true,
                MajorGridlineStyle = LineStyle.Solid
            });

            // ④ 建曲线集合
            SeriesList1 = new ObservableCollection<PlotSeriesViewModel1>();


            // ⑤ 造 300 个 (i, 0) 假点，调用初始化方法搭出四条空曲线
            // ⚠ 注意：这里是 300 个点，而示波器来的是 1000 个点；UpDataView1 会整体替换，所以数量不一致也能工作
            double[] x = new double[300];
            double[] y = new double[300];
            for (UInt16 i = 0; i < 300; i++)
            {
                x[i] = i;
                y[i] = 0;
            }
            InitQuXianView(x, y);
        }

        /// <summary>
        /// 初始化第 1 条曲线（CH1），然后调用 AddNewSeries1 把剩下三条也建出来。
        /// 只在构造函数里调用一次。
        /// </summary>
        public void InitQuXianView(double[] xData, double[] yData)
        {
            //报警曲线
            string JiXianName = "CH1";
            newSeries = new LineSeries
            {
                Title = JiXianName,            // 图例里显示的名字
                MarkerType = MarkerType.None   // 不画数据点标记（只画连线，波形图标准做法）
            };

            for (int i = 0; i < 300; i++)
            {
                newSeries.Points.Add(new DataPoint(xData[i], yData[i]));
            }
            //      ReNameList.Add(JiXianName);
            var seriesVM = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries };
            // ↑ 把 LineSeries 包一层，是为了给它加 IsVisible1（显隐开关）供勾选框绑定
            SeriesList1.Add(seriesVM);         // 加入集合（勾选框列表会自动多一行）
            PlotModel1.Series.Add(newSeries);  // 加入图中（真正显示）
            PlotModel1.InvalidatePlot(true);   // 通知 OxyPlot 重绘
            AddNewSeries1(xData, yData);
        }


        /// <summary>
        /// 初始化 CH2、CH3、CH4 三条曲线。
        /// ⚠ 注意：三条线用的是同一个 yData 加固定偏移（+0.1/+0.2/+0.3），纯粹为了让四条线初始时分开可见，
        ///    真实数据到达后会被 UpDataView1 整体覆盖，这些假值不会残留。
        /// </summary>
        public void AddNewSeries1( double[] xData, double[] yData)
        {
            _seriesCount1++;
            string JiXianName = "Ch2";
            var newSeries = new LineSeries
            {
                Title = "Ch2",
                MarkerType = MarkerType.None
            };

            for (int i = 0; i < xData.Length; i++)
            {
                newSeries.Points.Add(new DataPoint(xData[i], (yData[i])+0.1));
            }
            var seriesVM = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries };
            SeriesList1.Add(seriesVM);
            PlotModel1.Series.Add(newSeries);

            _seriesCount1++;
            
            var newSeries1 = new LineSeries
            {
                Title = "Ch3",
                MarkerType = MarkerType.None
            };

            for (int i = 0; i < xData.Length; i++)
            {
                newSeries1.Points.Add(new DataPoint(xData[i], (yData[i]) + 0.2));
            }
            var seriesVM1 = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries1 };
            SeriesList1.Add(seriesVM1);
            PlotModel1.Series.Add(newSeries1);

            var newSeries2 = new LineSeries
            {
                Title = "Ch4",
                MarkerType = MarkerType.None
            };

            for (int i = 0; i < xData.Length; i++)
            {
                newSeries2.Points.Add(new DataPoint(xData[i], (yData[i]) + 0.3));
            }
            var seriesVM2 = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries2 };
            SeriesList1.Add(seriesVM2);
            PlotModel1.Series.Add(newSeries2);

            // 用 WPF 的 Dispatcher 安排一次界面刷新（构造函数可能在非 UI 线程被调，稳妥起见）
            System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() => PlotModel1.InvalidatePlot(true)));
        }

        /// <summary>
        /// 更新某条曲线的全部数据点（示波器数据到达时被 MainWindow 调用）。
        /// 策略：先删掉旧点，再整批加入新点，最后重绘——避免逐点 Add 造成的频繁刷新卡顿。
        /// </summary>
        /// <param name="xData">横坐标数组（真实时间，秒）</param>
        /// <param name="yData">纵坐标数组（电压值）</param>
        /// <param name="ChSeries">通道序号：0=CH1, 1=CH2, 2=CH3, 3=CH4</param>
        public async Task UpDataView1(Double[] xData, Double[] yData, Byte ChSeries) // 0 CH1;1 CH2;2 CH3;3 CH4
        {
            var ReWriteLineSeries = SeriesList1[ChSeries].Series1;
            // 预先计算需要移除和添加的点数
            int removeCount = Math.Min(1000, ReWriteLineSeries.Points.Count);
            int addCount = xData.Length;

            // 批量移除旧点（从下标0开始删 removeCount 个）
            ReWriteLineSeries.Points.RemoveRange(0, removeCount);

            // 使用List<DataPoint>进行批量添加
            var newPoints = new List<DataPoint>(addCount);
            for (int i = 0; i < addCount; i++)
            {
                newPoints.Add(new DataPoint(xData[i], yData[i]));
            }
            ReWriteLineSeries.Points.AddRange(newPoints);
            await System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() => PlotModel1.InvalidatePlot(true)));
            // ↑ 重绘必须切到 UI 线程（WPF 规矩：界面只能由 UI 线程碰），BeginInvoke 是"拜托 UI 线程帮我执行"
        }

        /// <summary>
        /// 清空所有曲线。⚠ 主程序里没有任何地方调用它，属于预留功能。
        /// </summary>
        public void Clear()
        {
            _seriesCount1 = 0;
            SeriesList1.Clear();
            PlotModel1.Series.Clear();
            PlotModel1.InvalidatePlot(true);
        }
    }
}
