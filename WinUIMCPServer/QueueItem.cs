using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PrintQueueApp.WinUI;

public enum QueueStatus
{
    Pending,
    Submitted,
    Error,
}

public sealed class QueueItem
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".xls", ".xlsm", ".pdf",
    };

    public QueueItem(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Name = System.IO.Path.GetFileName(Path);
        if (File.Exists(Path))
        {
            var fileInfo = new FileInfo(Path);
            FileSize = fileInfo.Length;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
        }
    }

    public string Path { get; }
    public string Name { get; }
    public QueueStatus Status { get; private set; } = QueueStatus.Pending;
    public string Detail { get; private set; } = "待機中";
    public DateTimeOffset? SubmittedAt { get; private set; }
    public string? PrinterName { get; private set; }
    public long? FileSize { get; private set; }
    public DateTime? LastWriteTimeUtc { get; private set; }
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public string ExtensionLabel => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string AccessibleName => $"{Name}、{Detail}";
    public string SubmittedAtLabel => SubmittedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? "—";
    public string StatusLabel => Status switch
    {
        QueueStatus.Submitted => "送信済み",
        QueueStatus.Error => "エラー",
        _ => "待機中",
    };
    public string StatusGlyph => Status switch
    {
        QueueStatus.Submitted => "\uE73E",
        QueueStatus.Error => "\uEA39",
        _ => "\uE121",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(System.IO.Path.GetExtension(path));

    public void SubmitToPrinter(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            SetError("プリンターが選択されていません");
            return;
        }

        if (!File.Exists(Path))
        {
            SetError("ファイルが見つかりません");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(Path)
            {
                UseShellExecute = true,
                Verb = "printto",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add(printerName);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                SetError("印刷コマンドを開始できませんでした");
                return;
            }

            Status = QueueStatus.Submitted;
            PrinterName = printerName;
            SubmittedAt = DateTimeOffset.Now;
            Detail = $"{printerName}へ送信済み（物理印刷は未確認）";
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    public void ResetForRetry()
    {
        Status = QueueStatus.Pending;
        Detail = "待機中";
    }

    public string? GetFileChangeWarning()
    {
        if (!File.Exists(Path)) return "ファイルが見つかりません";
        if (FileSize is null || LastWriteTimeUtc is null) return null;

        var fileInfo = new FileInfo(Path);
        return fileInfo.Length != FileSize || fileInfo.LastWriteTimeUtc != LastWriteTimeUtc
            ? "キュー追加後にファイルが変更されています"
            : null;
    }

    public override string ToString() => $"{Name} — {Detail}";

    public QueueItemSnapshot ToSnapshot() => new(Path, Status, Detail, SubmittedAt, PrinterName, FileSize, LastWriteTimeUtc);

    public static QueueItem FromSnapshot(QueueItemSnapshot snapshot)
    {
        var item = new QueueItem(snapshot.Path)
        {
            Status = snapshot.Status,
            Detail = snapshot.Detail,
            SubmittedAt = snapshot.SubmittedAt,
            PrinterName = snapshot.PrinterName,
        };
        item.FileSize = snapshot.FileSize ?? item.FileSize;
        item.LastWriteTimeUtc = snapshot.LastWriteTimeUtc ?? item.LastWriteTimeUtc;
        return item;
    }

    private void SetError(string message)
    {
        Status = QueueStatus.Error;
        Detail = $"エラー: {message}";
    }
}

public sealed record QueueItemSnapshot(
    string Path,
    QueueStatus Status,
    string Detail,
    DateTimeOffset? SubmittedAt,
    string? PrinterName,
    long? FileSize = null,
    DateTime? LastWriteTimeUtc = null);

internal static class QueuePersistence
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General) { WriteIndented = true };

    public static string GetPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AYP", "PrintQueue", "queue.json");

    public static List<QueueItem> Load()
    {
        try
        {
            var json = File.ReadAllText(GetPath());
            return (JsonSerializer.Deserialize<List<QueueItemSnapshot>>(json, Options) ?? [])
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Path) && File.Exists(snapshot.Path))
                .Select(QueueItem.FromSnapshot)
                .ToList();
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static void Save(IEnumerable<QueueItem> items)
    {
        var path = GetPath();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items.Select(item => item.ToSnapshot()).ToList(), Options));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

internal static class QueueBatch
{
    // ponytail: Four concurrent Shell launches avoid unbounded process spikes; tune this constant if target file handlers need a different limit.
    internal const int MaxParallelSubmissions = 4;

    public static Task SubmitAsync(IEnumerable<QueueItem> items, string printerName, bool preserveOrder)
        => preserveOrder
            ? Task.Run(() =>
            {
                foreach (var item in items)
                    item.SubmitToPrinter(printerName);
            })
            : RunAsync(items, item => item.SubmitToPrinter(printerName));

    internal static Task RunAsync<T>(IEnumerable<T> items, Action<T> submit)
        => Task.Run(() => Parallel.ForEach(
            items,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSubmissions },
            submit));
}

internal static class PrinterCatalog
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorInsufficientBuffer = 122;
    private const uint PrinterEnumLocal = 0x00000002;
    private const uint PrinterEnumConnections = 0x00000004;
    private const uint PrinterStatusError = 0x00000002;
    private const uint PrinterStatusPaused = 0x00000001;
    private const uint PrinterStatusOffline = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public string? PrinterName;
        public string? ServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
        public IntPtr DriverName;
        public IntPtr Comment;
        public IntPtr Location;
        public IntPtr DevMode;
        public IntPtr SepFile;
        public IntPtr PrintProcessor;
        public IntPtr Datatype;
        public IntPtr Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePPM;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr printerInfo, uint bufferSize, out uint bytesNeeded, out uint printersReturned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? printerName, ref uint bufferSize);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    public static IReadOnlyList<string> GetInstalledPrinterNames()
    {
        const uint level = 4;
        var success = EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, level, IntPtr.Zero, 0, out var bytesNeeded, out _);
        if (!success && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (bytesNeeded == 0)
            return [];

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, level, buffer, bytesNeeded, out _, out var printersReturned))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var names = new List<string>(checked((int)printersReturned));
            var itemSize = Marshal.SizeOf<PrinterInfo4>();
            for (var index = 0; index < printersReturned; index++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(IntPtr.Add(buffer, checked((int)index * itemSize)));
                if (!string.IsNullOrWhiteSpace(info.PrinterName))
                    names.Add(info.PrinterName);
            }

            return names
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static string? GetDefaultPrinterName()
    {
        uint bufferSize = 0;
        var success = GetDefaultPrinter(null, ref bufferSize);
        var error = Marshal.GetLastWin32Error();
        if (!success && error is not ErrorInsufficientBuffer and not ErrorFileNotFound)
            throw new Win32Exception(error);
        if (bufferSize == 0)
            return null;

        var printerName = new StringBuilder(checked((int)bufferSize));
        return GetDefaultPrinter(printerName, ref bufferSize)
            ? printerName.ToString()
            : throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static PrinterHealth GetHealth(string printerName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var bytesNeeded);
            if (bytesNeeded == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
            try
            {
                if (!GetPrinter(handle, 2, buffer, bytesNeeded, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                var label = info.Status switch
                {
                    var status when (status & PrinterStatusOffline) != 0 => "オフライン",
                    var status when (status & PrinterStatusPaused) != 0 => "一時停止",
                    var status when (status & PrinterStatusError) != 0 => "エラー",
                    _ => "利用可能",
                };
                return new PrinterHealth(label, checked((int)info.Jobs));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(handle); }
    }
}

public sealed record PrinterHealth(string StatusLabel, int JobCount);

internal static class QueueItemSelfTest
{
    public static void Run()
    {
        if (!QueueItem.IsSupported("report.xlsx") || !QueueItem.IsSupported("invoice.XLS") || !QueueItem.IsSupported("contract.pdf"))
            throw new InvalidOperationException("Supported documents must be accepted.");
        if (QueueItem.IsSupported("notes.txt"))
            throw new InvalidOperationException("Unsupported documents must be rejected.");

        var changedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        File.WriteAllText(changedPath, "before");
        var changed = new QueueItem(changedPath);
        File.WriteAllText(changedPath, "after");
        if (changed.GetFileChangeWarning() is null)
            throw new InvalidOperationException("Changed documents must be detected before submission.");
        File.Delete(changedPath);

        var withoutPrinter = new QueueItem(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.pdf"));
        withoutPrinter.SubmitToPrinter(string.Empty);
        if (withoutPrinter.Detail != "エラー: プリンターが選択されていません")
            throw new InvalidOperationException("A printer must be selected before submission.");

        var missing = new QueueItem(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.pdf"));
        if (missing.ExtensionLabel != "PDF" || missing.StatusLabel != "待機中")
            throw new InvalidOperationException("Queue display metadata must match the file and status.");
        missing.SubmitToPrinter("Test Printer");
        if (missing.Status != QueueStatus.Error)
            throw new InvalidOperationException("Missing documents must not be submitted to the printer.");

        TestParallelDispatch();
        Console.WriteLine("QueueItem self-test passed.");
    }

    private static void TestParallelDispatch()
    {
        if (QueueBatch.MaxParallelSubmissions <= 1)
            throw new InvalidOperationException("Queue submissions must allow parallel work.");

        using var started = new CountdownEvent(QueueBatch.MaxParallelSubmissions);
        using var release = new ManualResetEventSlim();
        var entered = 0;
        var dispatch = QueueBatch.RunAsync(
            Enumerable.Range(0, QueueBatch.MaxParallelSubmissions * 2),
            _ =>
            {
                var position = Interlocked.Increment(ref entered);
                if (position <= QueueBatch.MaxParallelSubmissions)
                    started.Signal();
                release.Wait();
            });

        var reachedLimit = started.Wait(TimeSpan.FromSeconds(15));
        if (reachedLimit)
            Thread.Sleep(100);
        var enteredBeforeRelease = Volatile.Read(ref entered);
        release.Set();
        dispatch.GetAwaiter().GetResult();

        if (!reachedLimit || enteredBeforeRelease != QueueBatch.MaxParallelSubmissions)
            throw new InvalidOperationException("Queue submissions must start in parallel and respect the limit.");
    }
}
