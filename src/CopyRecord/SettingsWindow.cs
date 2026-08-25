using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CopyRecord
{
    internal sealed class SettingsWindow : Window
    {
        private const double LabelWidth = 140;
        private readonly CheckBox _startup;
        private readonly ComboBox _modifiers;
        private readonly TextBox _key;
        private readonly ComboBox _maximumItems;
        private readonly TextBox _retentionDays;
        private readonly TextBox _maximumMegabytes;
        private readonly CheckBox _ignoreSensitiveText;
        private readonly TextBox _excludedApplications;

        internal AppSettings Result { get; private set; }

        internal SettingsWindow(AppSettings settings)
        {
            Title = "CopyRecord 设置";
            Width = 480;
            Height = 588;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;

            Grid root = new Grid { Margin = new Thickness(28, 24, 28, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock
            {
                Text = "设置",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30))
            });
            header.Children.Add(new TextBlock
            {
                Text = "调整 CopyRecord 的行为与隐私偏好",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            });
            root.Children.Add(header);

            StackPanel form = new StackPanel();
            ScrollViewer scroll = new ScrollViewer
            {
                Content = form,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 8, 0)
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            form.Children.Add(CreateSectionHeader("常规"));
            _startup = new CheckBox
            {
                Content = "开机自动启动 CopyRecord",
                IsChecked = settings.StartWithWindows,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            form.Children.Add(WrapField(_startup));

            form.Children.Add(CreateSectionHeader("呼出快捷键"));
            StackPanel hotkeyControls = new StackPanel { Orientation = Orientation.Horizontal };
            _modifiers = new ComboBox { Width = 112, Height = 30, Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
            _modifiers.Items.Add("Ctrl+Shift"); _modifiers.Items.Add("Ctrl+Alt"); _modifiers.Items.Add("Alt+Shift");
            _modifiers.SelectedItem = settings.HotkeyModifiers;
            _key = new TextBox { Width = 46, Height = 30, MaxLength = 1, Text = settings.HotkeyKey, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            hotkeyControls.Children.Add(_modifiers);
            hotkeyControls.Children.Add(_key);
            form.Children.Add(CreateField("组合键", hotkeyControls, "用于呼出主面板，保存后立即生效。"));

            form.Children.Add(CreateSectionHeader("容量"));
            _maximumItems = new ComboBox { Width = 166, Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (int value in new[] { 50, 200, 500, 1000, 5000, 10000, 50000 }) _maximumItems.Items.Add(value);
            _maximumItems.SelectedItem = settings.MaximumItems;
            if (_maximumItems.SelectedIndex < 0) _maximumItems.SelectedItem = 5000;
            form.Children.Add(CreateField("最大历史条数", _maximumItems, "超出后自动清理最早的未收藏记录。"));

            _retentionDays = CreateNumberBox(settings.ImageRetentionDays);
            form.Children.Add(CreateField("图片保留天数", _retentionDays, "填 0 表示不按天数清理；收藏图片不会删除。"));

            _maximumMegabytes = CreateNumberBox(settings.ImageMaximumMegabytes);
            form.Children.Add(CreateField("图片空间上限", _maximumMegabytes, "单位为 MB，超出后自动清理最早的未收藏图片。"));

            form.Children.Add(CreateSectionHeader("隐私"));
            _ignoreSensitiveText = new CheckBox
            {
                Content = "不记录疑似验证码、银行卡号和密码的单行文本",
                IsChecked = settings.IgnoreSensitiveText,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            form.Children.Add(WrapField(_ignoreSensitiveText));

            _excludedApplications = new TextBox
            {
                Width = 260,
                Height = 52,
                Text = settings.ExcludedApplications ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(7, 4, 7, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "填写进程名，用逗号或换行分隔"
            };
            form.Children.Add(CreateField("不记录这些应用", _excludedApplications, "应用名可从剪贴板条目下方看到；不需要填写 .exe。"));

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Button cancel = new Button { Content = "取消", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            Button save = new Button { Content = "保存", Width = 80, Height = 32, IsDefault = true };
            cancel.Click += delegate { DialogResult = false; };
            save.Click += SaveClicked;
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
        }

        private void SaveClicked(object sender, RoutedEventArgs eventArgs)
        {
            int days, megabytes;
            if (!int.TryParse(_retentionDays.Text, out days) || !int.TryParse(_maximumMegabytes.Text, out megabytes) || string.IsNullOrWhiteSpace(_key.Text))
            {
                MessageBox.Show("请填写有效的快捷键、保留天数和空间上限。", "CopyRecord", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Result = new AppSettings
            {
                StartWithWindows = _startup.IsChecked == true,
                HotkeyModifiers = Convert.ToString(_modifiers.SelectedItem),
                HotkeyKey = _key.Text,
                MaximumItems = Convert.ToInt32(_maximumItems.SelectedItem),
                ImageRetentionDays = days,
                ImageMaximumMegabytes = megabytes,
                IgnoreSensitiveText = _ignoreSensitiveText.IsChecked == true,
                ExcludedApplications = _excludedApplications.Text,
                FirstRunCompleted = true
            };
            Result.Normalize();
            DialogResult = true;
        }

        private static FrameworkElement CreateSectionHeader(string title)
        {
            Border line = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 16, 0, 10)
            };
            line.Child = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90))
            };
            return line;
        }

        private static FrameworkElement CreateField(string label, FrameworkElement control, string hint)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);
            Grid.SetColumn(control, 1);
            row.Children.Add(control);

            StackPanel wrapper = new StackPanel();
            wrapper.Children.Add(row);
            if (!string.IsNullOrEmpty(hint))
            {
                wrapper.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(LabelWidth, 2, 0, 12),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            return wrapper;
        }

        private static FrameworkElement WrapField(FrameworkElement control)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(control);
            return panel;
        }

        private static TextBox CreateNumberBox(int value)
        {
            return new TextBox
            {
                Width = 166,
                Height = 30,
                Text = value.ToString(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(7, 0, 7, 0)
            };
        }
    }
}
