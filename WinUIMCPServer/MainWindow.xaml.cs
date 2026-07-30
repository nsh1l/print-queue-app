using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PrintQueueApp.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly List<QueueItem> _queue = [];

    public MainWindow()
    {
        InitializeComponent();
        Title = "印刷キュー";
        AppWindow.Resize(new SizeInt32(1000, 700));
        LoadPrinters();
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
            SetStatus($"ファイル選択に失敗しました: {exception.Message}");
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
            SetStatus($"ドロップしたファイルを読み取れませんでした: {exception.Message}");
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

    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
        => RemoveButton.IsEnabled = QueueList.SelectedItems.Count > 0;

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs eventArgs)
    {
        var selected = QueueList.SelectedItems.Cast<QueueItem>().ToHashSet();
        _queue.RemoveAll(item => selected.Contains(item));
        RefreshQueue(selected.Count > 0 ? $"{selected.Count}件削除しました" : null);
    }

    private void OnClearClick(object sender, RoutedEventArgs eventArgs)
    {
        _queue.Clear();
        RefreshQueue("キューをクリアしました");
    }

    private void OnPrintAllClick(object sender, RoutedEventArgs eventArgs)
    {
        if (PrinterComboBox.SelectedItem is not string printerName)
        {
            SetStatus("プリンターを選択してください");
            return;
        }

        foreach (var item in _queue.Where(item => item.Status == QueueStatus.Pending))
            item.SubmitToPrinter(printerName);

        RefreshQueue();
    }

    private void OnPrinterSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        PrintButton.IsEnabled = PrinterComboBox.SelectedItem is string
            && _queue.Any(item => item.Status == QueueStatus.Pending);
    }

    private void LoadPrinters()
    {
        try
        {
            var printers = PrinterCatalog.GetInstalledPrinterNames();
            PrinterComboBox.ItemsSource = printers;
            var defaultPrinter = PrinterCatalog.GetDefaultPrinterName();
            PrinterComboBox.SelectedItem = printers.FirstOrDefault(name =>
                string.Equals(name, defaultPrinter, StringComparison.CurrentCultureIgnoreCase))
                ?? printers.FirstOrDefault();
            RefreshQueue(printers.Count == 0 ? "利用できるプリンターが見つかりません" : null);
        }
        catch (Exception exception)
        {
            PrinterComboBox.IsEnabled = false;
            RefreshQueue($"プリンター一覧を取得できませんでした: {exception.Message}");
        }
    }

    private void RefreshQueue(string? message = null)
    {
        QueueList.ItemsSource = null;
        QueueList.ItemsSource = _queue;

        var pending = _queue.Count(item => item.Status == QueueStatus.Pending);
        var submitted = _queue.Count(item => item.Status == QueueStatus.Submitted);
        var errors = _queue.Count(item => item.Status == QueueStatus.Error);
        QueueCountText.Text = $"{_queue.Count} ファイル";
        SetStatus(message ?? $"待機 {pending}  ・  送信済み {submitted}  ・  エラー {errors}");
        EmptyState.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueList.Visibility = _queue.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveButton.IsEnabled = false;
        ClearButton.IsEnabled = _queue.Count > 0;
        PrintButton.IsEnabled = pending > 0 && PrinterComboBox.SelectedItem is string;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        if (StatusText.XamlRoot is null)
            return;

        var peer = FrameworkElementAutomationPeer.FromElement(StatusText)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
