using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;                       // IPAddress：校验/处理 IP 地址
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ValueSourceN2410ClassLibrary;     // 电压源类库（N2410 + N36200）
using OscilloscopeClassLibrary;         // 示波器类库
using SingalClassLibrary;               // 信号源类库（注意学长的库名拼写是 Singal 不是 Signal）
using System.Threading;                 // Thread.Sleep 所在

namespace MainSoftwareV100
{
    /// <summary>
    /// 连接设置弹窗（点主界面右上角"插头"图标弹出）。
    /// 它的角色很单纯：收集四台仪器的 IP+端口 → 调用各类的 OpenTranslate 建立 TCP 连接。
    /// 仪器对象本身不在这个窗口里创建（主窗口构造时建好了），
    /// 这里通过公开字段 m_xxxClass 接收主窗口传进来的引用（MainWindow 构造函数第 67-70 行赋值）。
    /// ⚠ 潜在问题：没有"断开"按钮，连接一旦建立就只能关软件；IP 填错也没有错误提示（else 分支全空）。
    /// </summary>
    public partial class ConnectInit : Window
    {
        public N2410Class m_n2410Class = null;          // N2410 电压源（仪器地址1）
        public N36200Class m_n36200Class = null;        // N36200 电压源（仪器地址2）
        public OscilloscopeClass m_oscilloscopeClass = null;  // 示波器
        public SignalClass m_singalClass = null;        // 信号源
        public ConnectInit()
        {
            InitializeComponent();
        }

        // 右上角关闭：只是隐藏，不真正关闭（下次点插头还能用）
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Hide();
        }

        //N2410 连接按钮
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string IpAddress = null;
            int Port = 0;
            if(IPAddress.TryParse(N2410Address.Text, out _))
            // ↑ TryParse：尝试把输入文本解析成 IP 格式，成功返回 true（out _ 丢弃解析结果，只要"格式合法"这个判断）
            {
                IpAddress = N2410Address.Text;
                if (int.TryParse(N3413Port.Text, out Port))
                {
                    m_n2410Class.OpenTranslate(IpAddress, Port);
                    // ↑ 真正建立 TCP 连接 + 启动 200ms 定时查询（见 N2410Class.OpenTranslate）
                }
                else
                {
                    // ⚠ 端口不是数字：没有任何提示，静默失败
                }
            }
            else
            {
                // ⚠ IP 格式错误：同样没有任何提示
            }
            
        }

        //N36200 连接按钮
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            string IpAddress = null;
            int Port = 0;
            if (IPAddress.TryParse(N36210Address.Text, out _))
            {
                IpAddress = N36210Address.Text;
                if (int.TryParse(N3620Port.Text, out Port))
                {
                    m_n36200Class.OpenTranslate(IpAddress, Port);
                    m_n36200Class.SetChannelMode();      // 设为普通模式
                    m_n36200Class.SetChannelCvOrCc();    // 设为 CV 优先
                    // ⚠ 潜在问题：刚点连接就立刻连发两条设置指令，此时 TCP 可能还没握上手
                    //   （ConnectAsync 是后台异步进行的），指令可能丢失——靠侥幸或重试工作
                }
                else
                {
                }
            }
            else
            {
            }
        }

        //示波器连接按钮
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            string IpAddress = null;
            int Port = 0;
            if (IPAddress.TryParse(ShiBoAddress.Text, out _))
            {
                IpAddress = ShiBoAddress.Text;
                if (int.TryParse(ShiBoPort.Text, out Port))
                {
                    m_oscilloscopeClass.OpenTranslate(IpAddress, Port);
                    // ↑ 内部：连 TCP + 立刻发一长串初始化 SCPI 指令（InitOscillPrame），同样存在"还没连上就发指令"的竞态
                    Thread.Sleep(200);
                    m_oscilloscopeClass.FindChannel();   // 问一次当前波形源通道（":WAVeform:SOURce?"）
                    // ⚠ 潜在问题：Thread.Sleep 会卡 UI 线程 200ms，界面短暂假死
                }
                else
                {
                }
            }
            else
            {
            }
        }

        //信号源连接按钮
        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            string IpAddress = null;
            int Port = 0;
            if (IPAddress.TryParse(SourceAddress.Text, out _))
            {
                IpAddress = SourceAddress.Text;
                if (int.TryParse(SourcePort.Text, out Port))
                {
                    m_singalClass.OpenTranslate(IpAddress, Port);
                    Thread.Sleep(200);
                    m_singalClass.Init();   // 信号源初始化：ARB 模式 + 脉冲串设置（见 SignalClass.Init）
                }
                else
                {
                }
            }
            else
            {
            }
        }
    }
}
