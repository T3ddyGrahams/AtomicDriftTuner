using System.Diagnostics;

namespace AtomicDriftTuner.Services;

public sealed class SimHubActionInvoker
{
    private readonly string _simHubExe;
    private readonly int _delayMs;

    public SimHubActionInvoker(string simHubExe, int delayMs = 70)
    {
        if (!File.Exists(simHubExe)) throw new FileNotFoundException("SimHubWPF.exe was not found.", simHubExe);
        _simHubExe = simHubExe;
        _delayMs = Math.Clamp(delayMs, 20, 500);
    }

    public async Task<int?> TriggerAsync(string actionName, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _simHubExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add("-triggeraction");
        psi.ArgumentList.Add(actionName);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start SimHub action command.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(5000);
        await process.WaitForExitAsync(timeout.Token);

        // The helper process may return -1 even when the running SimHub instance
        // accepted the command. Callers must verify success through AZOM readback.
        int exitCode = process.ExitCode;
        await Task.Delay(_delayMs, cancellationToken);
        return exitCode;
    }
}
