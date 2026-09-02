using System.Diagnostics;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class BridgeManagerService
{
    public const string BridgeFileName = "AtomicDriftTuner.SimHubBridge.dll";

    public string GetPackagedBridgePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "BridgePayload",
            BridgeFileName);

    public string GetInstalledBridgePath(string simHubRoot) =>
        Path.Combine(simHubRoot, BridgeFileName);

    public bool IsSimHubRunning()
    {
        try
        {
            return Process.GetProcessesByName("SimHubWPF").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public BridgeInstallStatus GetStatus(string? simHubRoot)
    {
        simHubRoot ??= "";

        var packaged = GetPackagedBridgePath();
        var installed =
            string.IsNullOrWhiteSpace(simHubRoot)
                ? ""
                : GetInstalledBridgePath(simHubRoot);

        return new BridgeInstallStatus
        {
            SimHubRoot = simHubRoot,
            InstalledPath = installed,
            PackagedPath = packaged,
            SimHubValid = SimHubLocator.IsValidRoot(simHubRoot),
            SimHubRunning = IsSimHubRunning(),
            PackagedBridgeAvailable = File.Exists(packaged),
            BridgeInstalled = !string.IsNullOrWhiteSpace(installed) && File.Exists(installed),
            InstalledVersion = FileVersion(installed),
            PackagedVersion = FileVersion(packaged)
        };
    }

    public void InstallOrRepair(string simHubRoot)
    {
        if (!SimHubLocator.IsValidRoot(simHubRoot))
            throw new DirectoryNotFoundException(
                "Choose the SimHub folder containing SimHubWPF.exe and SimHub.Plugins.dll.");

        if (IsSimHubRunning())
            throw new InvalidOperationException(
                "SimHub is currently running. Fully exit SimHub, including its tray process, before installing or repairing the Atomic bridge.");

        var source = GetPackagedBridgePath();
        if (!File.Exists(source))
            throw new FileNotFoundException(
                "This Atomic build does not contain a packaged bridge payload. Use a beta installer/portable package produced by distribution\\build-beta-package.ps1, or install the bridge manually from a developer build.",
                source);

        var destination = GetInstalledBridgePath(simHubRoot);
        File.Copy(source, destination, overwrite: true);
    }

    public bool LaunchElevatedInstall(string simHubRoot)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas"
        };

        psi.ArgumentList.Add("--install-bridge");
        psi.ArgumentList.Add(simHubRoot);

        Process.Start(psi);
        return true;
    }

    public static bool TryHandleElevatedCommand(string[] args, out string message, out bool success)
    {
        message = "";
        success = false;

        if (args.Length < 2 ||
            !string.Equals(args[0], "--install-bridge", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            new BridgeManagerService().InstallOrRepair(args[1]);
            message =
                "Atomic Drift Tuner Bridge was installed/repaired successfully.\n\n" +
                "Start SimHub, enable 'Atomic Drift Tuner Bridge' under SimHub plugins if needed, then restart SimHub once if prompted.";
            success = true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            success = false;
        }

        return true;
    }

    private static string FileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "not installed";

        try
        {
            var version =
                FileVersionInfo.GetVersionInfo(path).FileVersion;

            return string.IsNullOrWhiteSpace(version)
                ? "present"
                : version;
        }
        catch
        {
            return "present";
        }
    }
}
