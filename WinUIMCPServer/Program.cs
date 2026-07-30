using Microsoft.UI.Xaml;

namespace PrintQueueApp.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

public static class Program
{
    [System.Runtime.InteropServices.DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--print-test")
        {
            var item = new QueueItem(System.IO.Path.GetFullPath(args[1]));
            var printerName = PrinterCatalog.GetDefaultPrinterName();
            if (printerName is null)
            {
                Console.Error.WriteLine("既定のプリンターが見つかりません。");
                Environment.ExitCode = 1;
                return;
            }
            item.SubmitToPrinter(printerName);
            Console.WriteLine(item);
            if (item.Status != QueueStatus.Submitted)
                Environment.ExitCode = 1;
            return;
        }

        if (args.Length == 1 && args[0] == "--self-test")
        {
            QueueItemSelfTest.Run();
            return;
        }

        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
