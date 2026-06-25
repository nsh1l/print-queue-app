using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace PrintQueueApp.WinUI
{
    public sealed partial class MainWindow : Window
    {
        // ── State ──────────────────────────────────────────────────────
        private readonly Dictionary<string, UIElement> _elements = new();
        private readonly Dictionary<string, string> _callbacks = new();
        private readonly List<Dictionary<string, object>> _pendingEvents = new();
        private ObservableCollection<FileListItem> _fileList = new();

        // UI elements (set in BuildUI)
        private ListView? _fileListView;
        private TextBlock? _statusText;
        private ProgressBar? _progressBar;
        private TextBlock? _progressLabel;
        private DropZone? _dropZone;

        // ── Data Models ────────────────────────────────────────────────

        public class FileListItem
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Size { get; set; } = "";
            public string Status { get; set; } = "PENDING";
            public bool IsSelected { get; set; }
            public string? Error { get; set; }

            public string StatusIcon => Status switch
            {
                "PENDING" => "☐",
                "PROCESSING" => "⚙",
                "PRINTING" => "🖨",
                "DONE" => "✅",
                "ERROR" => "❌",
                _ => "☐"
            };
        }

        // ── MCPServer delegate interface ───────────────────────────────

        public IAsyncOperation<Dictionary<string, object>> CreateWindowAsync(
            string title, int width, int height)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                Title = title;
                Width = width;
                Height = height;
                BuildUI();
                Activate();
                return new Dictionary<string, object> { ["window_id"] = "main" };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> SetWindowLayoutAsync(
            string windowId, JsonObject? layout)
        {
            return Task.FromResult(new Dictionary<string, object> { ["ok"] = true }).AsAsyncOperation();
        }

        public IAsyncOperation<Dictionary<string, object>> AddDropZoneAsync(
            string windowId, string zoneId, string label)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                // Already added in BuildUI; update label if needed
                if (_dropZone != null)
                    _dropZone.Description = label;
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> UpdateFileListAsync(
            string windowId, JsonArray? files)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                _fileList.Clear();
                if (files != null)
                {
                    foreach (var node in files)
                    {
                        var obj = node as JsonObject;
                        if (obj == null) continue;

                        _fileList.Add(new FileListItem
                        {
                            Id = obj.TryGetPropertyValue("id", out var id) ? id?.GetValue<string>() ?? "" : "",
                            Name = obj.TryGetPropertyValue("name", out var n) ? n?.GetValue<string>() ?? "" : "",
                            Size = obj.TryGetPropertyValue("size", out var s) ? s?.GetValue<string>() ?? "" : "",
                            Status = obj.TryGetPropertyValue("status", out var st) ? st?.GetValue<string>() ?? "PENDING" : "PENDING",
                            IsSelected = obj.TryGetPropertyValue("selected", out var sel) ? sel?.GetValue<bool>() ?? false : false,
                            Error = obj.TryGetPropertyValue("error", out var err) ? err?.GetValue<string>() : null,
                        });
                    }
                }
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> AddButtonAsync(
            string windowId, string buttonId, string label, string icon)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                // Buttons are in BuildUI; button clicks fire events
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> AddLabelAsync(
            string windowId, string labelId, string text)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                _elements.TryGetValue(labelId, out var el);
                if (el is TextBlock tb)
                    tb.Text = text;
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> SetStatusTextAsync(
            string windowId, string text)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                if (_statusText != null)
                    _statusText.Text = text;
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> SetProgressAsync(
            string windowId, int percent, string label)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                if (_progressBar != null)
                    _progressBar.Value = percent;
                if (_progressLabel != null)
                    _progressLabel.Text = label;
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> RegisterCallbackAsync(
            string windowId, string eventType, string callbackId)
        {
            _callbacks[eventType] = callbackId;
            return Task.FromResult(new Dictionary<string, object> { ["ok"] = true }).AsAsyncOperation();
        }

        public IAsyncOperation<Dictionary<string, object>> PollEventsAsync(string windowId)
        {
            var events = _pendingEvents.ToList();
            _pendingEvents.Clear();
            return Task.FromResult(new Dictionary<string, object> { ["events"] = events }).AsAsyncOperation();
        }

        public IAsyncOperation<Dictionary<string, object>> ClearWindowAsync(string windowId)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                _fileList.Clear();
                _elements.Clear();
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> CloseWindowAsync(string windowId)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                Close();
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public IAsyncOperation<Dictionary<string, object>> ShowWindowAsync(string windowId)
        {
            return DispatcherQueue.EnqueueAsync(() =>
            {
                Activate();
                return new Dictionary<string, object> { ["ok"] = true };
            });
        }

        public Task<Dictionary<string, object>> HandleToolCallAsync(
            string toolName, JsonObject? arguments, global::System.Threading.CancellationToken ct)
        {
            // Route tools/call to individual methods
            var windowId = arguments?.TryGetPropertyValue("window_id", out var wid) == true
                ? wid?.GetValue<string>() ?? "main" : "main";

            var task = toolName switch
            {
                "create_window" => CreateWindowAsync(
                    arguments?.TryGetPropertyValue("title", out var t) == true ? t?.GetValue<string>() ?? "Window" : "Window",
                    arguments?.TryGetPropertyValue("width", out var w) == true ? w?.GetValue<int>() ?? 800 : 800,
                    arguments?.TryGetPropertyValue("height", out var h) == true ? h?.GetValue<int>() ?? 600 : 600),
                "add_drop_zone" => AddDropZoneAsync(windowId,
                    arguments?.TryGetPropertyValue("zone_id", out var dzi) == true ? dzi?.GetValue<string>() ?? "" : "",
                    arguments?.TryGetPropertyValue("label", out var dzlab) == true ? dzlab?.GetValue<string>() ?? "" : ""),
                "update_file_list" => UpdateFileListAsync(windowId,
                    arguments?.TryGetPropertyValue("files", out var fl) == true ? fl as JsonArray : null),
                "add_button" => AddButtonAsync(windowId,
                    arguments?.TryGetPropertyValue("button_id", out var bi) == true ? bi?.GetValue<string>() ?? "" : "",
                    arguments?.TryGetPropertyValue("label", out var bl) == true ? bl?.GetValue<string>() ?? "" : "",
                    arguments?.TryGetPropertyValue("icon", out var bicon) == true ? bicon?.GetValue<string>() ?? "" : ""),
                "add_label" => AddLabelAsync(windowId,
                    arguments?.TryGetPropertyValue("label_id", out var li) == true ? li?.GetValue<string>() ?? "" : "",
                    arguments?.TryGetPropertyValue("text", out var lt) == true ? lt?.GetValue<string>() ?? "" : ""),
                "set_status_text" => SetStatusTextAsync(windowId,
                    arguments?.TryGetPropertyValue("text", out var stt) == true ? stt?.GetValue<string>() ?? "" : ""),
                "set_progress" => SetProgressAsync(windowId,
                    arguments?.TryGetPropertyValue("percent", out var pc) == true ? pc?.GetValue<int>() ?? 0 : 0,
                    arguments?.TryGetPropertyValue("label", out var pcl) == true ? pcl?.GetValue<string>() ?? "" : ""),
                "register_callback" => RegisterCallbackAsync(windowId,
                    arguments?.TryGetPropertyValue("event_type", out var cbe) == true ? cbe?.GetValue<string>() ?? "" : "",
                    arguments?.TryGetPropertyValue("callback_id", out var cbid) == true ? cbid?.GetValue<string>() ?? "" : ""),
                "poll_events" => PollEventsAsync(windowId),
                "clear_window" => ClearWindowAsync(windowId),
                "close_window" => CloseWindowAsync(windowId),
                "show_window" => ShowWindowAsync(windowId),
                _ => Task.FromResult(new Dictionary<string, object> { ["error"] = $"Unknown tool: {toolName}" })
            };

            return task.AsTask();
        }

        public List<Dictionary<string, object>> GetPendingEvents() => _pendingEvents;

        // ── UI Construction ────────────────────────────────────────────

        private void BuildUI()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));

            // Title
            var titleBar = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)) };
            titleBar.Height = 48;
            var titleText = new TextBlock
            {
                Text = Title,
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 79, 195, 247)),
                Margin = new Windows.UI.Xaml.Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleBar.Children.Add(titleText);
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // Drop Zone
            var dropBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 79, 195, 247)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(255, 42, 42, 42)),
                Margin = new Windows.UI.Xaml.Thickness(16, 12, 16, 8),
                Padding = new Thickness(16),
                MinHeight = 80
            };
            var dropPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dropPanel.Children.Add(new TextBlock
            {
                Text = "📂 XLSX / XLS / PDF をここにドロップ",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 79, 195, 247)),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            dropPanel.Children.Add(new TextBlock
            {
                Text = "または",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            });
            var addFileBtn = new Button { Content = "📁 ファイルを選択", Margin = new Thickness(0, 4, 0, 0) };
            addFileBtn.Click += OnAddFilesClick;
            dropPanel.Children.Add(addFileBtn);
            dropBorder.Child = dropPanel;

            _dropZone = new DropZone { Description = "XLSX / XLS / PDF をここにドロップ" };
            _dropZone.FileDrop += OnFileDrop;
            _dropZone.Child = dropBorder;

            Grid.SetRow(_dropZone, 1);
            root.Children.Add(_dropZone);

            // File List
            _fileListView = new ListView
            {
                ItemsSource = _fileList,
                Margin = new Windows.UI.Xaml.Thickness(16, 0, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
            };
            _fileListView.ItemClick += OnFileItemClick;
            _fileListView.SelectionChanged += OnSelectionChanged;

            var listTemplate = new DataTemplate(() =>
            {
                var grid = new Grid { Margin = new Thickness(8, 4, 8, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                var nameBlock = new TextBlock
                {
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameBlock.SetBinding(TextBlock.TextProperty, new Windows.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });
                Grid.SetColumn(nameBlock, 0);

                var sizeBlock = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                sizeBlock.SetBinding(TextBlock.TextProperty, new Windows.UI.Xaml.Data.Binding { Path = new PropertyPath("Size") });
                Grid.SetColumn(sizeBlock, 1);

                var statusBlock = new TextBlock
                {
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                statusBlock.SetBinding(TextBlock.TextProperty, new Windows.UI.Xaml.Data.Binding { Path = new PropertyPath("StatusIcon") });
                Grid.SetColumn(statusBlock, 2);

                grid.Children.Add(nameBlock);
                grid.Children.Add(sizeBlock);
                grid.Children.Add(statusBlock);
                return new ListViewItem { ContentTemplate = () => grid };
            });

            _fileListView.ItemTemplate = listTemplate;
            Grid.SetRow(_fileListView, 2);
            root.Children.Add(_fileListView);

            // Progress
            var progressPanel = new StackPanel { Margin = new Windows.UI.Xaml.Thickness(16, 0, 16, 8) };
            _progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8 };
            _progressLabel = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            progressPanel.Children.Add(_progressBar);
            progressPanel.Children.Add(_progressLabel);
            Grid.SetRow(progressPanel, 3);
            root.Children.Add(progressPanel);

            // Status bar
            var statusBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
                Padding = new Thickness(16, 6, 16, 6)
            };
            _statusText = new TextBlock
            {
                Text = "待機中 (0件)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 129, 199, 132))
            };
            statusBar.Child = _statusText;
            Grid.SetRow(statusBar, 4);
            root.Children.Add(statusBar);

            // Button bar
            var buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Windows.UI.Xaml.Thickness(16, 8, 16, 12),
                Spacing = 8
            };

            var btnSelectAll = MakeButton("☑ 全選択", "btn_select_all");
            btnSelectAll.Click += (s, e) => _pendingEvents.Add(new() { ["type"] = "button_click", ["data"] = new Dictionary<string, object> { ["button_id"] = "btn_select_all" } });

            var btnClear = MakeButton("🗑 キューをクリア", "btn_clear_queue");
            btnClear.Click += (s, e) => _pendingEvents.Add(new() { ["type"] = "button_click", ["data"] = new Dictionary<string, object> { ["button_id"] = "btn_clear_queue" } });

            var btnPrint = MakeButton("🖨 全印刷", "btn_print_all");
            btnPrint.Click += (s, e) => _pendingEvents.Add(new() { ["type"] = "button_click", ["data"] = new Dictionary<string, object> { ["button_id"] = "btn_print_all" } });

            buttonBar.Children.Add(btnSelectAll);
            buttonBar.Children.Add(btnClear);
            buttonBar.Children.Add(btnPrint);
            Grid.SetRow(buttonBar, 5);
            root.Children.Add(buttonBar);

            Content = root;
        }

        private Button MakeButton(string label, string id)
        {
            return new Button
            {
                Content = label,
                Tag = id,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        // ── Event Handlers ─────────────────────────────────────────────

        private async void OnAddFilesClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");
            picker.FileTypeFilter.Add(".pdf");
            picker.ViewMode = PickerViewMode.List);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files?.Count > 0)
            {
                var paths = files.Select(f => f.Path).ToList();
                _pendingEvents.Add(new Dictionary<string, object>
                {
                    ["type"] = "file_drop",
                    ["data"] = new Dictionary<string, object> { ["paths"] = paths }
                });
            }
        }

        private void OnFileDrop(object? sender, FileDropEventArgs e)
        {
            var paths = e.Paths.ToList();
            if (paths.Count > 0)
            {
                _pendingEvents.Add(new Dictionary<string, object>
                {
                    ["type"] = "file_drop",
                    ["data"] = new Dictionary<string, object> { ["paths"] = paths }
                });
            }
        }

        private void OnFileItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FileListItem item)
            {
                _pendingEvents.Add(new Dictionary<string, object>
                {
                    ["type"] = "file_select",
                    ["data"] = new Dictionary<string, object> { ["id"] = item.Id }
                });
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }

    // ── DropZone Control ───────────────────────────────────────────────

    public class DropZone : Control
    {
        public string Description { get; set; } = "Drop files here";

        public event EventHandler<FileDropEventArgs>? FileDrop;

        public DropZone()
        {
            DefaultStyleKey = typeof(DropZone);
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.OfType<StorageFile>().Select(f => f.Path).ToList();
                FileDrop?.Invoke(this, new FileDropEventArgs(paths));
            }
        }
    }

    public class FileDropEventArgs : EventArgs
    {
        public IStorageItem[] Items { get; }
        public string[] Paths { get; }

        public FileDropEventArgs(string[] paths)
        {
            Paths = paths;
            Items = Array.Empty<IStorageItem>();
        }
    }
}
