using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CopyRecord
{
    internal enum TablePreviewAction
    {
        None,
        Paste,
        AddToQueue
    }

    internal sealed class TablePreviewWindow : Window
    {
        private readonly string _text;
        private readonly ComboBox _delimiter;
        private readonly CheckBox _preserveAsText;
        private readonly ListView _preview;
        private readonly TextBlock _summary;

        internal TablePreviewAction Action { get; private set; }
        internal TableTextResult Result { get; private set; }

        internal TablePreviewWindow(string text)
        {
            _text = text ?? string.Empty;
            Title = "表格预览";
            Width = 560;
            Height = 480;
            MinWidth = 560;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(247, 247, 247));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;

            Grid root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock heading = new TextBlock
            {
                Text = "粘贴为表格",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(heading);

            Grid controls = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.Children.Add(new TextBlock
            {
                Text = "分隔方式",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            });
            _delimiter = new ComboBox { Width = 135, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (TableDelimiterMode mode in Modes()) _delimiter.Items.Add(TableTextConverter.ModeName(mode));
            _delimiter.SelectedIndex = 0;
            _delimiter.SelectionChanged += delegate { RefreshPreview(); };
            Grid.SetColumn(_delimiter, 1);
            controls.Children.Add(_delimiter);

            _preserveAsText = new CheckBox
            {
                Content = "保留数字、前导零和公式样文字",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _preserveAsText.Checked += delegate { RefreshPreview(); };
            _preserveAsText.Unchecked += delegate { RefreshPreview(); };
            Grid.SetColumn(_preserveAsText, 2);
            controls.Children.Add(_preserveAsText);
            Grid.SetRow(controls, 1);
            root.Children.Add(controls);

            _preview = new ListView
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 205, 205)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(_preview, 2);
            root.Children.Add(_preview);

            _summary = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontSize = 12,
                Margin = new Thickness(0, 9, 0, 12)
            };
            Grid.SetRow(_summary, 3);
            root.Children.Add(_summary);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = new Button { Content = "取消", Width = 78, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            Button queue = new Button { Content = "加入队列", Width = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            Button paste = new Button { Content = "按表格粘贴", Width = 106, Height = 32, IsDefault = true };
            cancel.Click += delegate { DialogResult = false; };
            queue.Click += delegate { Action = TablePreviewAction.AddToQueue; DialogResult = true; };
            paste.Click += delegate { Action = TablePreviewAction.Paste; DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(queue);
            buttons.Children.Add(paste);
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_preview == null || _delimiter == null || _preserveAsText == null) return;
            TableDelimiterMode mode = Modes()[Math.Max(0, _delimiter.SelectedIndex)];
            Result = TableTextConverter.Convert(_text, mode, _preserveAsText.IsChecked == true);

            int visibleColumns = Math.Max(1, Math.Min(8, Result.ColumnCount));
            GridView view = new GridView();
            for (int column = 0; column < visibleColumns; column++)
            {
                view.Columns.Add(new GridViewColumn
                {
                    Header = ColumnName(column),
                    Width = Math.Max(90, (ActualWidth > 0 ? ActualWidth - 75 : 645) / visibleColumns),
                    DisplayMemberBinding = new Binding("[" + column + "]")
                });
            }
            _preview.View = view;

            List<string[]> rows = new List<string[]>();
            foreach (string[] source in Result.Rows.Take(30))
            {
                string[] padded = new string[visibleColumns];
                for (int column = 0; column < visibleColumns; column++)
                    padded[column] = column < source.Length ? source[column] : string.Empty;
                rows.Add(padded);
            }
            _preview.ItemsSource = rows;
            string detected = TableTextConverter.ModeName(Result.DetectedMode);
            string truncated = Result.RowCount > 30 || Result.ColumnCount > 8 ? "，预览仅显示前 30 行 × 8 列" : string.Empty;
            _summary.Text = Result.RowCount + " 行 × " + Result.ColumnCount + " 列 · " + detected + truncated;
        }

        private static TableDelimiterMode[] Modes()
        {
            return new[]
            {
                TableDelimiterMode.Auto,
                TableDelimiterMode.SingleColumn,
                TableDelimiterMode.Tab,
                TableDelimiterMode.Comma,
                TableDelimiterMode.ChineseComma,
                TableDelimiterMode.Semicolon,
                TableDelimiterMode.Pipe,
                TableDelimiterMode.MultipleSpaces
            };
        }

        private static string ColumnName(int index)
        {
            return index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();
        }
    }
}
