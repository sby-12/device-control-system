using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MainSoftwareV100
{
    /// <summary>
    /// AI助手窗口：独立非模态窗口，关闭主程序前随手关掉即可，绝不影响正常测控。
    /// 布局：上方状态条（AI正在做的事）+ 中间聊天记录 + 下方输入框。
    /// </summary>
    public partial class AiAssistantWindow : Window
    {
        private readonly AiAgent _agent = new AiAgent();
        private TextBlock _streamingBlock = null;

        public AiAssistantWindow(MainWindow main)
        {
            InitializeComponent();

            //接通工具与仪器（保留Init兼容旧用法）
            AiTools.Init(main.Oscilloscope, main.AiPower);
            //动态获取仪器：每次执行工具时实时从主窗口取，
            //先开AI窗口、后主界面才连接仪器也能正常用，不用重开窗口
            AiTools.ScopeProvider = () => main.Oscilloscope;
            AiTools.PowerProvider = () => main.AiPower;
            //波形帧获取：参数提取工具的数据来源（优先深存，退回实时快照）
            AiTools.FrameProvider = (ch) => main.GetAnalysisFrame(ch);
            AiTools.Build();

            //危险操作确认弹窗（在UI线程上问操作员）
            AiTools.ConfirmDangerous = async (desc) =>
            {
                bool allow = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    allow = MessageBox.Show("AI请求执行以下操作：\n\n" + desc + "\n\n是否允许？",
                        "操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                });
                return allow;
            };

            //事件接线
            _agent.OnStatus += s => Dispatcher.Invoke(() => StatusText.Text = s);
            _agent.OnToken += t => Dispatcher.Invoke(() =>
            {
                if (_streamingBlock == null)
                {
                    _streamingBlock = AddBubble("", "#E8EEF7", "#1E2E44", HorizontalAlignment.Left);
                }
                _streamingBlock.Text += t;
                ChatScroll.ScrollToBottom();
            });
            _agent.OnError += msg => Dispatcher.Invoke(() =>
            {
                AddBubble(msg, "#FDE8E8", "#8B1A1A", HorizontalAlignment.Left);
                StatusText.Text = "出错了";
            });

            //欢迎语与自检信息
            string hello = "你好，我是器件检测系统的AI助手。可以问我：\n" +
                           "· 这个波形正不正常（我会去读通道统计）\n" +
                           "· 波形比刚才有没有变化（我会对比最近几帧）\n" +
                           "· 怎么测二极管/开关管（我先查知识库）\n" +
                           "· 设一下触发电平、帮我读个测量值\n" +
                           "· 出了故障帮我分析原因\n" +
                           "危险操作（开电源输出、恢复出厂）会先问你确认。\n" +
                           "对话内容可点下方\"导出\"存成Markdown当实验记录。";
            AddBubble(hello, "#E8EEF7", "#1E2E44", HorizontalAlignment.Left);

            if (!AiConfig.Load())
            {
                //只在文件不存在时才生成模板——如果用户已经填了一半配置（比如只填了密钥），
                //生成模板会把他填好的内容整个覆盖掉，必须避免
                if (!System.IO.File.Exists(AiConfig.FilePath)) AiConfig.CreateTemplate();
                AddBubble("还没配置好API（需要api地址、api密钥、模型名三项）：请检查 exe 旁边的 AI Config.txt，填好后重开窗口即可对话。",
                    "#FFF6E0", "#7A5B00", HorizontalAlignment.Left);
            }
            else
            {
                int docCount = KnowledgeBase.Load();
                StatusText.Text = "就绪（知识库" + docCount + "个文档，工具" + AiTools.Count + "个，记忆" + AiConfig.MemoryTurns + "条）";
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text.Trim();
            if (text.Length == 0) return;
            //上一条还在处理时明确提示，而不是静默吞掉这条消息
            if (_agent.IsBusy)
            {
                StatusText.Text = "上一条还在处理，请等它答完再发...";
                return;
            }
            InputBox.Clear();
            AddBubble(text, "#FF2D5078", "White", HorizontalAlignment.Right);
            StatusText.Text = "正在思考...";
            _streamingBlock = null;
            await _agent.SendAsync(text);
            _streamingBlock = null;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _agent.ClearMemory();
            ChatPanel.Children.Clear();
            AddBubble("对话记忆已清空。", "#EEEEEE", "#555555", HorizontalAlignment.Left);
            StatusText.Text = "就绪";
        }

        /// <summary>
        /// 导出对话记录为Markdown（右气泡=操作员，左气泡=AI助手/系统提示）。
        /// 实验场景下"AI帮我调的参数+读到的波形结论"本身就是实验记录，可直接归档。
        /// </summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# AI助手对话记录");
            sb.AppendLine();
            sb.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            foreach (UIElement el in ChatPanel.Children)
            {
                Border b = el as Border;
                TextBlock tb = (b != null) ? b.Child as TextBlock : null;
                if (tb == null || string.IsNullOrWhiteSpace(tb.Text)) continue;
                string who = (b.HorizontalAlignment == HorizontalAlignment.Right) ? "操作员" : "AI助手";
                sb.AppendLine("## " + who);
                sb.AppendLine();
                sb.AppendLine(tb.Text);
                sb.AppendLine();
            }

            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出对话记录",
                Filter = "Markdown文件 (*.md)|*.md|文本文件 (*.txt)|*.txt",
                FileName = "AI对话记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                StatusText.Text = "已导出对话记录：" + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusText.Text = "导出失败：" + ex.Message;
            }
        }

        private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) Send_Click(sender, e);
        }

        /// <summary>加一条聊天气泡，返回TextBlock方便流式追加。</summary>
        private TextBlock AddBubble(string text, string bg, string fg, HorizontalAlignment align)
        {
            Border bubble = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(4, 3, 4, 3),
                HorizontalAlignment = align,
                MaxWidth = 420
            };
            TextBlock tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)),
                FontSize = 13.5,
                TextWrapping = TextWrapping.Wrap
            };
            bubble.Child = tb;
            ChatPanel.Children.Add(bubble);
            ChatScroll.ScrollToBottom();
            return tb;
        }
    }
}
