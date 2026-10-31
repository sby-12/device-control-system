using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MainSoftwareV100.MyControl
{
    /// <summary>
    /// 具有调节按钮的数值框
    /// </summary>
    [TemplatePart(Name = "BTNUP", Type = typeof(ButtonBase))]
    [TemplatePart(Name = "BTNDOWN", Type = typeof(ButtonBase))]
    [TemplatePart(Name = "TEXT", Type = typeof(NumberBox))]
    public class NumericUpDown : System.Windows.Controls.Primitives.RangeBase
    {

        static NumericUpDown()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericUpDown), new FrameworkPropertyMetadata(typeof(NumericUpDown)));
        }
        public NumericUpDown()
        {

        }
        private RepeatButton btnup, btndown;
        private NumberBox textbox;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (this.textbox != null)
            {
                this.textbox.ValueChanged -= Textbox_ValueChanged;
                this.textbox.LostFocus -= Textbox_LostFocus;
            }
            if (this.btnup != null)
            {
                this.btnup.Click -= Btnup_Click;
            }
            if (this.btndown != null)
            {
                this.btndown.Click -= Btndown_Click;
            }

            this.btnup = (RepeatButton)this.GetTemplateChild("BTNUP");
            this.btndown = (RepeatButton)this.GetTemplateChild("BTNDOWN");
            this.textbox = (NumberBox)this.GetTemplateChild("TEXT");
            
            if (this.textbox != null)
            {
                this.textbox.Value = this.Value;
                CheckAllowIncrementOrDecrement();
                this.textbox.ValueChanged += Textbox_ValueChanged;
                this.textbox.LostFocus += Textbox_LostFocus;
            }
            if (this.btnup != null)
            {
                this.btnup.Click += Btnup_Click;
            }
            if (this.btndown != null)
            {
                this.btndown.Click += Btndown_Click;
            }
        }

        #region 实现

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            if (e.Delta > 0)
            {
                this.OnIncrement();
            }
            else if (e.Delta < 0)
            {
                this.OnDecrement();
            }
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Up)
            {
                UpdateAndCheckValue(this.Value + this.SmallChange);
            }
            else if (e.Key == Key.Down)
            {
                UpdateAndCheckValue(this.Value - this.SmallChange);
            }
        }

        private void Textbox_LostFocus(object sender, RoutedEventArgs e)
        {//数值框失去焦点时将当前值写道数值框
            if (this.textbox.Value != this.Value)
            {
                this.textbox.Value = this.Value;
            }
        }
        private void Textbox_ValueChanged(object sender, EventArgs e)
        {//数值框的值变化时使用数值框的值替换当前值
            if (this.textbox.Value >= this.Minimum && this.textbox.Value <= this.Maximum)
            {
                this.Value = this.textbox.Value;
            }
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            this.textbox.Focus();
        }

        private void Btndown_Click(object sender, RoutedEventArgs e)
        {
            this.OnDecrement();
        }

        private void Btnup_Click(object sender, RoutedEventArgs e)
        {
            this.OnIncrement();
        }
        /// <summary>
        /// 检查是否允许递增/递减
        /// </summary>
        protected void CheckAllowIncrementOrDecrement()
        {
            CanIncrement = (Value < Maximum);
            CanDecrement = (Value > Minimum);
        }

        protected virtual void OnIncrement()
        {
            var value = this.Value + this.LargeChange;
            this.UpdateAndCheckValue(value);
        }

        protected virtual void OnDecrement()
        {
            var value = this.Value - this.LargeChange;
            this.UpdateAndCheckValue(value);
        }

        /// <summary>
        /// 更新并且检查当前值，保证值不会超出<see cref="Minimum"/>和<see cref="Maximum"/>限定的范围。
        /// </summary>
        /// <param name="value"></param>
        protected virtual void UpdateAndCheckValue(double value)
        {
            if (value > this.Maximum)
            {
                this.Value = Maximum;
            }
            else if (value < this.Minimum)
            {
                this.Value = Minimum;
            }
            else
            {
                this.Value = value;
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ValueProperty)
            {
                CheckAllowIncrementOrDecrement();               
                if (this.textbox != null)
                {
                    this.textbox.Value = this.Value;                              
                }
            }
        }
        #endregion

        #region 属性

        /// <summary>
        /// 数字的格式化字符串,默认最多保留小数点后6位。
        /// </summary>
        public string FormatString
        {
            get { return (string)GetValue(FormatStringProperty); }
            set { SetValue(FormatStringProperty, value); }
        }

        public static readonly DependencyProperty FormatStringProperty =
            DependencyProperty.Register("FormatString", typeof(string), typeof(NumericUpDown), new PropertyMetadata("0.######"));

        /// <summary>
        /// 文字对齐方式
        /// </summary>
        public TextAlignment TextAlignment
        {
            get { return (TextAlignment)GetValue(TextAlignmentProperty); }
            set { SetValue(TextAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TextAlignmentProperty =
            DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(NumericUpDown), new PropertyMetadata(TextAlignment.Left));


        /// <summary>
        /// 按钮重复触发Click事件的间隔时间，单位为毫秒，默认200毫秒。
        /// </summary>
        public int ButtonRepeatInterval
        {
            get { return (int)GetValue(ButtonRepeatIntervalProperty); }
            set { SetValue(ButtonRepeatIntervalProperty, value); }
        }

        public static readonly DependencyProperty ButtonRepeatIntervalProperty =
            DependencyProperty.Register("ButtonRepeatInterval", typeof(int), typeof(NumericUpDown), new PropertyMetadata(200));


        /// <summary>
        /// 允许递增
        /// </summary>
        public bool CanIncrement
        {
            get { return (bool)GetValue(CanIncrementProperty); }
            set { SetValue(CanIncrementProperty, value); }
        }

        protected internal static readonly DependencyProperty CanIncrementProperty =
            DependencyProperty.Register("CanIncrement", typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));


        /// <summary>
        /// 允许递减
        /// </summary>
        public bool CanDecrement
        {
            get { return (bool)GetValue(CanDecrementProperty); }
            set { SetValue(CanDecrementProperty, value); }
        }

        protected internal static readonly DependencyProperty CanDecrementProperty =
            DependencyProperty.Register("CanDecrement", typeof(bool), typeof(NumericUpDown), new PropertyMetadata(false));

       

        #endregion       
    }
    
}
