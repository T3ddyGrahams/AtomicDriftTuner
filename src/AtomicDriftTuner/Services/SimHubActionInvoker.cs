using System.Diagnostics;

namespace AtomicDriftTuner.Services;

public sealed class SimHubActionInvoker
{
    private const int DefaultDelayMs = 70;
    private const int MinimumDelayMs = 20;
    private const int MaximumDelayMs = 500;

    private const int ActionTimeoutMs = 5000;
    private const int MaxActionNameLength = 256;

    private readonly string _simHubExe;
    private readonly int _delayMs;

    public SimHubActionInvoker(
        string simHubExe,
        int delayMs = DefaultDelayMs)
    {
        _simHubExe =
            ValidateSimHubExecutable(
                simHubExe);

        _delayMs =
            Math.Clamp(
                delayMs,
                MinimumDelayMs,
                MaximumDelayMs);
    }

    public async Task<int?> TriggerAsync(
        string actionName,
        CancellationToken cancellationToken = default)
    {
        var normalizedAction =
            ValidateActionName(
                actionName);

        cancellationToken.ThrowIfCancellationRequested();

        var psi =
            new ProcessStartInfo
            {
                FileName =
                    _simHubExe,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                WindowStyle =
                    ProcessWindowStyle.Hidden,

                WorkingDirectory =
                    Path.GetDirectoryName(_simHubExe)
                    ?? AppContext.BaseDirectory
            };

        psi.ArgumentList.Add(
            "-triggeraction");

        psi.ArgumentList.Add(
            normalizedAction);

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException(
                "ADT could not start the SimHub action helper process.");

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            ActionTimeoutMs);

        try
        {
            await process.WaitForExitAsync(
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminateProcess(
                process);

            throw new TimeoutException(
                $"SimHub did not finish processing {normalizedAction} within {ActionTimeoutMs / 1000} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(
                process);

            throw;
        }

        var exitCode =
            process.ExitCode;

        // SimHub's helper invocation may report a non-zero or -1 exit code
        // even when the already-running SimHub instance accepted the action.
        //
        // Callers must treat this value as diagnostic only. Live AZOM
        // readback remains the authority for whether the requested setting
        // actually changed.
        await Task.Delay(
            _delayMs,
            cancellationToken);

        return exitCode;
    }

    private static string ValidateSimHubExecutable(
        string simHubExe)
    {
        if (string.IsNullOrWhiteSpace(simHubExe))
        {
            throw new ArgumentException(
                "SimHub executable path is required.",
                nameof(simHubExe));
        }

        string normalized;

        try
        {
            normalized =
                Path.GetFullPath(
                    simHubExe.Trim());
        }
        catch (Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new FileNotFoundException(
                "The SimHub executable path is invalid.",
                simHubExe,
                ex);
        }

        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException(
                "SimHubWPF.exe was not found.",
                normalized);
        }

        if (!string.Equals(
                Path.GetFileName(normalized),
                "SimHubWPF.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "ADT only allows the SimHub action fallback to launch SimHubWPF.exe.",
                nameof(simHubExe));
        }

        try
        {
            var attributes =
                File.GetAttributes(
                    normalized);

            if (
                (attributes & FileAttributes.Directory) != 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    "The selected SimHub executable must be a regular SimHubWPF.exe file.",
                    nameof(simHubExe));
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FileNotFoundException(
                "ADT could not validate SimHubWPF.exe.",
                normalized,
                ex);
        }

        return normalized;
    }

    private static string ValidateActionName(
        string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException(
                "SimHub action name is required.",
                nameof(actionName));
        }

        var normalized =
            actionName.Trim();

        if (normalized.Length > MaxActionNameLength)
        {
            throw new ArgumentException(
                $"SimHub action name exceeds the supported {MaxActionNameLength}-character limit.",
                nameof(actionName));
        }

        if (!normalized.StartsWith(
                "AZOM.",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ADT's SimHub action fallback only allows AZOM.* actions.",
                nameof(actionName));
        }

        if (
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0'))
        {
            throw new ArgumentException(
                "SimHub action name contains invalid characters.",
                nameof(actionName));
        }

        return normalized;
    }

    private static void TryTerminateProcess(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Cleanup failure must not hide the original timeout/cancellation.
        }
    }
}
