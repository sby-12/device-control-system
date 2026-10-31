using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using InstrumentInterfaceLibrary;
using System.Threading;
using ValueSourceN2410ClassLibrary;    //2026-10-31 修复24：挂 AfterConnectFailed 需要认 N2410Class/N36200Class
using OscilloscopeClassLibrary;        //2026-10-31 修复24：同上，认 RIGOL 的 OscilloscopeClass
using SingalClassLibrary;              //2026-10-31 修复24：同上，认 SignalClass

namespace MainSoftwareV100
{
    /// <summary>
    /// ConnectInit.xaml 的交互逻辑。
    ///
    /// 2026-10-30 改造（方案A·接口隔离）：
    /// 原来"主窗口 new 好四台设备 → 这里只负责填IP点连接"，
    /// 现在"这里选机型 + 填IP + 点连接 → 工厂 new 出对应适配类 → 交给主窗口接线"。
    /// 本窗口不再持有任何具体设备类型，只认接口。
    ///
    /// 2026-10-31 修复24：三个异步协议库（N2410/N36200电源、RIGOL示波器、原信号源）的
    /// ConnectAsync 是后台重试（最多3次、跨约10秒），失败不抛异常——try/catch 抓不到，
    /// 槽位会留死设备（主界面按钮点击报 NullReferenceException）。
    /// 现在挂 AfterConnectFailed：三次全失败 → 回滚槽位 + 按旧存档恢复原设备。
    /// （ITECH电源、LeCroy示波器、SDG6000X信号源走同步连接，失败会被上面的 catch 正常接住。）
    /// </summary>
    public partial class ConnectInit : Window
    {
        /// <summary>主窗口引用：连接成功后把设备交给它接线（波形回调、状态显示等）。</summary>
        public MainWindow OwnerMain = null;

        public ConnectInit()
        {
            InitializeComponent();

            //下拉框填机型列表（顺序与 DeviceFactory 里定义的一致）
            foreach (string m in DeviceFactory.PowerModels)  PowerAModel.Items.Add(m);
            foreach (string m in DeviceFactory.PowerModels)  PowerBModel.Items.Add(m);
            foreach (string m in DeviceFactory.ScopeModels)  ScopeModel.Items.Add(m);
            foreach (string m in DeviceFactory.SignalModels) SignalModel.Items.Add(m);
            PowerAModel.SelectedIndex = 0;
            PowerBModel.SelectedIndex = 1;   //默认 A=N2410、B=N36200，与老版本行为一致
            ScopeModel.SelectedIndex = 0;
            SignalModel.SelectedIndex = 0;

            LoadIpSettings();   // 从 IP Storage.txt 恢复上次保存的 机型+IP+端口
        }

        /// <summary>
        /// 启动时读档：把四台仪器上次保存的 机型+IP+端口 填回界面。
        /// 新键（电流源A/B）读不到就回退旧键（N2410/N36200），老用户不用重填。
        /// 机型段为空（旧档只有IP端口）时补下拉框第一个机型。
        /// </summary>
        private void LoadIpSettings()
        {
            //电流源A：新键 → 旧键 N2410
            if (!FillOne(PowerAModel, PowerAAddress, PowerAPort, IpStorage.KEY_POWER_A))
                FillOne(PowerAModel, PowerAAddress, PowerAPort, IpStorage.KEY_N2410, DeviceFactory.POWER_N2410);

            //电流源B：新键 → 旧键 N36200
            if (!FillOne(PowerBModel, PowerBAddress, PowerBPort, IpStorage.KEY_POWER_B))
                FillOne(PowerBModel, PowerBAddress, PowerBPort, IpStorage.KEY_N36200, DeviceFactory.POWER_N36200);

            FillOne(ScopeModel, ScopeAddress, ScopePort, IpStorage.KEY_SCOPE);
            FillOne(SignalModel, SignalAddress, SignalPort, IpStorage.KEY_SIGNAL);
        }

        /// <summary>把存档里的一台仪器填回界面。返回 false 表示没存过。</summary>
        private bool FillOne(ComboBox modelBox, TextBox ipBox, TextBox portBox, string key, string defaultModel = null)
        {
            if (!IpStorage.TryGet(key, out string model, out string ip, out int port)) return false;
            if (string.IsNullOrEmpty(model)) model = defaultModel;
            if (!string.IsNullOrEmpty(model))
            {
                int idx = modelBox.Items.IndexOf(model);
                if (idx >= 0) modelBox.SelectedIndex = idx;   //存档里的机型还在列表中才选，否则保持默认
            }
            ipBox.Text = ip;
            portBox.Text = port.ToString();
            return true;
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Hide();
        }

        //===== 四个连接按钮（逻辑同构：校验 → 工厂创建 → 先挂回调 → 再开连接 → 存档）=====
        //注意：必须先 Attach 再 OpenTranslate。库的初始化代码会调用 reciveState 回调报"初始化成功"，
        //回调没挂好就初始化会空引用；且示波器接收线程一通电就可能有数据进来，回调要先就位。

        //电流源A（Modbus 站号约定：A=1）
        private void PowerA_Connect(object sender, RoutedEventArgs e)
        {
            if (!TryParseIpPort(PowerAAddress, PowerAPort, out string ip, out int port)) return;
            string model = PowerAModel.SelectedItem as string;
            //2026-10-31 连接失败自动恢复原设备：Attach 会关掉旧设备连接，先留底旧存档
            IpStorage.TryGet(IpStorage.KEY_POWER_A, out string oldModel, out string oldIp, out int oldPort);
            try
            {
                IPowerSource dev = DeviceFactory.CreatePower(model, 1);
                //2026-10-31 修复24：异步重试全失败时回滚槽位（槽位还挂着这台设备才清，防误清新连接）
                if (dev is N2410Class t10) t10.translateClass.AfterConnectFailed = () => PowerAConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                else if (dev is N36200Class t36) t36.translateClass.AfterConnectFailed = () => PowerAConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                OwnerMain?.AttachPowerA(dev, model);
                dev.OpenTranslate(ip, port);
                IpStorage.Save(IpStorage.KEY_POWER_A, model, ip, port);
            }
            catch (Exception ex)
            {
                OwnerMain?.AttachPowerA(null, "");   //2026-10-31 连接失败回滚槽位：否则槽位非空但根本没连上，按钮会误报"指令设置成功"
                string msg = "电流源A连接失败：" + ex.Message;
                if (TryRestorePowerA(oldModel, oldIp, oldPort, model, newIp: ip))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != model)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            }
        }

        //2026-10-31 修复24：电流源A异步重试（后台线程）全失败的收尾，整体切回 UI 线程执行
        private void PowerAConnectFailed(IPowerSource dev, string newModel, string oldModel, string oldIp, int oldPort, string newIp)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(OwnerMain?.PowerA, dev)) return;   //槽位已换新设备/已回滚，别误清
                OwnerMain.AttachPowerA(null, "");
                string msg = "电流源A连接失败（已重试3次，请检查设备电源和网线）。";
                if (TryRestorePowerA(oldModel, oldIp, oldPort, newModel, newIp))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != newModel)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            });
        }

        //2026-10-31 连接失败后按旧存档把原设备接回来（best effort）。返回 true=恢复成功。
        private bool TryRestorePowerA(string oldModel, string oldIp, int oldPort, string newModel, string newIp)
        {
            if (string.IsNullOrEmpty(oldModel) || (oldModel == newModel && oldIp == newIp)) return false;
            try
            {
                IPowerSource dev = DeviceFactory.CreatePower(oldModel, 1);
                OwnerMain?.AttachPowerA(dev, oldModel);
                dev.OpenTranslate(oldIp, oldPort);
                return true;
            }
            catch
            {
                OwnerMain?.AttachPowerA(null, "");   //恢复也失败：回到未连接，别留假连接
                return false;
            }
        }

        //电流源B（Modbus 站号约定：B=2）
        private void PowerB_Connect(object sender, RoutedEventArgs e)
        {
            if (!TryParseIpPort(PowerBAddress, PowerBPort, out string ip, out int port)) return;
            string model = PowerBModel.SelectedItem as string;
            IpStorage.TryGet(IpStorage.KEY_POWER_B, out string oldModel, out string oldIp, out int oldPort);
            try
            {
                IPowerSource dev = DeviceFactory.CreatePower(model, 2);
                //2026-10-31 修复24：异步重试全失败时回滚槽位
                if (dev is N2410Class t10) t10.translateClass.AfterConnectFailed = () => PowerBConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                else if (dev is N36200Class t36) t36.translateClass.AfterConnectFailed = () => PowerBConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                OwnerMain?.AttachPowerB(dev, model);
                dev.OpenTranslate(ip, port);
                IpStorage.Save(IpStorage.KEY_POWER_B, model, ip, port);
            }
            catch (Exception ex)
            {
                OwnerMain?.AttachPowerB(null, "");   //2026-10-31 连接失败回滚槽位
                string msg = "电流源B连接失败：" + ex.Message;
                if (TryRestorePowerB(oldModel, oldIp, oldPort, model, ip))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != model)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            }
        }

        //2026-10-31 修复24：电流源B异步重试（后台线程）全失败的收尾，整体切回 UI 线程执行
        private void PowerBConnectFailed(IPowerSource dev, string newModel, string oldModel, string oldIp, int oldPort, string newIp)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(OwnerMain?.PowerB, dev)) return;   //槽位已换新设备/已回滚，别误清
                OwnerMain.AttachPowerB(null, "");
                string msg = "电流源B连接失败（已重试3次，请检查设备电源和网线）。";
                if (TryRestorePowerB(oldModel, oldIp, oldPort, newModel, newIp))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != newModel)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            });
        }

        private bool TryRestorePowerB(string oldModel, string oldIp, int oldPort, string newModel, string newIp)
        {
            if (string.IsNullOrEmpty(oldModel) || (oldModel == newModel && oldIp == newIp)) return false;
            try
            {
                IPowerSource dev = DeviceFactory.CreatePower(oldModel, 2);
                OwnerMain?.AttachPowerB(dev, oldModel);
                dev.OpenTranslate(oldIp, oldPort);
                return true;
            }
            catch
            {
                OwnerMain?.AttachPowerB(null, "");
                return false;
            }
        }

        private void Scope_Connect(object sender, RoutedEventArgs e)
        {
            if (!TryParseIpPort(ScopeAddress, ScopePort, out string ip, out int port)) return;
            string model = ScopeModel.SelectedItem as string;
            IpStorage.TryGet(IpStorage.KEY_SCOPE, out string oldModel, out string oldIp, out int oldPort);
            try
            {
                IScope dev = DeviceFactory.CreateScope(model);
                //2026-10-31 修复24：异步重试全失败时回滚槽位（RIGOL 是异步重试库；LeCroy 同步连接走 catch）
                if (dev is OscilloscopeClass rigol) rigol.translateClass.AfterConnectFailed = () => ScopeConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                OwnerMain?.AttachScope(dev, model);
                dev.OpenTranslate(ip, port);
                IpStorage.Save(IpStorage.KEY_SCOPE, model, ip, port);
            }
            catch (Exception ex)
            {
                OwnerMain?.AttachScope(null, "");   //2026-10-31 连接失败回滚槽位
                string msg = "示波器连接失败：" + ex.Message;
                if (TryRestoreScope(oldModel, oldIp, oldPort, model, ip))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != model)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            }
        }

        //2026-10-31 修复24：示波器异步重试（后台线程）全失败的收尾，整体切回 UI 线程执行
        private void ScopeConnectFailed(IScope dev, string newModel, string oldModel, string oldIp, int oldPort, string newIp)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(OwnerMain?.Oscilloscope, dev)) return;   //槽位已换新设备/已回滚，别误清
                OwnerMain.AttachScope(null, "");
                string msg = "示波器连接失败（已重试3次，请检查设备电源和网线）。";
                if (TryRestoreScope(oldModel, oldIp, oldPort, newModel, newIp))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != newModel)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            });
        }

        private bool TryRestoreScope(string oldModel, string oldIp, int oldPort, string newModel, string newIp)
        {
            if (string.IsNullOrEmpty(oldModel) || (oldModel == newModel && oldIp == newIp)) return false;
            try
            {
                IScope dev = DeviceFactory.CreateScope(oldModel);
                OwnerMain?.AttachScope(dev, oldModel);
                dev.OpenTranslate(oldIp, oldPort);
                return true;
            }
            catch
            {
                OwnerMain?.AttachScope(null, "");
                return false;
            }
        }

        private void Signal_Connect(object sender, RoutedEventArgs e)
        {
            if (!TryParseIpPort(SignalAddress, SignalPort, out string ip, out int port)) return;
            string model = SignalModel.SelectedItem as string;
            IpStorage.TryGet(IpStorage.KEY_SIGNAL, out string oldModel, out string oldIp, out int oldPort);
            try
            {
                ISignalSource dev = DeviceFactory.CreateSignal(model);
                //2026-10-31 修复24：异步重试全失败时回滚槽位（原信号源是异步重试库；SDG6000X 同步连接走 catch）
                if (dev is SignalClass old) old.translateClass.AfterConnectFailed = () => SignalConnectFailed(dev, model, oldModel, oldIp, oldPort, ip);
                OwnerMain?.AttachSignal(dev, model);
                dev.OpenTranslate(ip, port);
                IpStorage.Save(IpStorage.KEY_SIGNAL, model, ip, port);
            }
            catch (Exception ex)
            {
                OwnerMain?.AttachSignal(null, "");   //2026-10-31 连接失败回滚槽位
                string msg = "信号源连接失败：" + ex.Message;
                if (TryRestoreSignal(oldModel, oldIp, oldPort, model, ip))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != model)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            }
        }

        //2026-10-31 修复24：信号源异步重试（后台线程）全失败的收尾，整体切回 UI 线程执行
        private void SignalConnectFailed(ISignalSource dev, string newModel, string oldModel, string oldIp, int oldPort, string newIp)
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(OwnerMain?.SignalDev, dev)) return;   //槽位已换新设备/已回滚，别误清
                OwnerMain.AttachSignal(null, "");
                string msg = "信号源连接失败（已重试3次，请检查设备电源和网线）。";
                if (TryRestoreSignal(oldModel, oldIp, oldPort, newModel, newIp))
                    msg += "\n\n已自动恢复原连接（" + oldModel + " " + oldIp + "）。";
                else if (!string.IsNullOrEmpty(oldModel) && oldModel != newModel)
                    msg += "\n\n注意：原有设备的连接已断开，且自动恢复失败，请重新连接。";
                MessageBox.Show(this, msg);
            });
        }

        private bool TryRestoreSignal(string oldModel, string oldIp, int oldPort, string newModel, string newIp)
        {
            if (string.IsNullOrEmpty(oldModel) || (oldModel == newModel && oldIp == newIp)) return false;
            try
            {
                ISignalSource dev = DeviceFactory.CreateSignal(oldModel);
                OwnerMain?.AttachSignal(dev, oldModel);
                dev.OpenTranslate(oldIp, oldPort);
                return true;
            }
            catch
            {
                OwnerMain?.AttachSignal(null, "");
                return false;
            }
        }

        /// <summary>校验 IP+端口 输入。不合格弹提示并返回 false。</summary>
        private bool TryParseIpPort(TextBox ipBox, TextBox portBox, out string ip, out int port)
        {
            ip = null;
            port = 0;
            if (!IPAddress.TryParse(ipBox.Text, out _))
            {
                MessageBox.Show("IP地址格式不对，请检查。");
                return false;
            }
            if (!int.TryParse(portBox.Text, out port))
            {
                MessageBox.Show("端口格式不对，请检查。");
                return false;
            }
            ip = ipBox.Text;
            return true;
        }
    }
}
