using System.Configuration;
using System.Data;
using System.Windows;

namespace DanteMicBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            System.IO.File.AppendAllText("crash_log.txt", $"[Dispatcher Exception] {DateTime.Now}: {args.Exception}\n");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            System.IO.File.AppendAllText("crash_log.txt", $"[Domain Exception] {DateTime.Now}: {args.ExceptionObject}\n");
        };
    }
}

