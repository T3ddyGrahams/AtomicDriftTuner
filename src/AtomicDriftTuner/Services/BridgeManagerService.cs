using System.Diagnostics;
using System.Security.Cryptography;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class BridgeManagerService
{
    public const string BridgeFileName =
        "AtomicDriftTuner.SimHubBridge.dll";

    private const string ElevatedInstallArgument =
        "--install-bridge";

    public string GetPackagedBridgePath()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "BridgePayload",
                BridgeFileName));
    }

    public string GetInstalledBridgePath(
        string simHubRoot)
    {
        if (string.IsNullOrWhiteSpace(simHubRoot))
        {
            throw new ArgumentException(
                "SimHub folder is required.",
                nameof(simHubRoot));
        }

        return Path.GetFullPath(
            Path.Combine(
                simHubRoot,
                BridgeFileName));
    }

    public bool IsSimHubRunning()
    {
        try
        {
            using var currentProcess =
                Process.GetCurrentProcess();

            var processes =
                Process.GetProcessesByName(
                    "SimHubWPF");

            try
            {
                return processes.Any(
                    process =>
                        process.Id != currentProcess.Id);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Failure to enumerate processes should not crash ADT.
            // Installation will still be protected by normal filesystem
            // access and copy/verification checks.
            return false;
        }
    }

    public BridgeInstallStatus GetStatus(
        string? simHubRoot)
    {
        var root =
            NormalizeOptionalPath(
                simHubRoot);

        var packaged =
            GetPackagedBridgePath();

        var installed =
            "";

        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                installed =
                    GetInstalledBridgePath(
                        root);
            }
            catch
            {
                installed =
                    "";
            }
        }

        var packagedExists =
            IsRegularFile(
                packaged);

        var installedExists =
            !string.IsNullOrWhiteSpace(installed) &&
            IsRegularFile(
                installed);

        return new BridgeInstallStatus
        {
            SimHubRoot =
                root,

            InstalledPath =
                installed,

            PackagedPath =
                packaged,

            SimHubValid =
                SimHubLocator.IsValidRoot(
                    root),

            SimHubRunning =
                IsSimHubRunning(),

            PackagedBridgeAvailable =
                packagedExists,

            BridgeInstalled =
                installedExists,

            InstalledVersion =
                FileVersion(
                    installed),

            PackagedVersion =
                FileVersion(
                    packaged)
        };
    }

    public void InstallOrRepair(
        string simHubRoot)
    {
        var normalizedRoot =
            NormalizeRequiredSimHubRoot(
                simHubRoot);

        if (IsSimHubRunning())
        {
            throw new InvalidOperationException(
                "SimHub is currently running. Fully exit SimHub, including its tray process, before installing or repairing the ADT bridge.");
        }

        var source =
            GetPackagedBridgePath();

        ValidatePackagedBridge(
            source);

        var destination =
            GetInstalledBridgePath(
                normalizedRoot);

        var sourceHash =
            ComputeSha256(
                source);

        try
        {
            File.Copy(
                source,
                destination,
                overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "Windows denied access to the SimHub folder. Run the bridge install through ADT's administrator prompt and try again.",
                ex);
        }
        catch (IOException ex)
        {
            throw new IOException(
                "ADT could not copy the SimHub bridge into the SimHub folder. Make sure SimHub is fully closed and try again.",
                ex);
        }

        if (!IsRegularFile(destination))
        {
            throw new IOException(
                "The ADT bridge copy completed without producing the destination DLL.");
        }

        string destinationHash;

        try
        {
            destinationHash =
                ComputeSha256(
                    destination);
        }
        catch
        {
            TryDeleteFailedDestination(
                destination);

            throw;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(sourceHash),
                Convert.FromHexString(destinationHash)))
        {
            TryDeleteFailedDestination(
                destination);

            throw new InvalidDataException(
                "ADT bridge installation verification failed. The installed DLL does not match the packaged bridge payload.");
        }
    }

    public bool LaunchElevatedInstall(
        string simHubRoot)
    {
        var normalizedRoot =
            NormalizeRequiredSimHubRoot(
                simHubRoot);

        var source =
            GetPackagedBridgePath();

        ValidatePackagedBridge(
            source);

        var exe =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        exe =
            Path.GetFullPath(
                exe);

        if (!IsRegularFile(exe))
        {
            return false;
        }

        var psi =
            new ProcessStartInfo
            {
                FileName =
                    exe,

                UseShellExecute =
                    true,

                Verb =
                    "runas",

                WorkingDirectory =
                    AppContext.BaseDirectory
            };

        psi.ArgumentList.Add(
            ElevatedInstallArgument);

        psi.ArgumentList.Add(
            normalizedRoot);

        try
        {
            using var process =
                Process.Start(
                    psi);

            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
            when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED:
            // the user declined the Windows UAC prompt.
            return false;
        }
    }

    public static bool TryHandleElevatedCommand(
        string[] args,
        out string message,
        out bool success)
    {
        message =
            "";

        success =
            false;

        if (
            args is null ||
            args.Length < 2 ||
            !string.Equals(
                args[0],
                ElevatedInstallArgument,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var manager =
                new BridgeManagerService();

            manager.InstallOrRepair(
                args[1]);

            var status =
                manager.GetStatus(
                    args[1]);

            message =
                "Atomic Drift Tuner Bridge was installed/repaired and verified successfully.\n\n" +
                $"Installed version: {status.InstalledVersion}\n\n" +
                "Start SimHub, enable 'Atomic Drift Tuner Bridge' under SimHub plugins if needed, then restart SimHub once if prompted.";

            success =
                true;
        }
        catch (Exception ex)
        {
            message =
                "ADT could not install or repair the SimHub bridge.\n\n" +
                ex.Message;

            success =
                false;
        }

        return true;
    }

    private static string NormalizeRequiredSimHubRoot(
        string simHubRoot)
    {
        if (string.IsNullOrWhiteSpace(simHubRoot))
        {
            throw new DirectoryNotFoundException(
                "Choose the SimHub folder containing SimHubWPF.exe and SimHub.Plugins.dll.");
        }

        string normalized;

        try
        {
            normalized =
                Path.GetFullPath(
                    simHubRoot.Trim());
        }
        catch (Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new DirectoryNotFoundException(
                "The selected SimHub folder path is invalid.",
                ex);
        }

        if (!SimHubLocator.IsValidRoot(normalized))
        {
            throw new DirectoryNotFoundException(
                "Choose the SimHub folder containing SimHubWPF.exe and SimHub.Plugins.dll.");
        }

        return normalized;
    }

    private static string NormalizeOptionalPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(
                path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static void ValidatePackagedBridge(
        string path)
    {
        if (!IsRegularFile(path))
        {
            throw new FileNotFoundException(
                "This ADT build does not contain a valid packaged bridge payload. Use an installer/portable package produced by distribution\\build-beta-package.ps1, or install the bridge manually from a developer build.",
                path);
        }

        var info =
            new FileInfo(
                path);

        if (info.Length <= 0)
        {
            throw new InvalidDataException(
                "The packaged ADT SimHub bridge DLL is empty.");
        }

        try
        {
            _ =
                FileVersionInfo.GetVersionInfo(
                    path);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The packaged ADT SimHub bridge DLL could not be inspected.",
                ex);
        }
    }

    private static bool IsRegularFile(
        string? path)
    {
        if (
            string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
        {
            return false;
        }

        try
        {
            var attributes =
                File.GetAttributes(
                    path);

            return
                (attributes & FileAttributes.Directory) == 0 &&
                (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeSha256(
        string path)
    {
        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);

        using var sha =
            SHA256.Create();

        var hash =
            sha.ComputeHash(
                stream);

        return Convert
            .ToHexString(hash);
    }

    private static void TryDeleteFailedDestination(
        string destination)
    {
        try
        {
            if (File.Exists(destination))
            {
                File.Delete(
                    destination);
            }
        }
        catch
        {
            // Cleanup failure must not hide the verification failure.
        }
    }

    private static string FileVersion(
        string? path)
    {
        if (
            string.IsNullOrWhiteSpace(path) ||
            !IsRegularFile(path))
        {
            return "not installed";
        }

        try
        {
            var version =
                FileVersionInfo
                    .GetVersionInfo(path)
                    .FileVersion;

            return
                string.IsNullOrWhiteSpace(version)
                    ? "present"
                    : version;
        }
        catch
        {
            return "present";
        }
    }
}
