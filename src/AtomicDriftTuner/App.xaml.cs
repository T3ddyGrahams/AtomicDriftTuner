using System.IO;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        if (BridgeManagerService.TryHandleElevatedCommand(
                e.Args,
                out var bridgeMessage,
                out var bridgeSuccess))
        {
            MessageBox.Show(
                bridgeMessage,
                "Atomic Bridge Install / Repair",
                MessageBoxButton.OK,
                bridgeSuccess
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Error);

            Shutdown(
                bridgeSuccess
                    ? 0
                    : 1);
            return;
        }

        try
        {
            var settings = new AppSettingsStore().Load();
            ThemeService.Apply(settings.Theme);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Theme startup", ex);
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("WPF DispatcherUnhandledException", e.Exception);

        MessageBox.Show(
            "Atomic Drift Tuner caught an unexpected UI error instead of closing.\n\n" +
            e.Exception.Message +
            "\n\nA diagnostic log was saved under %LOCALAPPDATA%\\AtomicDriftTuner\\Logs.",
            "Atomic Drift Tuner",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        WriteCrashLog(
            "AppDomain UnhandledException (terminating=" + e.IsTerminating + ")",
            e.ExceptionObject as Exception ??
            new Exception(
                e.ExceptionObject?.ToString() ??
                "Unknown fatal error"));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtomicDriftTuner",
                "Logs");

            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "atomic-crash.log");

            File.AppendAllText(
                path,
                $"[{DateTime.Now:O}] {source}\r\n{ex}\r\n\r\n");
        }
        catch
        {
            // Logging must never cause a second crash.
        }
    }
}
