using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using OxyPlot;
using OxyPlot.Series;

namespace MainSoftwareV100
{
    public class PlotSeriesViewModel1 : INotifyPropertyChanged
    {
        private bool _isVisible1 = true;
        private readonly PlotModel _plotModel1;


        public LineSeries Series1 { get; set; }

        public PlotSeriesViewModel1(PlotModel plotModel)
        {
            _plotModel1 = plotModel;
        }
        public bool IsVisible1
        {
            get => _isVisible1;
            set
            {
                _isVisible1 = value;
                Series1.IsVisible= value;
                OnPropertyChanged();
                _plotModel1.InvalidatePlot(true);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
