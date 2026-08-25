using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CopyRecord
{
    internal sealed class MainWindow : Window
    {
        private const int HotkeyId = 17031;
        private const string AllAppsLabel = "\u5168\u90E8\u5E94\u7528";
        private const string FavoritesLabel = "\u6536\u85CF\u5185\u5BB9";
        private const string TypeAllLabel = "\u5168\u90E8\u7C7B\u578B";
        private const string TypeTextLabel = "\u6587\u672C";
        private const string TypeImageLabel = "\u56FE\u7247";
        private const string TypeFilesLabel = "\u6587\u4EF6";
        private const string TypeLinkLabel = "\u94FE\u63A5";
        private const string TypeCodeLabel = "\u4EE3\u7801";
        private readonly SettingsStore _settingsStore;
        private AppSettings _settings;
        private readonly HistoryStore _store;
        private readonly StorageWorkQueue _storageQueue;
        private readonly TextBox _searchBox;
        private readonly ComboBox _sourceFilter;
        private readonly ComboBox _typeFilter;
        private readonly ListBox _resultsList;
        private readonly TextBlock _statusText;
        private readonly TextBlock _modeText;
        private readonly Grid _resultsHost;
        private readonly Border _scrollThumb;
        private readonly DispatcherTimer _captureTimer;
        private readonly System.Windows.Forms.NotifyIcon _tray;
        private readonly Button _invalidFilterButton;
        private readonly Button _clearButton;
        private readonly PasteQueueSession _pasteQueue;
        private readonly Border _queueBar;
        private readonly TextBlock _queueText;
        private readonly Button _queueStartButton;
        private readonly Button _queueClearButton;
        private readonly Dictionary<uint, DateTime> _selfClipboardSequences = new Dictionary<uint, DateTime>();
        private HwndSource _source;
        private IntPtr _handle;
        private IntPtr _returnWindow;
        private int _captureAttempts;
        private bool _isPaused;
        private bool _isExiting;
        private bool _hotkeyRegistered;
        private bool _showInvalidFiles;
        private bool _updatingSourceFilter;
        private int _childDialogDepth;
        private bool _pasteDispatchInFlight;
        private bool _isWindowDragging;
        private System.Drawing.Point _dragStartCursor;
        private double _dragStartLeft;
        private double _dragStartTop;
        private bool _isScrollThumbDragging;
        private double _scrollDragStartY;
        private double _scrollDragStartOffset;

        internal MainWindow()
        {
            _settingsStore = new SettingsStore();
            _settings = _settingsStore.Load();
            _store = new HistoryStore(_settings);
            _storageQueue = new StorageWorkQueue();
            _storageQueue.Failed += StorageQueueFailed;
            _pasteQueue = new PasteQueueSession();

            Title = "CopyRecord 2026.8.24";
            Icon = AppIcon.CreateBitmapImage();
            Width = 372;
            Height = 455;
            MinWidth = 350;
            MinHeight = 430;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Brush("#FFF7F7F7");
            ShowInTaskbar = false;
            Topmost = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            Border shell = new Border
            {
                Background = Brush("#FFF7F7F7"),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(12),
                Effect = null
            };
            Content = shell;

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = layout;

            Grid topChrome = new Grid { Margin = new Thickness(4, 0, 2, 8), Background = Brushes.Transparent };
            topChrome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topChrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topChrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topChrome.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs eventArgs)
            {
                if (eventArgs.LeftButton != MouseButtonState.Pressed || IsInsideButton(eventArgs.OriginalSource as DependencyObject)) return;
                BeginManualWindowDrag(eventArgs);
            };
            StackPanel pickerHeader = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock pickerTitle = new TextBlock
            {
                Text = "CopyRecord 2026.8.24",
                Foreground = Brush("#FF202020"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };
            pickerHeader.Children.Add(pickerTitle);
            topChrome.Children.Add(pickerHeader);
            Button settingsButton = CreatePlainButton("\uE713");
            settingsButton.FontFamily = new FontFamily("Segoe MDL2 Assets");
            settingsButton.FontSize = 15;
            settingsButton.Width = 30;
            settingsButton.Height = 30;
            settingsButton.ToolTip = "设置";
            settingsButton.Click += delegate { ShowSettings(); };
            Grid.SetColumn(settingsButton, 1);
            topChrome.Children.Add(settingsButton);
            Button closeButton = CreatePlainButton("\uE711");
            closeButton.FontFamily = new FontFamily("Segoe MDL2 Assets");
            closeButton.FontSize = 12;
            closeButton.Width = 30;
            closeButton.Height = 30;
            closeButton.Padding = new Thickness(0);
            closeButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            closeButton.VerticalContentAlignment = VerticalAlignment.Center;
            closeButton.Click += delegate { Hide(); };
            Grid.SetColumn(closeButton, 2);
            topChrome.Children.Add(closeButton);
            layout.Children.Add(topChrome);

            Grid header = new Grid { Margin = new Thickness(4, 0, 4, 10), Background = Brushes.Transparent };
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs eventArgs)
            {
                if (eventArgs.LeftButton != MouseButtonState.Pressed || IsInsideButton(eventArgs.OriginalSource as DependencyObject)) return;
                BeginManualWindowDrag(eventArgs);
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock title = new TextBlock
            {
                Text = "剪贴板",
                Foreground = Brush("#FF1A1A1A"),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            _modeText = new TextBlock
            {
                Text = string.Empty,
                Foreground = Brush("#FF6B6B6B"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            brand.Children.Add(title);
            brand.Children.Add(_modeText);
            header.Children.Add(brand);

            StackPanel headerActions = new StackPanel { Orientation = Orientation.Horizontal };
            _invalidFilterButton = new Button
            {
                Content = "失效文件",
                Foreground = Brush("#FF545454"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(7, 5, 7, 5),
                Margin = new Thickness(0, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "查看已移动或删除的文件"
            };
            _invalidFilterButton.Click += delegate
            {
                _showInvalidFiles = !_showInvalidFiles;
                _invalidFilterButton.Content = _showInvalidFiles ? "全部记录" : "失效文件";
                _clearButton.Content = _showInvalidFiles ? "清理失效" : "全部清除";
                RefreshResults();
            };
            headerActions.Children.Add(_invalidFilterButton);

            _clearButton = new Button
            {
                Content = "全部清除",
                Foreground = Brush("#FF252525"),
                Background = Brushes.White,
                BorderBrush = Brush("#FFD0D0D0"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            _clearButton.Click += delegate
            {
                string prompt = _showInvalidFiles
                    ? "清理所有未收藏的失效文件记录？收藏的记录会保留。"
                    : "清空所有未收藏的剪贴板历史？";
                MessageBoxResult choice = ShowGuardedMessage(prompt, "CopyRecord",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Yes)
                {
                    if (_showInvalidFiles)
                    {
                        _storageQueue.Enqueue(delegate
                        {
                            int removed = _store.RemoveInvalidFiles();
                            NotifyStorageChanged(removed + " 条失效记录已清理");
                        });
                    }
                    else
                    {
                        _storageQueue.Enqueue(delegate
                        {
                            _store.ClearUnfavorited();
                            NotifyStorageChanged(null);
                        });
                    }
                }
            };
            headerActions.Children.Add(_clearButton);
            Grid.SetColumn(headerActions, 1);
            header.Children.Add(headerActions);
            Grid.SetRow(header, 1);
            layout.Children.Add(header);

            Grid searchRow = new Grid();
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });

            _searchBox = new TextBox
            {
                FontSize = 15,
                Foreground = Brush("#FF1A1A1A"),
                Background = Brushes.White,
                BorderBrush = Brush("#FFD1D1D1"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 4, 11, 4),
                CaretBrush = Brush("#FF0067C0"),
                SelectionBrush = Brush("#FF99C7F0"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = CreateRoundedSearchBoxStyle()
            };
            _searchBox.TextChanged += delegate { RefreshResults(); };
            searchRow.Children.Add(_searchBox);

            _sourceFilter = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(7, 0, 0, 0),
                Padding = new Thickness(7, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "\u6309\u590D\u5236\u6765\u6E90\u7B5B\u9009"
            };
            _sourceFilter.Items.Add(AllAppsLabel);
            _sourceFilter.Items.Add(FavoritesLabel);
            _sourceFilter.SelectedIndex = 0;
            _sourceFilter.SelectionChanged += delegate
            {
                if (!_updatingSourceFilter) RefreshResults();
            };
            Grid.SetColumn(_sourceFilter, 1);
            searchRow.Children.Add(_sourceFilter);

            _typeFilter = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(7, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "\u6309\u5185\u5BB9\u7C7B\u578B\u7B5B\u9009"
            };
            _typeFilter.Items.Add(TypeAllLabel);
            _typeFilter.Items.Add(TypeTextLabel);
            _typeFilter.Items.Add(TypeImageLabel);
            _typeFilter.Items.Add(TypeFilesLabel);
            _typeFilter.Items.Add(TypeLinkLabel);
            _typeFilter.Items.Add(TypeCodeLabel);
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectionChanged += delegate { RefreshResults(); };
            Grid.SetColumn(_typeFilter, 2);
            searchRow.Children.Add(_typeFilter);
            Grid.SetRow(searchRow, 2);
            layout.Children.Add(searchRow);

            _queueBar = new Border
            {
                Background = Brush("#FFEAF3FB"),
                BorderBrush = Brush("#FFC9DFF2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 6, 7, 6),
                Margin = new Thickness(0, 7, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid queueLayout = new Grid();
            queueLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            queueLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            queueLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _queueText = new TextBlock
            {
                Foreground = Brush("#FF315B7D"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 6, 0)
            };
            queueLayout.Children.Add(_queueText);
            _queueStartButton = new Button
            {
                Content = "\u786E\u5B9A", Height = 27, Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 5, 0)
            };
            _queueStartButton.Click += delegate { StartPasteQueue(); };
            Grid.SetColumn(_queueStartButton, 1);
            queueLayout.Children.Add(_queueStartButton);
            _queueClearButton = new Button { Content = "清空", Height = 27, Padding = new Thickness(8, 2, 8, 2) };
            _queueClearButton.Click += delegate
            {
                _pasteQueue.Clear();
                UpdateQueueBar();
            };
            Grid.SetColumn(_queueClearButton, 2);
            queueLayout.Children.Add(_queueClearButton);
            _queueBar.Child = queueLayout;
            Grid.SetRow(_queueBar, 3);
            layout.Children.Add(_queueBar);

            _resultsList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 2),
                Foreground = Brush("#FF1A1A1A"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemContainerStyle = CreateItemContainerStyle()
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_resultsList, ScrollBarVisibility.Hidden);
            ScrollViewer.SetCanContentScroll(_resultsList, false);
            _resultsList.PreviewMouseWheel += ResultsPreviewMouseWheel;
            _resultsList.MouseDoubleClick += delegate { PasteSelected(false); };
            _resultsList.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(ResultsScrollChanged));

            _resultsHost = new Grid();
            _resultsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _resultsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            _resultsHost.Children.Add(_resultsList);
            _scrollThumb = new Border
            {
                Width = 10,
                MinHeight = 18,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 9, 0, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = true,
                Cursor = Cursors.SizeNS,
                RenderTransform = new TranslateTransform()
            };
            _scrollThumb.Child = new Border
            {
                Width = 4,
                Background = Brush("#8A6F6F6F"),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _scrollThumb.MouseLeftButtonDown += ScrollThumbMouseLeftButtonDown;
            _scrollThumb.MouseMove += ScrollThumbMouseMove;
            _scrollThumb.MouseLeftButtonUp += ScrollThumbMouseLeftButtonUp;
            Grid.SetColumn(_scrollThumb, 1);
            _resultsHost.Children.Add(_scrollThumb);
            Grid.SetRow(_resultsHost, 4);
            layout.Children.Add(_resultsHost);

            Grid footer = new Grid { Margin = new Thickness(2, 4, 2, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _statusText = new TextBlock
            {
                Foreground = Brush("#FF686868"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock hints = new TextBlock
            {
                Text = "双击复制",
                Foreground = Brush("#FF777777"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(_statusText);
            Grid.SetColumn(hints, 1);
            footer.Children.Add(hints);
            Grid.SetRow(footer, 5);
            layout.Children.Add(footer);

            _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _captureTimer.Tick += CaptureTimerTick;

            _tray = CreateTrayIcon();

            PreviewKeyDown += WindowPreviewKeyDown;
            MouseMove += WindowDragMouseMove;
            MouseLeftButtonUp += WindowDragMouseUp;
            Closing += WindowClosing;
            SourceInitialized += delegate { EnableAcrylicBackground(); };
        }

        internal void StartBackground()
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            _handle = helper.EnsureHandle();
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(WindowMessageHook);

            NativeMethods.AddClipboardFormatListener(_handle);
            bool hotkeyReady = RegisterConfiguredHotkey();

            _tray.Visible = true;
            if (!hotkeyReady)
            {
                _tray.ShowBalloonTip(4000, "CopyRecord",
                    GetHotkeyDisplay() + " 已被其他程序占用。可从托盘打开 CopyRecord。",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
            RefreshResults();
            RefreshSourceFilter();
        }

        internal bool ConsumeFirstRun()
        {
            if (_settings.FirstRunCompleted) return false;
            _settings.FirstRunCompleted = true;
            try { _settingsStore.Save(_settings); } catch { }
            return true;
        }

        internal void ShowPalette()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (!_pasteQueue.IsActive && foreground != _handle) _returnWindow = foreground;

            _searchBox.Text = string.Empty;
            RefreshSourceFilter();
            RefreshResults();
            UpdateQueueBar();
            Show();
            PositionNearCursor();
            Activate();
            _searchBox.Focus();
            Keyboard.Focus(_searchBox);
        }

        internal void ExitApplication()
        {
            _isExiting = true;
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_handle);
                if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(_handle, HotkeyId);
            }
            if (_source != null) _source.RemoveHook(WindowMessageHook);
            _tray.Visible = false;
            _tray.Dispose();
            _storageQueue.Dispose();
            _store.Dispose();
            Application.Current.Shutdown();
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                if (_childDialogDepth > 0)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                ShowPalette();
                handled = true;
            }
            else if (message == NativeMethods.WM_CLIPBOARDUPDATE && !_isPaused)
            {
                _captureAttempts = 0;
                _captureTimer.Interval = TimeSpan.FromMilliseconds(70);
                _captureTimer.Stop();
                _captureTimer.Start();
            }
            return IntPtr.Zero;
        }

        private void CaptureTimerTick(object sender, EventArgs eventArgs)
        {
            _captureTimer.Stop();
            if (ConsumeSelfClipboardSequence()) return;
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    StringCollection fileDrop = Clipboard.GetFileDropList();
                    List<string> paths = new List<string>();
                    foreach (string path in fileDrop) paths.Add(path);
                    string fileSourceApp;
                    string fileSourceTitle;
                    GetForegroundApp(out fileSourceApp, out fileSourceTitle);
                    if (ClipboardCapturePolicy.ShouldIgnoreSource(_settings, fileSourceApp)) return;
                    string[] capturedPaths = paths.ToArray();
                    _storageQueue.Enqueue(delegate
                    {
                        _store.AddOrRefreshFiles(capturedPaths, fileSourceApp, fileSourceTitle);
                        NotifyStorageChanged(null);
                    });
                    return;
                }

                BitmapSource image = TryGetClipboardImage();
                if (image != null)
                {
                    string imageSourceApp;
                    string imageSourceTitle;
                    GetForegroundApp(out imageSourceApp, out imageSourceTitle);
                    if (ClipboardCapturePolicy.ShouldIgnoreSource(_settings, imageSourceApp)) return;
                    BitmapSource capturedImage = image.IsFrozen ? image : image.Clone();
                    if (!capturedImage.IsFrozen) capturedImage.Freeze();
                    _storageQueue.Enqueue(delegate
                    {
                        _store.AddOrRefreshImage(capturedImage, imageSourceApp, imageSourceTitle);
                        NotifyStorageChanged(null);
                    });
                    return;
                }

                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;
                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(text)) return;

                string rtf = Clipboard.ContainsData(DataFormats.Rtf) ? Clipboard.GetData(DataFormats.Rtf) as string : null;
                string html = Clipboard.ContainsData(DataFormats.Html) ? Clipboard.GetData(DataFormats.Html) as string : null;
                string sourceApp;
                string sourceTitle;
                GetForegroundApp(out sourceApp, out sourceTitle);
                if (ClipboardCapturePolicy.ShouldIgnoreSource(_settings, sourceApp) ||
                    ClipboardCapturePolicy.ShouldIgnoreText(_settings, text)) return;
                _storageQueue.Enqueue(delegate
                {
                    _store.AddOrRefresh(text, rtf, html, sourceApp, sourceTitle);
                    NotifyStorageChanged(null);
                });
            }
            catch (ExternalException)
            {
                RetryCapture();
            }
        }

        private void RetryCapture()
        {
            _captureAttempts++;
            if (_captureAttempts >= 5) return;
            _captureTimer.Interval = TimeSpan.FromMilliseconds(70 * _captureAttempts);
            _captureTimer.Start();
        }

        private void RefreshResults()
        {
            ClipboardItem selected = GetSelectedItem();
            string sourceApp = SelectedSourceApplication();
            bool showFavorites = IsFavoritesSelected();
            ContentTypeFilterMode typeFilter = SelectedContentTypeFilter();
            List<ClipboardItem> results = showFavorites
                ? _store.Search(_searchBox.Text, null, 100, true, typeFilter)
                : _showInvalidFiles
                    ? _store.SearchInvalidFiles(sourceApp, 100)
                    : _store.Search(_searchBox.Text, sourceApp, 100, false, typeFilter);
            _resultsList.Items.Clear();
            foreach (ClipboardItem item in results)
            {
                _resultsList.Items.Add(CreateResultListItem(item));
            }
            if (results.Count > 0)
            {
                int index = selected == null ? 0 : results.FindIndex(item => item.Id == selected.Id);
                _resultsList.SelectedIndex = index >= 0 ? index : 0;
            }
            _statusText.Text = results.Count.ToString() + (_showInvalidFiles ? " 条失效文件" : " 条结果") +
                (_isPaused ? "  ·  已暂停记录" : string.Empty);
            _modeText.Text = _isPaused ? "  已暂停" : string.Empty;
        }

        private void NotifyStorageChanged(string message)
        {
            if (_isExiting) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_isExiting) return;
                if (IsVisible)
                {
                    RefreshSourceFilter();
                    RefreshResults();
                }
                if (!string.IsNullOrEmpty(message)) _statusText.Text = message;
            }));
        }

        private void StorageQueueFailed(Exception exception)
        {
            NotifyStorageChanged("保存失败，请稍后重试");
        }

        private string SelectedSourceApplication()
        {
            string value = Convert.ToString(_sourceFilter.SelectedItem);
            return string.IsNullOrEmpty(value) || value == AllAppsLabel || value == FavoritesLabel ? null : value;
        }

        private bool IsFavoritesSelected()
        {
            return Convert.ToString(_sourceFilter.SelectedItem) == FavoritesLabel;
        }

        private ContentTypeFilterMode SelectedContentTypeFilter()
        {
            string value = _typeFilter == null ? null : Convert.ToString(_typeFilter.SelectedItem);
            if (value == TypeTextLabel) return ContentTypeFilterMode.Text;
            if (value == TypeImageLabel) return ContentTypeFilterMode.Image;
            if (value == TypeFilesLabel) return ContentTypeFilterMode.Files;
            if (value == TypeLinkLabel) return ContentTypeFilterMode.Link;
            if (value == TypeCodeLabel) return ContentTypeFilterMode.Code;
            return ContentTypeFilterMode.All;
        }

        private void RefreshSourceFilter()
        {
            if (_sourceFilter == null) return;
            bool favoritesSelected = IsFavoritesSelected();
            string selected = SelectedSourceApplication();
            List<string> applications = _store.GetSourceApplications(30);
            _updatingSourceFilter = true;
            try
            {
                _sourceFilter.Items.Clear();
                _sourceFilter.Items.Add(AllAppsLabel);
                _sourceFilter.Items.Add(FavoritesLabel);
                foreach (string application in applications) _sourceFilter.Items.Add(application);
                if (favoritesSelected)
                {
                    _sourceFilter.SelectedItem = FavoritesLabel;
                }
                else
                {
                    string matched = applications.Find(item =>
                        string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
                    _sourceFilter.SelectedItem = string.IsNullOrEmpty(matched) ? AllAppsLabel : matched;
                }
            }
            finally
            {
                _updatingSourceFilter = false;
            }
        }

        private void PasteSelected(bool plainTextOnly)
        {
            ClipboardItem item = GetSelectedItem();
            PasteItem(item, plainTextOnly);
        }

        private void PasteItem(ClipboardItem item, bool plainTextOnly)
        {
            DispatchPasteEntry(PasteQueueEntry.FromItem(item, plainTextOnly), _returnWindow, false);
        }

        private void DispatchPasteEntry(PasteQueueEntry entry, IntPtr targetWindow, bool fromQueue)
        {
            if (entry == null || _pasteDispatchInFlight) return;
            _pasteDispatchInFlight = true;
            string error;
            uint expectedClipboardSequence;
            if (!PrepareClipboard(entry, out expectedClipboardSequence, out error))
            {
                _pasteDispatchInFlight = false;
                if (fromQueue) _pasteQueue.CompleteCurrent(false);
                ReportPasteFailure(error);
                UpdateQueueBar();
                return;
            }
            FinishPasteDispatch(entry, fromQueue, true, null);
        }

        private bool PrepareClipboard(PasteQueueEntry entry, out uint clipboardSequence, out string error)
        {
            clipboardSequence = 0;
            error = null;
            try
            {
                ClipboardItem item = entry.Item;
                if (entry.Mode == PasteEntryMode.Table)
                {
                    if (string.IsNullOrEmpty(entry.Text))
                    {
                        error = "表格内容为空";
                        return false;
                    }
                    DataObject tableData = new DataObject();
                    tableData.SetData(DataFormats.UnicodeText, entry.Text);
                    tableData.SetData(DataFormats.Text, entry.Text);
                    if (!string.IsNullOrEmpty(entry.Html)) tableData.SetData(DataFormats.Html, entry.Html);
                    Clipboard.SetDataObject(tableData, true);
                }
                else if (item != null && item.IsFileList)
                {
                    StringCollection files = new StringCollection();
                    foreach (string path in item.FilePaths)
                    {
                        if (File.Exists(path) || Directory.Exists(path)) files.Add(path);
                    }
                    if (files.Count == 0)
                    {
                        error = "文件已被移动或删除";
                        return false;
                    }
                    Clipboard.SetFileDropList(files);
                }
                else if (item != null && item.IsImage)
                {
                    BitmapSource bitmap = LoadBitmap(item.ImagePath);
                    if (bitmap == null)
                    {
                        error = "图片文件已丢失";
                        return false;
                    }
                    Clipboard.SetImage(bitmap);
                }
                else
                {
                    if (item == null || string.IsNullOrEmpty(item.Text))
                    {
                        error = "文字内容为空";
                        return false;
                    }
                    DataObject data = new DataObject();
                    data.SetData(DataFormats.UnicodeText, item.Text);
                    data.SetData(DataFormats.Text, item.Text);
                    if (entry.Mode == PasteEntryMode.Original)
                    {
                        if (!string.IsNullOrEmpty(item.Rtf)) data.SetData(DataFormats.Rtf, item.Rtf);
                        if (!string.IsNullOrEmpty(item.Html)) data.SetData(DataFormats.Html, item.Html);
                    }
                    Clipboard.SetDataObject(data, true);
                }
                clipboardSequence = RememberSelfClipboardSequence();
                return true;
            }
            catch (ExternalException)
            {
                error = "剪贴板正被其他应用占用，请重试";
                return false;
            }
        }

        private void FinishPasteDispatch(PasteQueueEntry entry, bool fromQueue, bool success, string error)
        {
            _pasteDispatchInFlight = false;
            if (fromQueue) _pasteQueue.CompleteCurrent(success);
            if (success && entry != null && entry.Item != null)
            {
                ClipboardItem used = entry.Item;
                _storageQueue.Enqueue(delegate { _store.MarkUsed(used); });
            }
            if (!success) ReportPasteFailure(error);
            else if (fromQueue && _pasteQueue.Count == 0)
                _tray.ShowBalloonTip(1600, "CopyRecord", "连续复制队列已完成", System.Windows.Forms.ToolTipIcon.Info);
            else if (!fromQueue) _statusText.Text = "已复制到剪贴板";
            UpdateQueueBar();
        }

        private void ReportPasteFailure(string message)
        {
            string value = string.IsNullOrEmpty(message) ? "复制失败，请重试" : message;
            _statusText.Text = value;
            if (!IsVisible) _tray.ShowBalloonTip(2200, "CopyRecord", value, System.Windows.Forms.ToolTipIcon.Warning);
        }

        private void AddToPasteQueue(PasteQueueEntry entry)
        {
            if (entry == null) return;
            _pasteQueue.Add(entry);
            UpdateQueueBar();
            _statusText.Text = "已加入连续复制队列，共 " + _pasteQueue.Count + " 条";
        }

        private void StartPasteQueue()
        {
            if (_pasteQueue.Count == 0) return;
            int count = _pasteQueue.Count;
            string combined = _pasteQueue.BuildCombinedText();
            _pasteQueue.Clear();
            UpdateQueueBar();
            if (string.IsNullOrEmpty(combined))
            {
                _statusText.Text = "队列中没有可连接的文字内容";
                _tray.ShowBalloonTip(1600, "CopyRecord", "队列中没有可连接的文字内容", System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, combined);
            data.SetData(DataFormats.Text, combined);
            Clipboard.SetDataObject(data, true);
            _statusText.Text = "已连接 " + count + " 条记录并复制到剪贴板";
            _tray.ShowBalloonTip(1600, "CopyRecord", "已连接队列内容并复制到剪贴板", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void BeginQueuePaste(IntPtr targetWindow)
        {
            if (_pasteDispatchInFlight) return;
            PasteQueueEntry entry;
            if (!_pasteQueue.TryBeginNext(out entry)) return;
            DispatchPasteEntry(entry, targetWindow, true);
        }

        private void UpdateQueueBar()
        {
            if (_queueBar == null) return;
            _queueBar.Visibility = _pasteQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_pasteQueue.Count > 0)
            {
                _queueText.Text = "队列 · " + _pasteQueue.Count + " 条 · 点击开始连接并复制";
            }
            _queueStartButton.Content = "\u786E\u5B9A";
            _queueStartButton.IsEnabled = _pasteQueue.Count > 0;
            _queueClearButton.IsEnabled = true;
            _tray.Text = _pasteQueue.Count > 0
                ? "CopyRecord · 队列 " + _pasteQueue.Count + " 条"
                : "CopyRecord 剪贴板管理器";
        }

        private void ShowTablePreview(ClipboardItem item)
        {
            if (item == null || item.IsImage || item.IsFileList || string.IsNullOrEmpty(item.Text) || !HasMultipleLines(item.Text))
            {
                _statusText.Text = "只有多行文字可以转换为表格";
                return;
            }

            _childDialogDepth++;
            try
            {
                TablePreviewWindow preview = new TablePreviewWindow(item.Text)
                {
                    Owner = this,
                    Topmost = true
                };
                if (preview.ShowDialog() != true || preview.Result == null) return;
                PasteQueueEntry entry = PasteQueueEntry.FromTable(item, preview.Result);
                if (preview.Action == TablePreviewAction.AddToQueue) AddToPasteQueue(entry);
                else if (preview.Action == TablePreviewAction.Paste) DispatchPasteEntry(entry, _returnWindow, false);
            }
            finally
            {
                _childDialogDepth--;
            }
        }

        private MessageBoxResult ShowGuardedMessage(string message, string caption,
            MessageBoxButton buttons, MessageBoxImage image)
        {
            _childDialogDepth++;
            try
            {
                return MessageBox.Show(this, message, caption, buttons, image);
            }
            finally
            {
                _childDialogDepth--;
            }
        }

        private static bool HasMultipleLines(string text)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOfAny(new[] { '\r', '\n', '\u2028', '\u2029' }) >= 0;
        }

        private uint RememberSelfClipboardSequence()
        {
            PruneClipboardSequences();
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence != 0) _selfClipboardSequences[sequence] = DateTime.UtcNow.AddSeconds(5);
            return sequence;
        }

        private bool ConsumeSelfClipboardSequence()
        {
            PruneClipboardSequences();
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == 0 || !_selfClipboardSequences.ContainsKey(sequence)) return false;
            return true;
        }

        private void PruneClipboardSequences()
        {
            DateTime now = DateTime.UtcNow;
            List<uint> expired = new List<uint>();
            foreach (KeyValuePair<uint, DateTime> pair in _selfClipboardSequences)
            {
                if (pair.Value < now) expired.Add(pair.Key);
            }
            foreach (uint sequence in expired) _selfClipboardSequences.Remove(sequence);
        }

        private static BitmapSource TryGetClipboardImage()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    BitmapSource direct = Clipboard.GetImage();
                    if (direct != null)
                    {
                        if (direct.CanFreeze) direct.Freeze();
                        return direct;
                    }
                }

                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return null;
                string[] pngFormats = { "PNG", "image/png" };
                foreach (string format in pngFormats)
                {
                    if (!data.GetDataPresent(format, true)) continue;
                    object raw = data.GetData(format, true);
                    Stream stream = raw as Stream;
                    if (stream != null)
                    {
                        if (stream.CanSeek) stream.Position = 0;
                        BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        frame.Freeze();
                        return frame;
                    }
                    byte[] bytes = raw as byte[];
                    if (bytes != null && bytes.Length > 0)
                    {
                        using (MemoryStream memory = new MemoryStream(bytes))
                        {
                            BitmapFrame frame = BitmapFrame.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            frame.Freeze();
                            return frame;
                        }
                    }
                    System.Drawing.Image drawingImage = raw as System.Drawing.Image;
                    if (drawingImage != null)
                    {
                        using (MemoryStream memory = new MemoryStream())
                        {
                            drawingImage.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                            memory.Position = 0;
                            BitmapFrame frame = BitmapFrame.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            frame.Freeze();
                            return frame;
                        }
                    }
                }

                if (data.GetDataPresent(DataFormats.Bitmap, true))
                {
                    object rawBitmap = data.GetData(DataFormats.Bitmap, true);
                    BitmapSource source = rawBitmap as BitmapSource;
                    if (source != null)
                    {
                        if (source.CanFreeze) source.Freeze();
                        return source;
                    }

                    System.Drawing.Bitmap drawingBitmap = rawBitmap as System.Drawing.Bitmap;
                    if (drawingBitmap != null)
                    {
                        IntPtr handle = drawingBitmap.GetHbitmap();
                        try
                        {
                            BitmapSource converted = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero,
                                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            converted.Freeze();
                            return converted;
                        }
                        finally { NativeMethods.DeleteObject(handle); }
                    }
                }
            }
            catch (ExternalException) { throw; }
            catch { }
            return null;
        }

        private static BitmapSource LoadBitmap(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    FormatConvertedBitmap bitmap = new FormatConvertedBitmap(
                        decoder.Frames[0], PixelFormats.Bgr32, null, 0);
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { return null; }
        }

        private static BitmapSource LoadThumbnailBitmap(string path, int maxDimension)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = maxDimension;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; }
        }

        private static object BuildImageToolTip(BitmapSource source)
        {
            if (source == null) return null;
            return new Border
            {
                Background = Brush("#FFFFFFFF"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6),
                Child = new Image { Source = source, Width = 380, MaxHeight = 420, Stretch = Stretch.Uniform }
            };
        }

        private void WindowPreviewKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Key.Escape)
            {
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.Down || eventArgs.Key == Key.Up)
            {
                int count = _resultsList.Items.Count;
                if (count == 0) return;
                int delta = eventArgs.Key == Key.Down ? 1 : -1;
                int next = Math.Max(0, Math.Min(count - 1, _resultsList.SelectedIndex + delta));
                _resultsList.SelectedIndex = next;
                _resultsList.ScrollIntoView(_resultsList.SelectedItem);
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.Enter &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowTablePreview(GetSelectedItem());
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ClipboardItem item = GetSelectedItem();
                _store.ToggleFavorite(item);
                RefreshResults();
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.Delete)
            {
                ClipboardItem item = GetSelectedItem();
                _store.Remove(item);
                RefreshResults();
                eventArgs.Handled = true;
            }
        }

        private void PositionNearCursor()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(cursor);
            System.Drawing.Rectangle area = screen.WorkingArea;
            IntPtr window = _handle != IntPtr.Zero ? _handle : new WindowInteropHelper(this).Handle;
            if (window == IntPtr.Zero) return;

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(window, out rect)) return;
            int windowWidth = rect.Right - rect.Left;
            int windowHeight = rect.Bottom - rect.Top;
            const int edgeGap = 12;
            int targetX = area.Right - windowWidth - edgeGap;
            int targetY = area.Bottom - windowHeight - edgeGap;
            NativeMethods.SetWindowPos(window, IntPtr.Zero, targetX, targetY, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        private void EnableAcrylicBackground()
        {
            IntPtr window = new WindowInteropHelper(this).Handle;
            if (window == IntPtr.Zero) return;

            int cornerPreference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(window, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref cornerPreference, sizeof(int));

            int darkMode = 0;
            NativeMethods.DwmSetWindowAttribute(window, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkMode, sizeof(int));

            int backdrop = NativeMethods.DWMSBT_NONE;
            NativeMethods.DwmSetWindowAttribute(window,
                NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        private void ResultsScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
        {
            if (eventArgs.ExtentHeight <= eventArgs.ViewportHeight || eventArgs.ViewportHeight <= 0)
            {
                _scrollThumb.Visibility = Visibility.Collapsed;
                return;
            }

            double availableHeight = Math.Max(0, _resultsHost.ActualHeight - 18);
            double thumbHeight = Math.Max(18,
                availableHeight * eventArgs.ViewportHeight / eventArgs.ExtentHeight);
            double travel = Math.Max(0, availableHeight - thumbHeight);
            double range = Math.Max(1, eventArgs.ExtentHeight - eventArgs.ViewportHeight);
            double offset = travel * eventArgs.VerticalOffset / range;
            _scrollThumb.Height = thumbHeight;
            TranslateTransform transform = _scrollThumb.RenderTransform as TranslateTransform;
            if (transform != null) transform.Y = offset;
            _scrollThumb.Visibility = Visibility.Visible;
        }

        private void ScrollThumbMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
        {
            ScrollViewer viewer = FindVisualChild<ScrollViewer>(_resultsList);
            if (viewer == null) return;
            _isScrollThumbDragging = true;
            _scrollDragStartY = eventArgs.GetPosition(_resultsHost).Y;
            _scrollDragStartOffset = viewer.VerticalOffset;
            _scrollThumb.CaptureMouse();
            eventArgs.Handled = true;
        }

        private void ScrollThumbMouseMove(object sender, MouseEventArgs eventArgs)
        {
            if (!_isScrollThumbDragging || eventArgs.LeftButton != MouseButtonState.Pressed) return;
            ScrollViewer viewer = FindVisualChild<ScrollViewer>(_resultsList);
            if (viewer == null) return;
            double availableHeight = Math.Max(0, _resultsHost.ActualHeight - 18);
            double travel = Math.Max(1, availableHeight - _scrollThumb.ActualHeight);
            double range = Math.Max(0, viewer.ExtentHeight - viewer.ViewportHeight);
            double delta = eventArgs.GetPosition(_resultsHost).Y - _scrollDragStartY;
            viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(range, _scrollDragStartOffset + delta * range / travel)));
            eventArgs.Handled = true;
        }

        private void ScrollThumbMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
        {
            if (!_isScrollThumbDragging) return;
            _isScrollThumbDragging = false;
            _scrollThumb.ReleaseMouseCapture();
            eventArgs.Handled = true;
        }

        private void ResultsPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
        {
            ScrollViewer viewer = FindVisualChild<ScrollViewer>(_resultsList);
            if (viewer == null) return;

            // Mouse wheel reports 120 units per notch. Divide by six for a compact
            // 20px movement while retaining proportional precision for touchpads.
            double pixelDelta = eventArgs.Delta / 6.0;
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset - pixelDelta);
            eventArgs.Handled = true;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                T match = child as T;
                if (match != null) return match;
                match = FindVisualChild<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        private static void GetForegroundApp(out string app, out string title)
        {
            app = "未知应用";
            title = string.Empty;
            try
            {
                IntPtr window = NativeMethods.GetForegroundWindow();
                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                Process process = Process.GetProcessById((int)processId);
                app = process.ProcessName;
                StringBuilder buffer = new StringBuilder(512);
                NativeMethods.GetWindowText(window, buffer, buffer.Capacity);
                title = buffer.ToString();
            }
            catch { }
        }

        private System.Windows.Forms.NotifyIcon CreateTrayIcon()
        {
            System.Windows.Forms.NotifyIcon tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "CopyRecord 剪贴板管理器",
                Icon = AppIcon.CreateDrawingIcon(16)
            };
            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            System.Windows.Forms.ToolStripMenuItem open = new System.Windows.Forms.ToolStripMenuItem("打开 CopyRecord");
            System.Windows.Forms.ToolStripMenuItem pause = new System.Windows.Forms.ToolStripMenuItem("暂停记录");
            System.Windows.Forms.ToolStripMenuItem settings = new System.Windows.Forms.ToolStripMenuItem("设置…");
            System.Windows.Forms.ToolStripMenuItem cancelQueue = new System.Windows.Forms.ToolStripMenuItem("取消连续复制队列");
            System.Windows.Forms.ToolStripMenuItem clear = new System.Windows.Forms.ToolStripMenuItem("清空未收藏历史");
            System.Windows.Forms.ToolStripMenuItem exit = new System.Windows.Forms.ToolStripMenuItem("退出");
            open.Click += delegate { Dispatcher.BeginInvoke(new Action(ShowPalette)); };
            pause.Click += delegate
            {
                _isPaused = !_isPaused;
                pause.Text = _isPaused ? "恢复记录" : "暂停记录";
                Dispatcher.BeginInvoke(new Action(RefreshResults));
            };
            settings.Click += delegate { Dispatcher.BeginInvoke(new Action(ShowSettings)); };
            cancelQueue.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _pasteQueue.Clear();
                    UpdateQueueBar();
                }));
            };
            clear.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    MessageBoxResult choice = ShowGuardedMessage("清空所有未收藏的剪贴板历史？", "CopyRecord",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (choice == MessageBoxResult.Yes)
                    {
                        _store.ClearUnfavorited();
                        RefreshResults();
                    }
                }));
            };
            exit.Click += delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); };
            menu.Items.Add(open);
            menu.Items.Add(pause);
            menu.Items.Add(settings);
            menu.Items.Add(cancelQueue);
            menu.Items.Add(clear);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowPalette)); };
            return tray;
        }

        internal void ShowSettings()
        {
            SettingsWindow window = new SettingsWindow(_settings) { Owner = this, Topmost = true };
            _childDialogDepth++;
            try
            {
                if (window.ShowDialog() != true || window.Result == null) return;
                AppSettings previous = _settings;
                _settings = window.Result;
                _settingsStore.Save(_settings);
                AppSettings settingsToApply = _settings;
                _storageQueue.Enqueue(delegate
                {
                    _store.ApplySettings(settingsToApply);
                    NotifyStorageChanged(null);
                });

                if (_hotkeyRegistered)
                {
                    NativeMethods.UnregisterHotKey(_handle, HotkeyId);
                    _hotkeyRegistered = false;
                }
                if (!RegisterConfiguredHotkey())
                {
                    _settings.HotkeyModifiers = previous.HotkeyModifiers;
                    _settings.HotkeyKey = previous.HotkeyKey;
                    _settingsStore.Save(_settings);
                    RegisterConfiguredHotkey();
                    MessageBox.Show(this, "新快捷键已被其他程序占用，已恢复为 " + GetHotkeyDisplay() + "。",
                        "CopyRecord", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                RefreshResults();
            }
            finally
            {
                _childDialogDepth--;
            }
        }

        private bool RegisterConfiguredHotkey()
        {
            uint modifiers = _settings.HotkeyModifiers == "Ctrl+Alt"
                ? NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT
                : _settings.HotkeyModifiers == "Alt+Shift"
                    ? NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT
                    : NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;
            char key = string.IsNullOrEmpty(_settings.HotkeyKey) ? 'V' : char.ToUpperInvariant(_settings.HotkeyKey[0]);
            _hotkeyRegistered = NativeMethods.RegisterHotKey(_handle, HotkeyId, modifiers, (uint)key);
            return _hotkeyRegistered;
        }

        private string GetHotkeyDisplay()
        {
            return _settings.HotkeyModifiers + "+" + _settings.HotkeyKey;
        }

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs eventArgs)
        {
            if (_isExiting) return;
            eventArgs.Cancel = true;
            Hide();
        }

        private static Brush Brush(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }

        private ClipboardItem GetSelectedItem()
        {
            ListBoxItem container = _resultsList.SelectedItem as ListBoxItem;
            return container == null ? null : container.Tag as ClipboardItem;
        }

        private ListBoxItem CreateResultListItem(ClipboardItem item)
        {
            if (item.IsFileList)
            {
                string[] paths = item.FilePaths;
                string existingPath = FirstExistingPath(paths);
                bool exists = !string.IsNullOrEmpty(existingPath);
                bool hasInvalidPaths = item.HasInvalidFilePaths;
                Grid fileCard = new Grid { Height = 44 };
                fileCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                fileCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fileCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

                Border iconFrame = new Border
                {
                    Width = 36, Height = 36, CornerRadius = new CornerRadius(4),
                    Background = Brush("#FFF0F0F0"), HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    ClipToBounds = true
                };
                BitmapSource imagePreview = ClipboardItem.IsImageFilePath(existingPath) ? LoadThumbnailBitmap(existingPath, 128) : null;
                if (imagePreview != null)
                {
                    iconFrame.Child = new Image
                    {
                        Source = imagePreview, Width = 36, Height = 36, Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    BitmapSource fileIcon = LoadFileIcon(existingPath);
                    iconFrame.Child = fileIcon != null
                        ? (UIElement)new Image { Source = fileIcon, Width = 28, Height = 28, Stretch = Stretch.Uniform }
                        : new TextBlock
                        {
                            Text = Directory.Exists(existingPath) ? "\uE8B7" : "\uE8A5",
                            FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22,
                            Foreground = Brush("#FF4F6F8F"), HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                }
                fileCard.Children.Add(iconFrame);

                StackPanel fileInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                fileInfo.Children.Add(new TextBlock
                {
                    Text = item.DisplayPreview, FontSize = 14, Foreground = exists ? Brush("#FF1A1A1A") : Brush("#FF8A8A8A"),
                    TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = string.Join("\n", paths)
                });
                fileInfo.Children.Add(new TextBlock
                {
                    Text = !hasInvalidPaths ? item.Detail : exists ? "部分文件已移动或删除" : "文件已被移动或删除",
                    FontSize = 11, Foreground = hasInvalidPaths ? Brush("#FFB04A4A") : Brush("#FF6E6E6E"),
                    Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(fileInfo, 1);
                fileCard.Children.Add(fileInfo);

                ContextMenu fileMenu = new ContextMenu();
                MenuItem open = new MenuItem { Header = "打开", IsEnabled = exists };
                MenuItem reveal = new MenuItem { Header = "打开所在位置", IsEnabled = exists };
                MenuItem relocate = new MenuItem { Header = "重新定位失效路径…", IsEnabled = hasInvalidPaths };
                MenuItem enqueue = new MenuItem { Header = "加入连续复制队列" };
                MenuItem favorite = new MenuItem { Header = item.IsFavorite ? "取消收藏" : "收藏" };
                MenuItem delete = new MenuItem { Header = "删除" };
                open.Click += delegate { OpenFilePath(existingPath); };
                reveal.Click += delegate { RevealFilePath(existingPath); };
                relocate.Click += delegate { RelocateInvalidPath(item); };
                enqueue.Click += delegate { AddToPasteQueue(PasteQueueEntry.FromItem(item, false)); };
                favorite.Click += delegate { _store.ToggleFavorite(item); RefreshResults(); };
                delete.Click += delegate { _store.Remove(item); RefreshResults(); };
                fileMenu.Items.Add(open);
                fileMenu.Items.Add(reveal);
                fileMenu.Items.Add(relocate);
                fileMenu.Items.Add(enqueue);
                fileMenu.Items.Add(new Separator());
                fileMenu.Items.Add(favorite);
                fileMenu.Items.Add(delete);
                fileCard.ContextMenu = fileMenu;

                StackPanel fileActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Button moreButton = CreateCardIconButton("\u2022\u2022\u2022", "更多");
                moreButton.Content = new TextBlock
                {
                    Text = "\u2022\u2022\u2022", FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
                    FontWeight = FontWeights.SemiBold, Foreground = Brush("#FF3F5F7F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                moreButton.ToolTip = "更多";
                moreButton.Click += delegate(object sender, RoutedEventArgs e)
                {
                    fileMenu.PlacementTarget = moreButton;
                    fileMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    fileMenu.IsOpen = true;
                    e.Handled = true;
                };
                Button fileDelete = CreateCardIconButton("删", "删除本条记录");
                fileDelete.Content = new TextBlock
                {
                    Text = "删", FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                    FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFD9534F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                fileDelete.ToolTip = "删除本条记录";
                fileDelete.Click += delegate { _store.Remove(item); RefreshResults(); };
                Button pinButton = CreateCardIconButton(item.IsFavorite ? "\uE77A" : "\uE718", item.IsFavorite ? "取消收藏" : "收藏");
                pinButton.Click += delegate { _store.ToggleFavorite(item); RefreshResults(); };
                fileActions.Children.Add(moreButton);
                fileActions.Children.Add(fileDelete);
                fileActions.Children.Add(pinButton);
                Grid.SetColumn(fileActions, 2);
                fileCard.Children.Add(fileActions);

                object fileToolTip = string.Join("\n", paths);
                if (ClipboardItem.IsImageFilePath(existingPath))
                {
                    fileToolTip = BuildImageToolTip(LoadThumbnailBitmap(existingPath, 380)) ?? fileToolTip;
                }
                return new ListBoxItem { Tag = item, Content = fileCard, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = fileToolTip };
            }

            if (item.IsImage)
            {
                Grid imageCard = new Grid { Height = 44 };
                imageCard.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                imageCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                double aspect = item.ImageHeight > 0 ? (double)item.ImageWidth / item.ImageHeight : 1.0;
                double thumbnailWidth = Math.Max(38, Math.Min(280, 38 * aspect));

                Border imageFrame = new Border
                {
                    Width = thumbnailWidth,
                    Height = 38,
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(5),
                    ClipToBounds = true,
                    Margin = new Thickness(0, 3, 12, 3),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                BitmapSource source = item.Thumbnail;
                if (source != null)
                {
                    imageFrame.Child = new Image
                    {
                        Source = source,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                }
                else
                {
                    imageFrame.Child = new TextBlock
                    {
                        Text = "图片无法读取",
                        Foreground = Brush("#FF777777"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                imageCard.Children.Add(imageFrame);

                ContextMenu imageMenu = new ContextMenu();
                MenuItem enqueue = new MenuItem { Header = "加入连续复制队列" };
                MenuItem favorite = new MenuItem { Header = item.IsFavorite ? "取消收藏" : "收藏" };
                MenuItem delete = new MenuItem { Header = "删除" };
                enqueue.Click += delegate { AddToPasteQueue(PasteQueueEntry.FromItem(item, false)); };
                favorite.Click += delegate { _store.ToggleFavorite(item); RefreshResults(); };
                delete.Click += delegate { _store.Remove(item); RefreshResults(); };
                imageMenu.Items.Add(enqueue);
                imageMenu.Items.Add(new Separator());
                imageMenu.Items.Add(favorite);
                imageMenu.Items.Add(delete);
                imageCard.ContextMenu = imageMenu;

                StackPanel imageActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Button moreButton = CreateCardIconButton("\u2022\u2022\u2022", "更多");
                moreButton.Content = new TextBlock
                {
                    Text = "\u2022\u2022\u2022", FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
                    FontWeight = FontWeights.SemiBold, Foreground = Brush("#FF3F5F7F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                moreButton.ToolTip = "更多";
                moreButton.Click += delegate(object sender, RoutedEventArgs e)
                {
                    imageMenu.PlacementTarget = moreButton;
                    imageMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    imageMenu.IsOpen = true;
                    e.Handled = true;
                };
                Button imageDelete = CreateCardIconButton("删", "删除本条记录");
                imageDelete.Content = new TextBlock
                {
                    Text = "删", FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                    FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFD9534F"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                imageDelete.ToolTip = "删除本条记录";
                imageDelete.Click += delegate { _store.Remove(item); RefreshResults(); };
                Button pinButton = CreateCardIconButton(item.IsFavorite ? "\uE77A" : "\uE718", item.IsFavorite ? "取消收藏" : "收藏");
                pinButton.Click += delegate { _store.ToggleFavorite(item); RefreshResults(); };
                imageActions.Children.Add(moreButton);
                imageActions.Children.Add(imageDelete);
                imageActions.Children.Add(pinButton);
                Grid.SetColumn(imageActions, 1);
                imageCard.Children.Add(imageActions);

                return new ListBoxItem
                {
                    Tag = item,
                    Content = imageCard,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    ToolTip = BuildImageToolTip(LoadThumbnailBitmap(item.ImagePath, 380)) ?? item.Preview
                };
            }

            Grid textCard = new Grid { Height = 44 };
            textCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            textCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            StackPanel stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = item.DisplayPreview,
                FontSize = 15,
                Foreground = Brush("#FF1A1A1A"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Detail,
                FontSize = 12,
                Foreground = Brush("#FF6E6E6E"),
                Margin = new Thickness(0, 5, 0, 0)
            });
            textCard.Children.Add(stack);

            StackPanel textActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button tablePaste = CreateCardIconButton("T", "表格预览");
            tablePaste.Content = new TextBlock
            {
                Text = "▦", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 15,
                Foreground = Brush("#FF3F5F7F"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tablePaste.ToolTip = "多行文字转表格";
            tablePaste.Visibility = HasMultipleLines(item.Text) ? Visibility.Visible : Visibility.Collapsed;
            tablePaste.Click += delegate(object sender, RoutedEventArgs eventArgs)
            {
                ShowTablePreview(item);
                eventArgs.Handled = true;
            };
            Button enqueueText = CreateCardIconButton("+", "加入连续复制队列");
            enqueueText.Content = new TextBlock
            {
                Text = "+", FontFamily = new FontFamily("Segoe UI"), FontSize = 16,
                Foreground = Brush("#FF3F5F7F"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            enqueueText.ToolTip = "加入连续复制队列";
            enqueueText.Click += delegate(object sender, RoutedEventArgs eventArgs)
            {
                AddToPasteQueue(PasteQueueEntry.FromItem(item, false));
                eventArgs.Handled = true;
            };
            Button textDelete = CreateCardIconButton("删", "删除本条记录");
            textDelete.Content = new TextBlock
            {
                Text = "删", FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFD9534F"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            textDelete.ToolTip = "删除本条记录";
            textDelete.Click += delegate(object sender, RoutedEventArgs eventArgs)
            {
                _store.Remove(item);
                RefreshResults();
                eventArgs.Handled = true;
            };
            Button textPin = CreateCardIconButton(item.IsFavorite ? "\uE77A" : "\uE718",
                item.IsFavorite ? "取消收藏" : "收藏");
            textPin.Click += delegate(object sender, RoutedEventArgs eventArgs)
            {
                _store.ToggleFavorite(item);
                RefreshResults();
                eventArgs.Handled = true;
            };
            textActions.Children.Add(tablePaste);
            textActions.Children.Add(enqueueText);
            textActions.Children.Add(textDelete);
            textActions.Children.Add(textPin);
            Grid.SetColumn(textActions, 1);
            textCard.Children.Add(textActions);

            return new ListBoxItem
            {
                Tag = item,
                Content = textCard,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = item.Text
            };
        }

        private static BitmapSource LoadFileIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    source.Freeze();
                    return source;
                }
            }
            catch { return null; }
        }

        private static string FirstExistingPath(IEnumerable<string> paths)
        {
            if (paths == null) return string.Empty;
            foreach (string path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path)) return path;
            }
            return string.Empty;
        }

        private void RelocateInvalidPath(ClipboardItem item)
        {
            if (item == null) return;
            string missingPath = null;
            foreach (string path in item.FilePaths)
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    missingPath = path;
                    break;
                }
            }
            if (string.IsNullOrEmpty(missingPath)) return;

            string replacement = null;
            bool looksLikeFolder = string.IsNullOrEmpty(Path.GetExtension(missingPath));
            _childDialogDepth++;
            try
            {
                if (looksLikeFolder)
                {
                    using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        dialog.Description = "为“" + Path.GetFileName(missingPath) + "”选择新的文件夹位置";
                        dialog.ShowNewFolderButton = false;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) replacement = dialog.SelectedPath;
                    }
                }
                else
                {
                    using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog())
                    {
                        dialog.Title = "重新定位 “" + Path.GetFileName(missingPath) + "”";
                        dialog.FileName = Path.GetFileName(missingPath);
                        dialog.CheckFileExists = true;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) replacement = dialog.FileName;
                    }
                }
            }
            finally
            {
                _childDialogDepth--;
            }
            if (string.IsNullOrEmpty(replacement)) return;

            string oldPath = missingPath;
            _storageQueue.Enqueue(delegate
            {
                _store.ReplaceFilePath(item, oldPath, replacement);
                NotifyStorageChanged("失效路径已更新");
            });
        }

        private static void OpenFilePath(string path)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return;
            try { Process.Start(path); }
            catch { }
        }

        private static void RevealFilePath(string path)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return;
            try { Process.Start("explorer.exe", "/select,\"" + path.Replace("\"", string.Empty) + "\""); }
            catch { }
        }

        private static Button CreateCardIconButton(string glyph, string accessibleName)
        {
            Button button = new Button
            {
                Foreground = Brush("#FF202020"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Width = 28,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = accessibleName
            };
            Canvas heartCanvas = new Canvas { Width = 20, Height = 20 };
            bool favorited = accessibleName.StartsWith("取消", StringComparison.Ordinal);
            System.Windows.Shapes.Path heart = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M12,21.35l-1.45-1.32C5.4,15.36,2,12.28,2,8.5C2,5.42,4.42,3,7.5,3c1.74,0,3.41,0.81,4.5,2.09C13.09,3.81,14.76,3,16.5,3C19.58,3,22,5.42,22,8.5c0,3.78-3.4,6.86-8.55,11.54L12,21.35z"),
                Stroke = favorited ? Brush("#FFE81123") : Brush("#FF202020"),
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = favorited ? Brush("#FFE81123") : Brushes.Transparent,
                Stretch = Stretch.Uniform,
                Width = 16,
                Height = 16
            };
            Canvas.SetLeft(heart, 2);
            Canvas.SetTop(heart, 2);
            heartCanvas.Children.Add(heart);
            button.Content = heartCanvas;
            return button;
        }

        private static Button CreatePlainButton(string text)
        {
            return new Button
            {
                Content = text,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = Brush("#FF202020"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand
            };
        }

        private static Border CreateTopTab(string kind, bool selected)
        {
            Button button = CreatePlainButton(string.Empty);
            button.Content = CreateTopTabIcon(kind);
            button.Width = 40;
            button.Height = 38;
            Border tab = new Border
            {
                BorderBrush = selected ? Brush("#FF0078D4") : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, selected ? 3 : 0),
                Margin = new Thickness(0, 0, 2, 0),
                Child = button
            };
            return tab;
        }

        private static UIElement CreateTopTabIcon(string kind)
        {
            if (kind == "gif")
            {
                return new Border
                {
                    BorderBrush = Brush("#FF202020"),
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock { Text = "GIF", FontSize = 9, FontWeight = FontWeights.SemiBold }
                };
            }
            if (kind == "kaomoji")
                return new TextBlock { Text = ";-)", FontSize = 17, Foreground = Brush("#FF202020") };
            if (kind == "symbols")
                return new TextBlock { Text = "%↻\n△+", FontSize = 9, LineHeight = 9, TextAlignment = TextAlignment.Center, Foreground = Brush("#FF202020") };

            Canvas canvas = new Canvas { Width = 22, Height = 22 };
            if (kind == "smile")
            {
                canvas.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 18, Height = 18, Stroke = Brush("#FF202020"), StrokeThickness = 1.4
                });
                canvas.Children.Add(CreateDot(5, 6));
                canvas.Children.Add(CreateDot(11, 6));
                System.Windows.Shapes.Path smile = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 5,11 C 7,15 11,15 13,11"),
                    Stroke = Brush("#FF202020"), StrokeThickness = 1.2, Fill = Brushes.Transparent
                };
                canvas.Children.Add(smile);
                return canvas;
            }

            System.Windows.Shapes.Path clipboard = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 6,5 L 16,5 L 16,19 L 6,19 Z M 9,3 L 13,3 L 14,6 L 8,6 Z"),
                Stroke = Brush("#FF202020"), StrokeThickness = 1.3, Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(clipboard);
            if (kind == "heart")
            {
                System.Windows.Shapes.Path heart = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 11,15 C 9,13 7,12 7,9 C 7,6 11,6 11,9 C 11,6 15,6 15,9 C 15,12 13,13 11,15 Z"),
                    Fill = Brush("#FF202020"), Stroke = Brush("#FF202020"), StrokeThickness = 0.6
                };
                canvas.Children.Add(heart);
            }
            else
            {
                for (int index = 0; index < 3; index++)
                {
                    System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                    {
                        X1 = 9, X2 = 14, Y1 = 10 + index * 3, Y2 = 10 + index * 3,
                        Stroke = Brush("#FF202020"), StrokeThickness = 1
                    };
                    canvas.Children.Add(line);
                }
            }
            return canvas;
        }

        private static System.Windows.Shapes.Ellipse CreateDot(double left, double top)
        {
            System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse
            {
                Width = 2, Height = 2, Fill = Brush("#FF202020")
            };
            Canvas.SetLeft(dot, left);
            Canvas.SetTop(dot, top);
            return dot;
        }

        private static Style CreateThinScrollBarStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='4'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type ScrollBar}'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
                 Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                 Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='{x:Type Thumb}'>
                    <Border Background='#8A8A8A' CornerRadius='2'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is Button) return true;
                try { current = VisualTreeHelper.GetParent(current); }
                catch (InvalidOperationException) { current = LogicalTreeHelper.GetParent(current); }
            }
            return false;
        }

        private void BeginManualWindowDrag(MouseButtonEventArgs eventArgs)
        {
            _isWindowDragging = true;
            _dragStartCursor = System.Windows.Forms.Cursor.Position;
            _dragStartLeft = Left;
            _dragStartTop = Top;
            Mouse.Capture(this);
            eventArgs.Handled = true;
        }

        private void WindowDragMouseMove(object sender, MouseEventArgs eventArgs)
        {
            if (!_isWindowDragging) return;
            if (eventArgs.LeftButton != MouseButtonState.Pressed)
            {
                EndManualWindowDrag();
                return;
            }

            System.Drawing.Point current = System.Windows.Forms.Cursor.Position;
            Vector physicalDelta = new Vector(current.X - _dragStartCursor.X, current.Y - _dragStartCursor.Y);
            PresentationSource source = PresentationSource.FromVisual(this);
            Vector logicalDelta = source != null && source.CompositionTarget != null
                ? source.CompositionTarget.TransformFromDevice.Transform(physicalDelta)
                : physicalDelta;
            Left = _dragStartLeft + logicalDelta.X;
            Top = _dragStartTop + logicalDelta.Y;
        }

        private void WindowDragMouseUp(object sender, MouseButtonEventArgs eventArgs)
        {
            EndManualWindowDrag();
        }

        private void EndManualWindowDrag()
        {
            if (!_isWindowDragging) return;
            _isWindowDragging = false;
            if (Mouse.Captured == this) Mouse.Capture(null);
        }

        private static Style CreateItemContainerStyle()
        {
            Style style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 7)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#FFE1E1E1")));

            FrameworkElementFactory card = new FrameworkElementFactory(typeof(Border));
            card.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            card.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            card.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            card.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            card.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            card.SetValue(Border.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 7,
                ShadowDepth = 2,
                Opacity = 0.16,
                Color = Colors.Black
            });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            card.AppendChild(presenter);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = card }));

            style.Triggers.Add(new Trigger
            {
                Property = ListBoxItem.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brush("#FFF2F2F2")),
                    new Setter(Control.BorderBrushProperty, Brush("#FFD5D5D5"))
                }
            });
            style.Triggers.Add(new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.White),
                    new Setter(Control.BorderBrushProperty, Brush("#FF202020"))
                }
            });
            return style;
        }

        private static Style CreateRoundedSearchBoxStyle()
        {
            Style style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#FFD1D1D1")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "SearchBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory contentGrid = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory placeholder = new FrameworkElementFactory(typeof(TextBlock));
            placeholder.Name = "SearchPlaceholder";
            placeholder.SetValue(TextBlock.TextProperty, "搜索剪贴板");
            placeholder.SetValue(TextBlock.ForegroundProperty, Brush("#FF8A8A8A"));
            placeholder.SetValue(TextBlock.FontSizeProperty, 15.0);
            placeholder.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            placeholder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            placeholder.SetValue(UIElement.IsHitTestVisibleProperty, false);
            placeholder.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            contentGrid.AppendChild(placeholder);

            FrameworkElementFactory contentHost = new FrameworkElementFactory(typeof(ScrollViewer));
            contentHost.Name = "PART_ContentHost";
            contentHost.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            contentHost.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentGrid.AppendChild(contentHost);
            border.AppendChild(contentGrid);

            ControlTemplate template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
            Trigger focused = new Trigger
            {
                Property = UIElement.IsKeyboardFocusedProperty,
                Value = true
            };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#FF0078D4"), "SearchBorder"));
            template.Triggers.Add(focused);
            Trigger empty = new Trigger
            {
                Property = TextBox.TextProperty,
                Value = string.Empty
            };
            empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SearchPlaceholder"));
            template.Triggers.Add(empty);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
    }
}
