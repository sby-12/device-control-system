using OscilloscopeClassLibrary;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;


namespace MainSoftwareV100
{
    public class MainViewModel
    {
        public ObservableCollection<PlotSeriesViewModel1> SeriesList1 { get; set; }
       
        public PlotModel PlotModel1 { get; set; }
       

        public LineSeries newSeries = null;
        
        public int _seriesCount1 = 0;
        private int _seriesCount2 = 0;
        private int _seriesCount3 = 0;
        private int _seriesCount4 = 0;
        private int _seriesCount5 = 0;
        public MainViewModel()
        {
            PlotModel1 = new PlotModel() { Title = "波形曲线" };
            PlotModel1.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Bottom,
                Title = "时间",
                IsZoomEnabled = true,
                IsPanEnabled = true,
                MajorGridlineStyle = LineStyle.Solid,
            });
            PlotModel1.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Title = "CH1/CH3",
                TextColor = OxyColors.Red,
                AxislineColor = OxyColors.Red,
                TicklineColor = OxyColors.Red,
                IsZoomEnabled = true,
                IsPanEnabled = true,
                MajorGridlineStyle = LineStyle.Solid
            });
            PlotModel1.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Right,      // 位置在右边
                Key = "Y2",                          // 标记值
                Title = "CH2/CH4",
                TextColor = OxyColors.Green,
                AxislineColor = OxyColors.Green,
                TicklineColor = OxyColors.Green,
                IsZoomEnabled = true,
                IsPanEnabled = true,
                MajorGridlineStyle = LineStyle.Solid
            });
            SeriesList1 = new ObservableCollection<PlotSeriesViewModel1>();



            double[] x = new double[300];
            double[] y = new double[300];
            for (UInt16 i = 0; i < 300; i++)
            {
                x[i] = i;
                y[i] = 0;
            }
            InitQuXianView(x, y);
        }

        public void InitQuXianView(double[] xData, double[] yData)
        {
            //报警曲线
            string JiXianName = "CH1";
            newSeries = new LineSeries
            {
                Title = JiXianName,
                MarkerType = MarkerType.None
            };

            for (int i = 0; i < 300; i++)
            {
                newSeries.Points.Add(new DataPoint(xData[i], yData[i]));
            }
            //      ReNameList.Add(JiXianName);
            var seriesVM = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries };
            SeriesList1.Add(seriesVM);
            PlotModel1.Series.Add(newSeries);
            PlotModel1.InvalidatePlot(true);
            AddNewSeries1(xData, yData);
           
        }


        public void AddNewSeries1( double[] xData, double[] yData)
        {
            _seriesCount1++;
            string JiXianName = "Ch2";
            var newSeries = new LineSeries { Title = "Ch2", MarkerType = MarkerType.None, YAxisKey = "Y2" };

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

            var newSeries2 = new LineSeries { Title = "Ch4", MarkerType = MarkerType.None, YAxisKey = "Y2" };

            for (int i = 0; i < xData.Length; i++)
            {
                newSeries2.Points.Add(new DataPoint(xData[i], (yData[i]) + 0.3));
            }
            var seriesVM2 = new PlotSeriesViewModel1(PlotModel1) { Series1 = newSeries2 };
            SeriesList1.Add(seriesVM2);
            PlotModel1.Series.Add(newSeries2);

            System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() => PlotModel1.InvalidatePlot(true)));
        }

        public async Task UpDataView1(Double[] xData, Double[] yData, Byte ChSeries) // 0 CH1;1 CH2;2 CH3;3 CH4
        {
            var ReWriteLineSeries = SeriesList1[ChSeries].Series1;
            // 预先计算需要移除和添加的点数
            int removeCount = Math.Min(OscilloscopeClass.WavePointCount, ReWriteLineSeries.Points.Count);
            int addCount = xData.Length;

            // 批量移除点数
            ReWriteLineSeries.Points.RemoveRange(0, removeCount);

            // 使用List<DataPoint>进行批量添加
            var newPoints = new List<DataPoint>(addCount);
            for (int i = 0; i < addCount; i++)
            {
                newPoints.Add(new DataPoint(xData[i], yData[i]));
            }
            ReWriteLineSeries.Points.AddRange(newPoints);
            await System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() => PlotModel1.InvalidatePlot(true)));
        }

        public void Clear()
        {
            _seriesCount1 = 0;
            SeriesList1.Clear();
            PlotModel1.Series.Clear();
            PlotModel1.InvalidatePlot(true);
        }
    }
}
