using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class App : System.Windows.Application
{
    private const string BridgeInstallArgument =
        "--install-bridge";

    private const long MaximumCrashLogBytes =
        2L * 1024L * 1024L;

    private const int MaximumLoggedExceptionCharacters =
        128 * 1024;

    private static readonly object CrashLogGate =
        new();

    private static readonly Regex AuthorizationRegex =
        new(
            @"(?i)\b(authorization\s*:\s*(?:bearer|basic)\s+)[^\s]+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveAssignmentRegex =
        new(
            @"(?i)\b(token|password|passwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret)\b(\s*[:=]\s*)([^\s;,'""\]\}]+)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex DiscordWebhookRegex =
        new(
            @"(?i)(https://(?:canary\.|ptb\.)?discord(?:app)?\.com/api/webhooks/\d+/)[A-Za-z0-9._-]+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    protected override void OnStartup(
        StartupEventArgs e)
    {
        HookGlobalExceptionHandlers();

        // Helper-mode routing happens before normal WPF startup. This keeps a
        // privileged bridge-install invocation from loading themes, constructing
        // MainWindow, starting timers/services, or processing normal UI startup.
        if (TryHandleBridgeHelperStartup(
                e.Args))
        {
            return;
        }

        base.OnStartup(
            e);

        try
        {
            var settings =
                new AppSettingsStore()
                    .Load();

            ThemeService.Apply(
                settings.Theme);
        }
        catch (Exception ex)
        {
            // Theme failure should not prevent ADT from attempting to start with
            // the resources already available in App.xaml.
            WriteCrashLog(
                "Theme startup",
                ex);
        }

        try
        {
            var main =
                new MainWindow();

            MainWindow =
                main;

            main.Show();
        }
        catch (Exception ex)
        {
            HandleFatalStartupFailure(
                ex);
        }
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        UnhookGlobalExceptionHandlers();

        base.OnExit(
            e);
    }

    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException -=
            OnDispatcherUnhandledException;

        DispatcherUnhandledException +=
            OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException -=
            OnDomainUnhandledException;

        AppDomain.CurrentDomain.UnhandledException +=
            OnDomainUnhandledException;

        TaskScheduler.UnobservedTaskException -=
            OnUnobservedTaskException;

        TaskScheduler.UnobservedTaskException +=
            OnUnobservedTaskException;
    }

    private void UnhookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException -=
            OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException -=
            OnDomainUnhandledException;

        TaskScheduler.UnobservedTaskException -=
            OnUnobservedTaskException;
    }

    private bool TryHandleBridgeHelperStartup(
        string[] args)
    {
        var containsReservedBridgeArgument =
            args.Any(
                argument =>
                    string.Equals(
                        argument,
                        BridgeInstallArgument,
                        StringComparison.OrdinalIgnoreCase));

        try
        {
            if (BridgeManagerService.TryHandleElevatedCommand(
                    args,
                    out var bridgeMessage,
                    out var bridgeSuccess))
            {
                SafeShowMessage(
                    bridgeMessage,
                    "ADT Bridge Install / Repair",
                    bridgeSuccess
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Error);

                Shutdown(
                    bridgeSuccess
                        ? 0
                        : 1);

                return true;
            }
        }
        catch (Exception ex)
        {
            WriteCrashLog(
                "Elevated bridge helper startup",
                ex);

            SafeShowMessage(
                "ADT could not complete the elevated bridge-install operation.\n\n" +
                SafeUserMessage(
                    ex),
                "ADT Bridge Install / Repair",
                MessageBoxImage.Error);

            Shutdown(
                1);

            return true;
        }

        if (!containsReservedBridgeArgument)
        {
            return false;
        }

        // A malformed reserved helper invocation must never fall through into a
        // normal ADT session. In particular, do not start MainWindow after an
        // invalid or incomplete privileged-install command line.
        WriteCrashLog(
            "Rejected malformed bridge helper invocation",
            new InvalidOperationException(
                "The reserved bridge-install startup command was present but did not match the expected helper argument contract."));

        SafeShowMessage(
            "ADT refused an invalid bridge-install startup request.\n\n" +
            "No bridge files were changed. Start ADT normally and use Setup & Paths to run Install / Repair again.",
            "ADT Bridge Install / Repair",
            MessageBoxImage.Error);

        Shutdown(
            2);

        return true;
    }

    private void HandleFatalStartupFailure(
        Exception ex)
    {
        WriteCrashLog(
            "ADT startup",
            ex);

        SafeShowMessage(
            "ADT could not finish starting and will close.\n\n" +
            SafeUserMessage(
                ex) +
            "\n\nA local crash log was written under %LOCALAPPDATA%\\AtomicDriftTuner\\Logs.",
            "ADT Startup Error",
            MessageBoxImage.Error);

        try
        {
            Shutdown(
                1);
        }
        catch
        {
            // Startup is already failing. Do not replace the original failure
            // with a second shutdown exception.
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(
            "WPF DispatcherUnhandledException",
            e.Exception);

        // Continuing after an arbitrary unhandled UI exception can leave tuning,
        // live-bridge, remote-write, or cached workspace state partially mutated.
        // Log the failure, inform the user, and fail closed instead.
        e.Handled =
            true;

        SafeShowMessage(
            "ADT encountered an unexpected UI error and must close to avoid continuing with uncertain application state.\n\n" +
            SafeUserMessage(
                e.Exception) +
            "\n\nA local crash log was written under %LOCALAPPDATA%\\AtomicDriftTuner\\Logs.",
            "ADT Unexpected Error",
            MessageBoxImage.Error);

        try
        {
            Shutdown(
                1);
        }
        catch
        {
            // The dispatcher is already in an exceptional state.
        }
    }

    private void OnDomainUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        WriteCrashLog(
            "AppDomain UnhandledException (terminating=" +
            e.IsTerminating +
            ")",
            e.ExceptionObject as Exception ??
            new Exception(
                e.ExceptionObject?.ToString() ??
                "Unknown fatal error"));
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(
            "TaskScheduler UnobservedTaskException",
            e.Exception);

        // This event is raised for a faulted Task that nobody awaited/observed.
        // Preserve the process after recording it; active awaited operations have
        // their own UI/service error handling and are not routed through here.
        e.SetObserved();
    }

    private static void SafeShowMessage(
        string message,
        string title,
        MessageBoxImage image)
    {
        try
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                image);
        }
        catch (Exception ex)
        {
            WriteCrashLog(
                "MessageBox failure",
                ex);
        }
    }

    private static string SafeUserMessage(
        Exception exception)
    {
        var message =
            exception.GetBaseException()
                .Message;

        if (string.IsNullOrWhiteSpace(
                message))
        {
            return
                "An unexpected error occurred.";
        }

        message =
            RedactSensitiveText(
                message);

        var builder =
            new StringBuilder(
                Math.Min(
                    message.Length,
                    800));

        var previousWasSpace =
            false;

        foreach (
            var character in
            message)
        {
            if (builder.Length >=
                800)
            {
                break;
            }

            if (
                character is
                    '\r' or
                    '\n' or
                    '\t' ||
                char.IsControl(
                    character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(
                        ' ');

                    previousWasSpace =
                        true;
                }

                continue;
            }

            builder.Append(
                character);

            previousWasSpace =
                character ==
                ' ';
        }

        var result =
            builder
                .ToString()
                .Trim();

        return string.IsNullOrWhiteSpace(
                result)
            ? "An unexpected error occurred."
            : result;
    }

    private static void WriteCrashLog(
        string source,
        Exception ex)
    {
        try
        {
            var dir =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "AtomicDriftTuner",
                    "Logs");

            Directory.CreateDirectory(
                dir);

            var path =
                Path.Combine(
                    dir,
                    "atomic-crash.log");

            var previousPath =
                Path.Combine(
                    dir,
                    "atomic-crash.previous.log");

            var exceptionText =
                ex.ToString();

            if (exceptionText.Length >
                MaximumLoggedExceptionCharacters)
            {
                exceptionText =
                    exceptionText[
                        ..MaximumLoggedExceptionCharacters] +
                    Environment.NewLine +
                    "[ADT crash log truncated.]";
            }

            exceptionText =
                RedactSensitiveText(
                    exceptionText);

            var entry =
                $"[{DateTimeOffset.UtcNow:O}] {source}\r\n" +
                exceptionText +
                "\r\n\r\n";

            lock (CrashLogGate)
            {
                RotateCrashLogIfNeeded(
                    path,
                    previousPath,
                    Encoding.UTF8.GetByteCount(
                        entry));

                File.AppendAllText(
                    path,
                    entry,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier:
                            false));
            }
        }
        catch
        {
            // Logging must never cause a second crash.
        }
    }

    private static void RotateCrashLogIfNeeded(
        string path,
        string previousPath,
        int incomingBytes)
    {
        try
        {
            if (!File.Exists(
                    path))
            {
                return;
            }

            var length =
                new FileInfo(
                    path)
                    .Length;

            if (
                length +
                incomingBytes <=
                MaximumCrashLogBytes)
            {
                return;
            }

            if (File.Exists(
                    previousPath))
            {
                File.Delete(
                    previousPath);
            }

            File.Move(
                path,
                previousPath);
        }
        catch
        {
            // Rotation is best-effort. The outer logger will still attempt to
            // append the new entry to the current log.
        }
    }

    private static string RedactSensitiveText(
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return value;
        }

        var result =
            value;

        result =
            ReplaceIfPresent(
                result,
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                "%USERPROFILE%");

        result =
            ReplaceIfPresent(
                result,
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "%LOCALAPPDATA%");

        result =
            ReplaceIfPresent(
                result,
                Environment.UserName,
                "[USER]");

        result =
            ReplaceIfPresent(
                result,
                Environment.MachineName,
                "[MACHINE]");

        result =
            AuthorizationRegex.Replace(
                result,
                "$1[REDACTED]");

        result =
            SensitiveAssignmentRegex.Replace(
                result,
                "$1$2[REDACTED]");

        result =
            DiscordWebhookRegex.Replace(
                result,
                "$1[REDACTED]");

        return result;
    }

    private static string ReplaceIfPresent(
        string text,
        string? value,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return text;
        }

        return text.Replace(
            value,
            replacement,
            StringComparison.OrdinalIgnoreCase);
    }
}
