using System.Diagnostics;

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

    public static bool IsSupported(string path) => SupportedExtensions.Contains(System.IO.Path.GetExtension(path));

    public void SubmitToDefaultPrinter()
    {
        if (!File.Exists(Path))
        {
            SetError("ファイルが見つかりません");
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path)
            {
                UseShellExecute = true,
                Verb = "print",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                SetError("印刷コマンドを開始できませんでした");
                return;
            }

            Status = QueueStatus.Submitted;
            Detail = "既定のプリンターへ送信済み";
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

internal static class QueueItemSelfTest
{
    public static void Run()
    {
        if (!QueueItem.IsSupported("report.xlsx") || !QueueItem.IsSupported("invoice.XLS") || !QueueItem.IsSupported("contract.pdf"))
            throw new InvalidOperationException("Supported documents must be accepted.");
        if (QueueItem.IsSupported("notes.txt"))
            throw new InvalidOperationException("Unsupported documents must be rejected.");

        var missing = new QueueItem(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.pdf"));
        missing.SubmitToDefaultPrinter();
        if (missing.Status != QueueStatus.Error)
            throw new InvalidOperationException("Missing documents must not be submitted to the printer.");

        Console.WriteLine("QueueItem self-test passed.");
    }
}
