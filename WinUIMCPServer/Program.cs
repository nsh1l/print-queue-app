using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;

namespace PrintQueueApp.WinUI
{
    public class Program
    {
        [MTAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--mcp")
            {
                // Run as MCP stdio server (no GUI, tools only)
                var server = new MCPServer(null);
                var stdio = new MCPServerStdio(server);
                stdio.RunAsync(CancellationToken.None).Wait();
                return;
            }

            // Normal WinUI app entry
            AppInstance inst = AppInstance.FindOrRegisterForInstance("print-queue-app");
            global::System.Diagnostics.Debug.WriteLine("PrintQueueApp WinUI starting...");

            // Dispatch to WinUI thread
            Windows.ApplicationModel.Core.CoreApplication.Run(
                new Windows.ApplicationModel.Core.IFrameworkViewSource[]
                {
                    new AppViewSource()
                }
            );
        }
    }

    public class AppViewSource : Windows.ApplicationModel.Core.IFrameworkViewSource
    {
        public Windows.ApplicationModel.Core.IFrameworkView CreateView()
        {
            return new AppView();
        }
    }

    public class AppView : Windows.ApplicationModel.Core.IFrameworkView
    {
        private MainWindow? _window;

        public void Initialize(Windows.ApplicationModel.Core.CoreApplicationView applicationView)
        {
            applicationView.Activated += (sender, args) =>
            {
                if (args.Kind == Windows.ApplicationModel.Activation.ActivationKind.File)
                {
                    // Handle file open activation
                }
            };
        }

        public void Load(string entryPoint)
        {
        }

        public void Run()
        {
            _window = new MainWindow();
            _window.Activate();
            Windows.UI.Xaml.Window.Current.CoreWindow.Dispatcher.ProcessEvents(
                Windows.UI.Core.CoreProcessEventsOption.ProcessUntilQuit
            );
        }

        public void SetWindow(Windows.UI.Xaml.Window window)
        {
        }

        public void Uninitialize()
        {
        }
    }
}
