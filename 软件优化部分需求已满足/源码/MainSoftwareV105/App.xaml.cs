using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace MainSoftwareV100
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            //2026-10-31 全局保险丝：任何按钮/设备库抛出的未处理 UI 线程异常都不再崩软件，
            //统一转成一条错误提示，程序继续运行。单设备时代各库内部吞掉错误所以安全；
            //多设备适配后库会按"出错要响亮"的原则抛异常（NotSupported/超时/断线），需要这层兜底。
            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;   //吞掉异常，软件继续活
                try
                {
                    var w = Current.MainWindow;
                    if (w != null && w.IsVisible)
                        Toast.Show(w, "操作失败：" + args.Exception.Message, true);
                    else
                        MessageBox.Show("操作失败：" + args.Exception.Message);
                }
                catch { /*连提示都弹不出来就算了，至少没崩*/ }
            };
        }
    }
}
