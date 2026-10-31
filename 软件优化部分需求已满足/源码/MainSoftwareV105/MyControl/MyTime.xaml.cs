
using System;
using System.Collections.Generic;
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
using System.Windows.Threading;

namespace MainSoftwareV100.MyControl
{
    /// <summary>
    /// MyTime.xaml 的交互逻辑
    /// </summary>
    public partial class MyTime : UserControl
    {
        public MyTime()
        {
            InitializeComponent();
        }
        DispatcherTimer freshDateTimeTimer = new DispatcherTimer();
        private void UserControl_Loaded_1(object sender, RoutedEventArgs e)
        {
            freshDateTimeTimer.Tick += freshDateTimeTimer_Tick;
            freshDateTimeTimer.Interval = TimeSpan.FromSeconds(1);
            freshDateTimeTimer_Tick(null, null);
            freshDateTimeTimer.Start();
        }

        private void freshDateTimeTimer_Tick(object sender, EventArgs e)
        {
            DateTime dt = DateTime.Now;
            tbTime.Text = dt.ToString("HH:mm:ss");
            tbDate.Text = dt.ToString("yyyy-MM-dd");
            tbWeek.Text = dt.ToString("dddd");
        }


    }
}
