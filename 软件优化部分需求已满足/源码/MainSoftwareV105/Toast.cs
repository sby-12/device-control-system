using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MainSoftwareV100
{
    /// <summary>
    /// 轻量浮动提示条（Toast）：在指定窗口顶部居中弹一条消息，
    /// 淡入→停留→淡出后自动关闭。不抢焦点、不阻塞任何操作。
    /// 用法：Toast.Show(this, "指令设置成功");  失败提示：Toast.Show(this, "设置失败", true);
    /// 2026-10-30 新增：解决"点了设置按钮不知道有没有发出去"的问题。
    /// 提示级别统一为"指令已成功发送"（仪器库不改，不做应答级确认）。
    /// 2026-10-30 改进（方案一·合并刷新）：1.5秒内连续触发不叠窗，
    /// 后来的提示直接改文字、计时清零，始终只有一条。
    /// </summary>
    public static class Toast
    {
        //当前存活的那条提示（最多同时存在一条）
        private static Window _current = null;
        private static TextBlock _currentText = null;
        private static Border _currentBorder = null;
        private static Storyboard _currentSb = null;

        /// <summary>
        /// 显示一条提示。isError=true 用淡红底色表示失败；showSeconds 控制停留时长。
        /// 上一条还没消失时：不新建窗口，改文字+换颜色+计时清零（合并刷新）。
        /// </summary>
        public static void Show(Window owner, string message, bool isError = false, double showSeconds = 1.5)
        {
            if (owner == null || string.IsNullOrEmpty(message)) return;
            //跨线程调用兜底：一律切回UI线程
            if (!owner.Dispatcher.CheckAccess())
            {
                owner.Dispatcher.Invoke(() => Show(owner, message, isError, showSeconds));
                return;
            }

            //===== 已有存活提示：合并刷新（改文字、换底色、动画重来）=====
            if (_current != null)
            {
                if (_currentText != null) _currentText.Text = message;
                if (_currentBorder != null)
                {
                    _currentBorder.Background = new SolidColorBrush(isError ? Color.FromRgb(0xFD, 0xE8, 0xE8) : Color.FromRgb(0xE8, 0xF4, 0xFD));
                    _currentBorder.BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(0xE0, 0x99, 0x99) : Color.FromRgb(0x99, 0xC8, 0xE0));
                }
                _currentSb.Stop();      //动画回到起点
                _currentSb.Begin();     //重新淡入→停留→淡出
                return;
            }

            //===== 没有存活提示：新建一条 =====
            TextBlock text = new TextBlock
            {
                Text = message,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x44)),
                Padding = new Thickness(18, 8, 18, 8)
            };
            Border border = new Border
            {
                Background = new SolidColorBrush(isError ? Color.FromRgb(0xFD, 0xE8, 0xE8) : Color.FromRgb(0xE8, 0xF4, 0xFD)),
                BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(0xE0, 0x99, 0x99) : Color.FromRgb(0x99, 0xC8, 0xE0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = text,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Gray, Opacity = 0.3, BlurRadius = 8, ShadowDepth = 2
                }
            };

            //无框小窗：关键三属性——透明背景、不抢焦点、置顶
            Window toast = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,        //不抢焦点：操作员可以继续点别的按钮
                Topmost = true,               //浮在主窗口之上
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = border
            };
            //挂到主窗口：主窗口最小化时提示一起隐藏。
            //注意：只能给"已经显示过"的窗口当 Owner，否则抛 InvalidOperationException。
            //BAML 加载阶段（主窗口还没 Show）触发的提示会走到这里，此时跳过 Owner 设置。
            if (owner != null && owner.IsVisible) toast.Owner = owner;

            //淡出结束时关闭窗口，并清掉静态引用（只有引用还是它自己时才清，防止误清新提示）
            Storyboard sb = new Storyboard();
            toast.Closed += (s, e) =>
            {
                if (_current == toast) { _current = null; _currentText = null; _currentBorder = null; _currentSb = null; }
            };

            toast.Loaded += (s, e) =>
            {
                //定位到主窗口顶部往下60px、水平居中（屏幕坐标）
                Point pt = owner.PointToScreen(new Point((owner.ActualWidth - toast.ActualWidth) / 2, 60));
                toast.Left = pt.X;
                toast.Top = pt.Y;

                //淡入（0.2s）→ 停留 showSeconds → 淡出（0.2s）→ 关闭
                Duration fadeDur = new Duration(TimeSpan.FromSeconds(0.2));
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, fadeDur);
                DoubleAnimation fadeOut = new DoubleAnimation(1, 0, fadeDur)
                {
                    BeginTime = TimeSpan.FromSeconds(showSeconds)
                };
                sb.Children.Add(fadeIn);
                sb.Children.Add(fadeOut);
                Storyboard.SetTarget(fadeIn, toast);
                Storyboard.SetTarget(fadeOut, toast);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Window.OpacityProperty));
                Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Window.OpacityProperty));
                sb.Completed += (s2, e2) => toast.Close();
                sb.Begin();
            };

            //登记为"当前提示"，再显示
            _current = toast;
            _currentText = text;
            _currentBorder = border;
            _currentSb = sb;
            toast.Show();
        }
    }
}
