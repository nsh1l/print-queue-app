using Microsoft.UI.Xaml;

namespace PrintQueueApp.WinUI;

public partial class App : Application
{
    private Window? _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--print-test")
        {
            var item = new QueueItem(System.IO.Path.GetFullPath(args[1]));
            item.SubmitToDefaultPrinter();
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

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new App());
    }
}
