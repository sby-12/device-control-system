using System;
using System.Collections.Generic;
using System.ComponentModel;             // INotifyPropertyChanged 所在命名空间
using System.Linq;
using System.Runtime.CompilerServices;  // [CallerMemberName] 特性
using System.Text;
using System.Threading.Tasks;
using OxyPlot;
using OxyPlot.Series;

namespace MainSoftwareV100
{
    /// <summary>
    /// 单条曲线的"包装类"。
    /// 为什么要包一层？OxyPlot 的 LineSeries 本身没有"是否显示"这个适合数据绑定的属性，
    /// 所以学长包了一层，加上 IsVisible1 属性，让 XAML 左侧的勾选框可以双向绑定它。
    /// 这是 WPF 数据绑定的标准套路（MVVM 模式的最简形态）。
    /// </summary>
    public class PlotSeriesViewModel1 : INotifyPropertyChanged
    {
        private bool _isVisible1 = true;    // 显隐状态，默认显示
        private readonly PlotModel _plotModel1;  // 持有图的引用，是为了切换显隐后立刻重绘


        public LineSeries Series1 { get; set; }  // 被包装的真身：OxyPlot 曲线对象

        public PlotSeriesViewModel1(PlotModel plotModel)
        {
            _plotModel1 = plotModel;
        }
        /// <summary>
        /// 曲线是否显示。勾选框打勾/取消时会由 WPF 自动调用 set 块（双向绑定）。
        /// </summary>
        public bool IsVisible1
        {
            get => _isVisible1;
            set
            {
                _isVisible1 = value;
                Series1.IsVisible= value;     // 同步给 OxyPlot 的曲线
                OnPropertyChanged();          // 通知界面"这个属性变了"（绑定机制要求的仪式）
                _plotModel1.InvalidatePlot(true);   // 重绘，曲线立刻显示/消失
            }
        }

        // 数据绑定的基础设施：属性变化通知事件
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            // ↑ ?. 空安全调用：如果没人监听就不通知（不报错）
            // ↑ [CallerMemberName] 让编译器自动填入调用者的名字（"IsVisible1"），不用手写字符串
        }
    }
}
