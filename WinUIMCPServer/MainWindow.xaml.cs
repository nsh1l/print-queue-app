using System.Diagnostics;
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
    private readonly DispatcherTimer _healthTimer;
    private bool _isSubmitting;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AYP Print-Queue";
        AppWindow.Resize(new SizeInt32(1100, 760));
        Closed += (_, _) => SaveQueue();
        _queue.AddRange(QueuePersistence.Load());
        LoadPrinters();
        RefreshQueue("前回のキューを読み込みました。再送は手動で実行してください");

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _healthTimer.Tick += (_, _) => RefreshPrinterHealth();
        _healthTimer.Start();
        RefreshPrinterHealth();
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
        catch (Exception exception) { SetStatus($"ファイル選択に失敗しました: {exception.Message}"); }
    }

    private void OnDragOver(object sender, DragEventArgs eventArgs)
    {
        if (_isSubmitting) return;
        if (eventArgs.DataView.Contains(StandardDataFormats.StorageItems))
            eventArgs.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void OnDrop(object sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var files = (await eventArgs.DataView.GetStorageItemsAsync()).OfType<StorageFile>();
            AddFiles(files.Select(file => file.Path));
        }
        catch (Exception exception) { SetStatus($"ドロップしたファイルを読み取れませんでした: {exception.Message}"); }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        if (_isSubmitting) return;
        var added = 0;
        var ignored = 0;
        var duplicate = 0;
        var knownPaths = _queue.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            var path = System.IO.Path.GetFullPath(rawPath);
            if (!QueueItem.IsSupported(path)) { ignored++; continue; }
            if (!knownPaths.Add(path)) { duplicate++; continue; }
            _queue.Add(new QueueItem(path));
            added++;
            RecordHistory($"追加: {path}");
        }

        var details = new List<string>();
        if (added > 0) details.Add($"{added}件追加");
        if (duplicate > 0) details.Add($"重複{duplicate}件を除外");
        if (ignored > 0) details.Add($"未対応形式{ignored}件を除外");
        RefreshQueue(details.Count == 0 ? "追加できるファイルがありません" : string.Join("、", details));
    }

    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        var selected = QueueList.SelectedItems.Cast<QueueItem>().ToList();
        RemoveButton.IsEnabled = !_isSubmitting && selected.Count > 0;
        MoveUpButton.IsEnabled = !_isSubmitting && selected.Count == 1 && _queue.IndexOf(selected[0]) > 0;
        MoveDownButton.IsEnabled = !_isSubmitting && selected.Count == 1 && _queue.IndexOf(selected[0]) < _queue.Count - 1;
        if (!_isSubmitting) UpdateSelectionButtons();
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs eventArgs) => MoveSelected(-1);
    private void OnMoveDownClick(object sender, RoutedEventArgs eventArgs) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        if (_isSubmitting || QueueList.SelectedItems.Count != 1) return;
        var item = QueueList.SelectedItems.Cast<QueueItem>().Single();
        var oldIndex = _queue.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= _queue.Count) return;
        _queue.RemoveAt(oldIndex);
        _queue.Insert(newIndex, item);
        RecordHistory($"順番変更: {item.Name}");
        RefreshQueue("キューの順番を変更しました");
        QueueList.SelectedItem = item;
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isSubmitting) return;
        var selected = QueueList.SelectedItems.Cast<QueueItem>().ToHashSet();
        _queue.RemoveAll(item => selected.Contains(item));
        if (selected.Count > 0) RecordHistory($"削除: {selected.Count}件");
        RefreshQueue(selected.Count > 0 ? $"{selected.Count}件削除しました" : null);
    }

    private void OnClearClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isSubmitting || _queue.Count == 0) return;
        _queue.Clear();
        RecordHistory("キューをクリア");
        RefreshQueue("キューをクリアしました");
    }

    private async void OnPrintAllClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(_queue.Where(item => item.Status == QueueStatus.Pending).ToList(), "送信");

    private async void OnResendClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(_queue.Where(item => item.Status == QueueStatus.Submitted).ToList(), "再送");

    private async void OnRetryErrorsClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(_queue.Where(item => item.Status == QueueStatus.Error).ToList(), "エラー再送", true);

    private async void OnSendSelectedClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(SelectedItems(item => item.Status == QueueStatus.Pending), "選択送信");

    private async void OnResendSelectedClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(SelectedItems(item => item.Status == QueueStatus.Submitted), "選択再送");

    private async void OnRetrySelectedErrorsClick(object sender, RoutedEventArgs eventArgs)
        => await SubmitBatchAsync(SelectedItems(item => item.Status == QueueStatus.Error), "選択エラー再送", true);

    private IReadOnlyList<QueueItem> SelectedItems(Func<QueueItem, bool> predicate)
        => QueueList.SelectedItems.Cast<QueueItem>().Where(predicate).ToList();

    private async Task SubmitBatchAsync(IReadOnlyList<QueueItem> items, string action, bool resetErrors = false)
    {
        if (PrinterComboBox.SelectedItem is not string printerName)
        {
            SetStatus("プリンターを選択してください");
            return;
        }
        if (items.Count == 0) return;
        if (!await ConfirmChangedFilesAsync(items)) return;

        var dialog = new ContentDialog
        {
            Title = $"{items.Count}件を{action}しますか？",
            Content = $"送信先: {printerName}\n\n{string.Join("\n", items.Select(item => $"・{item.Name}"))}\n\n「送信済み」はWindowsへ要求を渡した状態です。",
            PrimaryButtonText = action,
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!await ConfirmChangedFilesAsync(items)) return;

        if (resetErrors)
            foreach (var item in items)
                item.ResetForRetry();

        _isSubmitting = true;
        SetControlsEnabled(false);
        SetStatus($"{items.Count}件を{printerName}へ{action}中");
        try
        {
            await QueueBatch.SubmitAsync(items, printerName, PreserveOrderCheckBox.IsChecked == true);
            RecordHistory($"{action}: {items.Count}件 → {printerName}");
        }
        catch (Exception exception) { SetStatus($"印刷送信に失敗しました: {exception.Message}"); }
        finally
        {
            _isSubmitting = false;
            SetControlsEnabled(true);
            SaveQueue();
            RefreshQueue();
        }
    }

    private async Task<bool> ConfirmChangedFilesAsync(IReadOnlyList<QueueItem> items)
    {
        var warnings = items
            .Select(item => (item.Name, Warning: item.GetFileChangeWarning()))
            .Where(item => item.Warning is not null)
            .Select(item => $"・{item.Name}: {item.Warning}")
            .ToList();
        if (warnings.Count == 0) return true;

        var dialog = new ContentDialog
        {
            Title = "ファイルの状態を確認してください",
            Content = string.Join("\n", warnings),
            PrimaryButtonText = "そのまま続行",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetControlsEnabled(bool enabled)
    {
        ChooseFilesButton.IsEnabled = enabled;
        PrinterComboBox.IsEnabled = enabled;
        RefreshPrintersButton.IsEnabled = enabled;
        PreserveOrderCheckBox.IsEnabled = enabled;
        QueueSurface.AllowDrop = enabled;
        PrintButton.IsEnabled = enabled && _queue.Any(item => item.Status == QueueStatus.Pending) && PrinterComboBox.SelectedItem is string;
        ResendButton.IsEnabled = enabled && _queue.Any(item => item.Status == QueueStatus.Submitted) && PrinterComboBox.SelectedItem is string;
        RetryErrorsButton.IsEnabled = enabled && _queue.Any(item => item.Status == QueueStatus.Error) && PrinterComboBox.SelectedItem is string;
        RemoveButton.IsEnabled = false;
        MoveUpButton.IsEnabled = false;
        MoveDownButton.IsEnabled = false;
        SendSelectedButton.IsEnabled = false;
        ResendSelectedButton.IsEnabled = false;
        RetrySelectedErrorsButton.IsEnabled = false;
        ClearButton.IsEnabled = enabled && _queue.Count > 0;
        if (enabled) UpdateSelectionButtons();
    }

    private void UpdateSelectionButtons()
    {
        var selected = QueueList.SelectedItems.Cast<QueueItem>().ToList();
        SendSelectedButton.IsEnabled = selected.Any(item => item.Status == QueueStatus.Pending);
        ResendSelectedButton.IsEnabled = selected.Any(item => item.Status == QueueStatus.Submitted);
        RetrySelectedErrorsButton.IsEnabled = selected.Any(item => item.Status == QueueStatus.Error);
    }

    private void OnPrinterSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_isSubmitting) RefreshQueue();
        RefreshPrinterHealth();
    }

    private void OnRefreshPrintersClick(object sender, RoutedEventArgs eventArgs) => LoadPrinters();

    private void LoadPrinters()
    {
        try
        {
            var previous = PrinterComboBox.SelectedItem as string;
            var printers = PrinterCatalog.GetInstalledPrinterNames();
            PrinterComboBox.ItemsSource = printers;
            PrinterComboBox.SelectedItem = printers.FirstOrDefault(name => string.Equals(name, previous, StringComparison.CurrentCultureIgnoreCase))
                ?? printers.FirstOrDefault(name => string.Equals(name, PrinterCatalog.GetDefaultPrinterName(), StringComparison.CurrentCultureIgnoreCase))
                ?? printers.FirstOrDefault();
            RefreshQueue(printers.Count == 0 ? "利用できるプリンターが見つかりません" : "プリンター一覧を更新しました");
            RecordHistory("プリンター一覧を更新");
        }
        catch (Exception exception)
        {
            PrinterComboBox.IsEnabled = false;
            RefreshQueue($"プリンター一覧を取得できませんでした: {exception.Message}");
        }
    }

    private void RefreshPrinterHealth()
    {
        if (PrinterComboBox.SelectedItem is not string printerName)
        {
            PrinterHealthText.Text = "プリンター未選択";
            return;
        }
        try
        {
            var health = PrinterCatalog.GetHealth(printerName);
            PrinterHealthText.Text = $"{health.StatusLabel}・スプーラー {health.JobCount}件";
        }
        catch (Exception exception) { PrinterHealthText.Text = $"状態取得不可: {exception.Message}"; }
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
        SaveQueue();
        SetControlsEnabled(!_isSubmitting);
    }

    private void SaveQueue()
    {
        try { QueuePersistence.Save(_queue); }
        catch (IOException exception) { SetStatus($"キューを保存できませんでした: {exception.Message}"); }
        catch (UnauthorizedAccessException exception) { SetStatus($"キューを保存できませんでした: {exception.Message}"); }
    }

    private static void RecordHistory(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AYP", "PrintQueue", "history.log");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}\t{message}{Environment.NewLine}");
        }
        catch (IOException) { /* History is diagnostic; it must not block printing. */ }
    }

    private void OnOpenHistoryClick(object sender, RoutedEventArgs eventArgs)
    {
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AYP", "PrintQueue", "history.log");
        if (!File.Exists(path)) RecordHistory("履歴を開く");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        if (StatusText.XamlRoot is null) return;
        var peer = FrameworkElementAutomationPeer.FromElement(StatusText) ?? FrameworkElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
