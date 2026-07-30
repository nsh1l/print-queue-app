using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PrintQueueApp.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly List<QueueItem> _queue = [];
    private readonly ListView _queueList = new()
    {
        SelectionMode = ListViewSelectionMode.Multiple,
        Margin = new Thickness(16, 0, 16, 8),
    };
    private readonly TextBlock _statusText = new()
    {
        Foreground = new SolidColorBrush(Colors.DimGray),
        Margin = new Thickness(16, 6, 16, 12),
    };

    public MainWindow()
    {
        Title = "印刷キュー管理";
        BuildUi();
        RefreshQueue();
    }

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "📋 印刷キュー管理",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(title);

        var dropZone = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.DodgerBlue),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12),
            AllowDrop = true,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "XLSX / XLS / XLSM / PDF をここにドロップ",
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new Button
                    {
                        Content = "📁 ファイルを選択",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0),
                    },
                },
            },
        };
        dropZone.DragOver += OnDragOver;
        dropZone.Drop += OnDrop;
        ((Button)((StackPanel)dropZone.Child).Children[1]).Click += OnChooseFilesClick;
        Grid.SetRow(dropZone, 1);
        root.Children.Add(dropZone);

        Grid.SetRow(_queueList, 2);
        root.Children.Add(_queueList);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var removeButton = new Button { Content = "🗑 選択を削除" };
        removeButton.Click += OnRemoveSelectedClick;
        var clearButton = new Button { Content = "キューをクリア" };
        clearButton.Click += (_, _) =>
        {
            _queue.Clear();
            RefreshQueue();
        };
        var printButton = new Button { Content = "🖨 既定のプリンターへ送信" };
        printButton.Click += OnPrintAllClick;
        buttonBar.Children.Add(removeButton);
        buttonBar.Children.Add(clearButton);
        buttonBar.Children.Add(printButton);
        Grid.SetRow(buttonBar, 3);
        root.Children.Add(buttonBar);

        Grid.SetRow(_statusText, 4);
        root.Children.Add(_statusText);
        Content = root;
    }

    private async void OnChooseFilesClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");
            picker.FileTypeFilter.Add(".xlsm");
            picker.FileTypeFilter.Add(".pdf");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            var files = await picker.PickMultipleFilesAsync();
            AddFiles(files.Select(file => file.Path));
        }
        catch (Exception exception)
        {
            _statusText.Text = $"ファイル選択に失敗しました: {exception.Message}";
        }
    }

    private void OnDragOver(object sender, DragEventArgs eventArgs)
    {
        if (eventArgs.DataView.Contains(StandardDataFormats.StorageItems))
            eventArgs.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void OnDrop(object sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        try
        {
            var files = (await eventArgs.DataView.GetStorageItemsAsync()).OfType<StorageFile>();
            AddFiles(files.Select(file => file.Path));
        }
        catch (Exception exception)
        {
            _statusText.Text = $"ドロップしたファイルを読み取れませんでした: {exception.Message}";
        }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var ignored = 0;
        foreach (var path in paths)
        {
            if (!QueueItem.IsSupported(path))
            {
                ignored++;
                continue;
            }

            _queue.Add(new QueueItem(path));
            added++;
        }

        RefreshQueue(ignored > 0 ? $"{added}件追加、{ignored}件は未対応形式のため除外" : $"{added}件追加");
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs eventArgs)
    {
        var selected = _queueList.SelectedItems.Cast<QueueItem>().ToHashSet();
        _queue.RemoveAll(item => selected.Contains(item));
        RefreshQueue();
    }

    private void OnPrintAllClick(object sender, RoutedEventArgs eventArgs)
    {
        foreach (var item in _queue.Where(item => item.Status == QueueStatus.Pending))
            item.SubmitToDefaultPrinter();

        RefreshQueue();
    }

    private void RefreshQueue(string? message = null)
    {
        _queueList.Items.Clear();
        foreach (var item in _queue)
            _queueList.Items.Add(item);

        var pending = _queue.Count(item => item.Status == QueueStatus.Pending);
        var submitted = _queue.Count(item => item.Status == QueueStatus.Submitted);
        var errors = _queue.Count(item => item.Status == QueueStatus.Error);
        _statusText.Text = message ?? $"{_queue.Count}件: 待機 {pending} / 送信済み {submitted} / エラー {errors}";
    }
}
