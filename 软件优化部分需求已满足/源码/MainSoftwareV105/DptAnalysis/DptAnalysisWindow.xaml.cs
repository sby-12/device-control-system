using System;
using System.Windows;
using System.Windows.Controls;

namespace MainSoftwareV100
{
    /// <summary>
    /// 器件开关参数提取窗口（2026-10-30 功能接线版）。
    /// 每个页签：通道映射下拉框 + 「提取参数」按钮 → 调 DptAnalysis 数学库 → 填参数框。
    /// 数据来源：主窗口 GetAnalysisFrame（优先深度保存RAW波形，无深存用实时快照）。
    /// </summary>
    public partial class DptAnalysisWindow : Window
    {
        private readonly MainWindow _main;

        public DptAnalysisWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            RefreshDeepStatus();   //窗口打开时同步一次深存储状态
        }

        //========== 深存储状态条 ==========

        //根据 WaveSnapshot 深存槽状态显示/隐藏状态条
        private void RefreshDeepStatus()
        {
            string text = WaveSnapshot.DeepStatusText();
            if (string.IsNullOrEmpty(text))
            {
                DeepStatusBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                DeepStatusText.Text = text;
                DeepStatusBar.Visibility = Visibility.Visible;
            }
        }

        //丢弃深存数据：之后提取退回实时快照
        private void ClearDeep_Click(object sender, RoutedEventArgs e)
        {
            WaveSnapshot.ClearDeep();
            RefreshDeepStatus();
            ShowStatus("已丢弃深存储数据，提取将使用实时波形快照");
        }

        //========== 通用小工具 ==========

        //状态栏提示（顺带刷新深存储状态条：深保存可能在本窗口打开期间完成）
        private void ShowStatus(string msg)
        {
            StatusText.Text = msg;
            RefreshDeepStatus();
        }

        //把算法结果填进参数框；fmt 如 "0.###"（三位小数，末尾零自动去掉）。
        //valid=false 表示算法判定该参数无效（端点未找到等），显示"--"而不是误导性的0。
        private void Set(string boxName, double value, bool valid = true, string fmt = "0.###")
        {
            TextBox box = FindName(boxName) as TextBox;
            if (box != null) box.Text = valid ? value.ToString(fmt) : "--";
        }

        //取某下拉框选中的通道号（0~3），"无"/未选返回 -1。
        //2026-10-31 修复通道错位：有的下拉框首项是"无"（如开关页签"对管Vge"的 [无,CH1,CH2,CH3,CH4]），
        //SelectedIndex 直接当通道号会选 CH1 实际分析 CH2、选 CH4 越界。
        //现在按文本解析 "CH数字"，与列表顺序无关。
        private int ChanOf(ComboBox cb)
        {
            if (cb == null || cb.SelectedItem == null) return -1;
            string s = (cb.SelectedItem as ComboBoxItem)?.Content?.ToString()
                       ?? cb.SelectedItem.ToString();
            if (string.IsNullOrWhiteSpace(s) || s == "无") return -1;
            s = s.Replace("CH", "").Trim();
            int ch;
            return int.TryParse(s, out ch) ? ch - 1 : -1;
        }

        //从主窗口取一帧波形并转成 double 数组；数据不足返回 null
        private double[] FrameData(int ch)
        {
            if (ch < 0 || _main == null) return null;
            WaveSnapshot.ChannelFrame f = _main.GetAnalysisFrame(ch);
            if (f == null || f.Data == null || f.Data.Length < 10) return null;
            double[] d = new double[f.Data.Length];
            for (int i = 0; i < d.Length; i++) d[i] = f.Data[i];
            return d;
        }

        //取采样间隔（秒）。2026-10-31 修复：原来异常时静默兜底1ns，会把时间/能量参数
        //整整错一个数量级还不报警；现在改为返回false并给出提示，由调用方拒绝本次提取。
        private bool TryDtOf(int ch, out double dt)
        {
            dt = 0;
            if (ch < 0 || _main == null) return false;
            WaveSnapshot.ChannelFrame f = _main.GetAnalysisFrame(ch);
            if (f == null || !(f.XIncrement > 0) || double.IsNaN(f.XIncrement))
                return false;
            dt = f.XIncrement;
            return true;
        }

        //========== 1. 开关页签（L/R 共用，按钮 Tag 区分前缀） ==========
        private void SwitchExtract_Click(object sender, RoutedEventArgs e)
        {
            string p = ((Button)sender).Tag as string;   //"L" 或 "R"
            int chVge = ChanOf(FindName(p + "_ChVge") as ComboBox);
            int chVce = ChanOf(FindName(p + "_ChVce") as ComboBox);
            int chIc  = ChanOf(FindName(p + "_ChIc") as ComboBox);
            int chOt  = ChanOf(FindName(p + "_ChOther") as ComboBox);   //对管Vge（串扰），可为空

            double[] vge = FrameData(chVge);
            double[] vce = FrameData(chVce);
            double[] ic  = FrameData(chIc);
            if (vge == null || vce == null || ic == null)
            {
                ShowStatus("通道数据不足：请确认示波器已采集波形，并检查 Vge/Vce/Ic 通道映射");
                return;
            }
            double dt;
            if (!TryDtOf(chVce, out dt))
            { ShowStatus("采样间隔异常：该通道波形缺少有效XIncrement，已拒绝提取（时间/能量参数会整体算错）"); return; }
            double[] other = FrameData(chOt);
            if (other != null && other.Length != vce.Length) other = null;   //对管通道长度不一致就不用

            DptSwitching.Result r = DptSwitching.Analyze(vge, vce, ic, dt, other);
            var inv = r.Invalid;   //算法判无效的参数集合
            Set(p + "_TdOn", r.TdOn, !inv.Contains("TdOn"));     Set(p + "_Tr", r.Tr, !inv.Contains("Tr"));         Set(p + "_Ton", r.Ton, !inv.Contains("Ton"));
            Set(p + "_TdOff", r.TdOff, !inv.Contains("TdOff"));   Set(p + "_Tf", r.Tf, !inv.Contains("Tf"));         Set(p + "_Toff", r.Toff, !inv.Contains("Toff"));
            Set(p + "_Tz", r.Tz);
            Set(p + "_DvDtOn", r.DvDtOn, !inv.Contains("DvDtOn")); Set(p + "_DvDtOff", r.DvDtOff, !inv.Contains("DvDtOff"));
            Set(p + "_DiDtOn", r.DiDtOn, !inv.Contains("DiDtOn")); Set(p + "_DiDtOff", r.DiDtOff, !inv.Contains("DiDtOff"));
            Set(p + "_Eon", r.Eon, !inv.Contains("Eon"));       Set(p + "_Eoff", r.Eoff, !inv.Contains("Eoff"));
            Set(p + "_Vdsm", r.Vdsm);     Set(p + "_DeltaVce", r.DeltaVce);
            Set(p + "_Ls", r.Ls, !inv.Contains("Ls"));         Set(p + "_Pmax", r.Pmax);
            Set(p + "_XtalkMax", r.CrosstalkVmax); Set(p + "_XtalkMin", r.CrosstalkVmin);
            ShowStatus(string.IsNullOrEmpty(r.Message) ? p + "管开关参数提取完成" : r.Message);
        }

        //========== 2. RR 反向恢复 ==========
        private void RecoveryExtract_Click(object sender, RoutedEventArgs e)
        {
            double[] vge = FrameData(ChanOf(RR_ChVge));
            double[] vce = FrameData(ChanOf(RR_ChVce));
            double[] ic  = FrameData(ChanOf(RR_ChIc));
            if (vge == null || vce == null || ic == null)
            {
                ShowStatus("通道数据不足：请检查 RR 页签的 Vge/Vce/Ic 通道映射");
                return;
            }
            double dt;
            if (!TryDtOf(ChanOf(RR_ChVce), out dt))
            { ShowStatus("采样间隔异常：该通道波形缺少有效XIncrement，已拒绝提取"); return; }
            DptRecovery.Result r = DptRecovery.Analyze(vge, vce, ic, dt);
            var inv = r.Invalid;
            Set("RR_Irr", r.Irr);     Set("RR_Trr", r.Trr, !inv.Contains("Trr"));   Set("RR_Vrr", r.Vrr);
            Set("RR_DvDt", r.DvDt, !inv.Contains("DvDt"));   Set("RR_DiDt", r.DiDt, !inv.Contains("DiDt"));
            Set("RR_Err", r.Err, !inv.Contains("Err"));     Set("RR_Qrr", r.Qrr, !inv.Contains("Qrr"));   Set("RR_Pdmax", r.Pdmax, !inv.Contains("Pdmax"));
            ShowStatus(string.IsNullOrEmpty(r.Message) ? "反向恢复参数提取完成" : r.Message);
        }

        //========== 3. 栅极电荷（QG-L/QG-R 共用，按钮 Tag 区分前缀） ==========
        private void GateChargeExtract_Click(object sender, RoutedEventArgs e)
        {
            string p = ((Button)sender).Tag as string;   //"QGL" 或 "QGR"
            double[] vge = FrameData(ChanOf(FindName(p + "_ChVge") as ComboBox));
            double[] ig  = FrameData(ChanOf(FindName(p + "_ChIg") as ComboBox));
            if (vge == null || ig == null)
            {
                ShowStatus("通道数据不足：栅极电荷需要 Vge 与 Ig（恒流源电流）两路波形");
                return;
            }
            double dt;
            if (!TryDtOf(ChanOf(FindName(p + "_ChVge") as ComboBox), out dt))
            { ShowStatus("采样间隔异常：该通道波形缺少有效XIncrement，已拒绝提取"); return; }
            DptGateCharge.Result r = DptGateCharge.Analyze(vge, ig, dt);
            Set(p + "_Qg", r.Qg); Set(p + "_Qgs", r.Qgs); Set(p + "_Qgd", r.Qgd); Set(p + "_Vgp", r.Vgp);
            ShowStatus(string.IsNullOrEmpty(r.Message) ? "栅极电荷提取完成" : r.Message);
        }

        //========== 4. UIS 雪崩 ==========
        private void AvalancheExtract_Click(object sender, RoutedEventArgs e)
        {
            double[] vce = FrameData(ChanOf(UIS_ChVce));
            double[] ic  = FrameData(ChanOf(UIS_ChIc));
            if (vce == null || ic == null)
            {
                ShowStatus("通道数据不足：UIS 需要 Vce 与 Ic 两路波形");
                return;
            }
            double dt;
            if (!TryDtOf(ChanOf(UIS_ChVce), out dt))
            { ShowStatus("采样间隔异常：该通道波形缺少有效XIncrement，已拒绝提取"); return; }
            DptAvalanche.Result r = DptAvalanche.Analyze(vce, ic, dt);
            Set("UIS_Ias", r.Ias); Set("UIS_Eas", r.Eas); Set("UIS_Tav", r.Tav);
            Set("UIS_Vav", r.Vav); Set("UIS_DiDt", r.DiDt);
            ShowStatus(string.IsNullOrEmpty(r.Message) ? "雪崩参数提取完成" : r.Message);
        }

        //========== 5. SC 短路 ==========
        private void ShortCircuitExtract_Click(object sender, RoutedEventArgs e)
        {
            double[] vce = FrameData(ChanOf(SC_ChVce));
            double[] ic  = FrameData(ChanOf(SC_ChIc));
            if (vce == null || ic == null)
            {
                ShowStatus("通道数据不足：SC 需要 Vce 与 Ic 两路波形");
                return;
            }
            double dt;
            if (!TryDtOf(ChanOf(SC_ChVce), out dt))
            { ShowStatus("采样间隔异常：该通道波形缺少有效XIncrement，已拒绝提取"); return; }
            DptShortCircuit.Result r = DptShortCircuit.Analyze(vce, ic, dt);
            Set("SC_Imax", r.Imax); Set("SC_Tsc", r.Tsc); Set("SC_Esc", r.Esc);
            Set("SC_Vpeak", r.Vpeak); Set("SC_Desat", r.DesatTime);
            ShowStatus(string.IsNullOrEmpty(r.Message) ? "短路参数提取完成" : r.Message);
        }
    }
}
