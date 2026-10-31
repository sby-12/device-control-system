using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MainSoftwareV100.MyControl
{
    /// <summary>
    /// Led.xaml 的交互逻辑
    /// </summary>
    public partial class Led : UserControl
    {
        /// <summary>
        /// 获取或设置Led颜色
        /// </summary>
        public Color LedColor
        {
            get
            {
                RadialGradientBrush brush = (RadialGradientBrush)mLed.Fill;
                return brush.GradientStops[1].Color;
            }
            set
            {
                RadialGradientBrush brush = (RadialGradientBrush)mLed.Fill;
                brush.GradientStops[1].Color = value;
            }
        }
        public Led()
        {
            InitializeComponent();
        }
    }
}
