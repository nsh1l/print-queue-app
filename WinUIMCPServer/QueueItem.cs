using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string Name { get; }
    public QueueStatus Status { get; private set; } = QueueStatus.Pending;
    public string Detail { get; private set; } = "待機中";
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public string ExtensionLabel => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string AccessibleName => $"{Name}、{Detail}";
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
            Detail = $"{printerName}へ送信済み";
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    public override string ToString() => $"{Name} — {Detail}";

    private void SetError(string message)
    {
        Status = QueueStatus.Error;
        Detail = $"エラー: {message}";
    }
}

internal static class QueueBatch
{
    // ponytail: Four concurrent Shell launches avoid unbounded process spikes; tune this constant if target file handlers need a different limit.
    internal const int MaxParallelSubmissions = 4;

    public static Task SubmitAsync(IEnumerable<QueueItem> items, string printerName)
        => RunAsync(items, item => item.SubmitToPrinter(printerName));

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public string? PrinterName;
        public string? ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr printerInfo,
        uint bufferSize,
        out uint bytesNeeded,
        out uint printersReturned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? printerName, ref uint bufferSize);

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
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
}

internal static class QueueItemSelfTest
{
    public static void Run()
    {
        if (!QueueItem.IsSupported("report.xlsx") || !QueueItem.IsSupported("invoice.XLS") || !QueueItem.IsSupported("contract.pdf"))
            throw new InvalidOperationException("Supported documents must be accepted.");
        if (QueueItem.IsSupported("notes.txt"))
            throw new InvalidOperationException("Unsupported documents must be rejected.");

        var printers = PrinterCatalog.GetInstalledPrinterNames();
        var expectedPrinters = printers
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase);
        if (!printers.SequenceEqual(expectedPrinters, StringComparer.CurrentCultureIgnoreCase))
            throw new InvalidOperationException("Installed printers must be unique and sorted.");

        var defaultPrinter = PrinterCatalog.GetDefaultPrinterName();
        if (defaultPrinter is not null && !printers.Contains(defaultPrinter, StringComparer.CurrentCultureIgnoreCase))
            throw new InvalidOperationException("The default printer must be present in the installed printer list.");

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

        if (!reachedLimit)
            throw new InvalidOperationException("Queue submissions must start in parallel.");
        if (enteredBeforeRelease != QueueBatch.MaxParallelSubmissions)
            throw new InvalidOperationException("Queue submissions must respect the parallel limit.");
    }
}
