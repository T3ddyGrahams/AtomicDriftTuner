using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class BridgeManagerService
{
    public const string BridgeFileName =
        "AtomicDriftTuner.SimHubBridge.dll";

    private const string ElevatedInstallArgument =
        "--install-bridge";

    private const string ExpectedBridgeAssemblyName =
        "AtomicDriftTuner.SimHubBridge";

    private const long MaximumBridgePayloadBytes =
        32L * 1024L * 1024L;

    private static readonly object InstallGate =
        new();

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
        if (string.IsNullOrWhiteSpace(
                simHubRoot))
        {
            throw new ArgumentException(
                "SimHub folder is required.",
                nameof(simHubRoot));
        }

        var root =
            Path.GetFullPath(
                simHubRoot
                    .Trim()
                    .Trim('"'));

        var destination =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    BridgeFileName));

        EnsureDirectChild(
            root,
            destination);

        return destination;
    }

    public bool IsSimHubRunning()
    {
        return
            TryGetSimHubRunningState(
                out var running) &&
            running;
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

        if (!string.IsNullOrWhiteSpace(
                root))
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
            TryValidatePackagedBridge(
                packaged);

        var installedExists =
            !string.IsNullOrWhiteSpace(
                installed) &&
            TryValidateBridgeAssembly(
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
                SafeIsValidSimHubRoot(
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
        InstallOrRepairCore(
            simHubRoot,
            expectedSourceSha256:
                null);
    }

    public bool LaunchElevatedInstall(
        string simHubRoot)
    {
        var normalizedRoot =
            NormalizeRequiredSimHubRoot(
                simHubRoot);

        EnsureInstallTargetSafety(
            normalizedRoot);

        var source =
            GetPackagedBridgePath();

        ValidatePackagedBridge(
            source);

        var sourceHash =
            ComputeSha256(
                source);

        var exe =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                exe))
        {
            return false;
        }

        exe =
            Path.GetFullPath(
                exe);

        if (!IsRegularFile(
                exe))
        {
            return false;
        }

        var executableName =
            Path.GetFileNameWithoutExtension(
                exe);

        if (!string.Equals(
                executableName,
                "AtomicDriftTuner",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ADT cannot launch the elevated bridge installer from this host process. Run the packaged AtomicDriftTuner.exe and try again.");
        }

        var appBase =
            Path.GetFullPath(
                AppContext.BaseDirectory);

        var exeDirectory =
            Path.GetFullPath(
                Path.GetDirectoryName(
                    exe) ??
                "");

        if (!PathsEqual(
                appBase,
                exeDirectory))
        {
            throw new InvalidOperationException(
                "ADT refused to elevate because the running executable is outside the active ADT application directory.");
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
                    appBase
            };

        psi.ArgumentList.Add(
            ElevatedInstallArgument);

        psi.ArgumentList.Add(
            normalizedRoot);

        // Snapshot the exact packaged payload before UAC. The elevated process
        // refuses to install if the payload changes between this request and the
        // privileged copy.
        psi.ArgumentList.Add(
            sourceHash);

        try
        {
            using var process =
                Process.Start(
                    psi);

            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
            when (
                ex.NativeErrorCode ==
                1223)
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
            args.Length !=
                3 ||
            !string.Equals(
                args[0],
                ElevatedInstallArgument,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsSha256Hex(
                args[2]))
        {
            message =
                "ADT refused the elevated bridge-install request because its packaged-payload verification value was invalid.";

            return true;
        }

        try
        {
            var manager =
                new BridgeManagerService();

            manager.InstallOrRepairCore(
                args[1],
                args[2]);

            var status =
                manager.GetStatus(
                    args[1]);

            if (!status.BridgeInstalled)
            {
                throw new InvalidDataException(
                    "The elevated bridge-install operation completed, but ADT could not detect a valid installed bridge afterward.");
            }

            message =
                "ADT SimHub Bridge was installed/repaired and verified during installation.\n\n" +
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

    private void InstallOrRepairCore(
        string simHubRoot,
        string? expectedSourceSha256)
    {
        lock (InstallGate)
        {
            var normalizedRoot =
                NormalizeRequiredSimHubRoot(
                    simHubRoot);

            EnsureInstallTargetSafety(
                normalizedRoot);

            EnsureSimHubStopped();

            var source =
                GetPackagedBridgePath();

            ValidatePackagedBridge(
                source);

            var sourceHash =
                ComputeSha256(
                    source);

            if (
                !string.IsNullOrWhiteSpace(
                    expectedSourceSha256) &&
                !FixedTimeHashEquals(
                    sourceHash,
                    expectedSourceSha256))
            {
                throw new InvalidDataException(
                    "The packaged ADT bridge payload changed after the administrator operation was requested. No bridge files were installed. Restart ADT and try again.");
            }

            var destination =
                GetInstalledBridgePath(
                    normalizedRoot);

            EnsureSafeExistingDestination(
                destination);

            if (File.Exists(
                    destination))
            {
                EnsureDestinationNotInUse(
                    destination);
            }

            var stagePath =
                Path.Combine(
                    normalizedRoot,
                    $".{BridgeFileName}.{Guid.NewGuid():N}.installing");

            var backupPath =
                Path.Combine(
                    normalizedRoot,
                    $".{BridgeFileName}.{Guid.NewGuid():N}.backup");

            var backupCreated =
                false;

            var replacementAttempted =
                false;

            var keepBackup =
                false;

            string? backupHash =
                null;

            try
            {
                File.Copy(
                    source,
                    stagePath,
                    overwrite:
                        false);

                ValidatePackagedBridge(
                    stagePath);

                var stageHash =
                    ComputeSha256(
                        stagePath);

                if (!FixedTimeHashEquals(
                        sourceHash,
                        stageHash))
                {
                    throw new InvalidDataException(
                        "The staged ADT bridge payload did not match the packaged source. The installed bridge was not changed.");
                }

                if (File.Exists(
                        destination))
                {
                    var originalHash =
                        ComputeSha256(
                            destination);

                    File.Copy(
                        destination,
                        backupPath,
                        overwrite:
                            false);

                    backupCreated =
                        true;

                    backupHash =
                        ComputeSha256(
                            backupPath);

                    if (!FixedTimeHashEquals(
                            originalHash,
                            backupHash))
                    {
                        throw new InvalidDataException(
                            "ADT could not create a verified rollback copy of the currently installed bridge. The installed bridge was not changed.");
                    }
                }

                replacementAttempted =
                    true;

                // Stage and destination are deliberately in the same directory so
                // the final publish is a same-volume rename rather than a streaming
                // overwrite of the active destination path.
                File.Move(
                    stagePath,
                    destination,
                    overwrite:
                        true);

                ValidateInstalledBridge(
                    destination,
                    sourceHash);

                if (backupCreated)
                {
                    TryDeleteFile(
                        backupPath);
                }
            }
            catch (Exception installException)
            {
                if (replacementAttempted)
                {
                    var rollbackFailure =
                        TryRollback(
                            destination,
                            backupPath,
                            backupCreated,
                            backupHash);

                    if (rollbackFailure is not null)
                    {
                        keepBackup =
                            backupCreated &&
                            File.Exists(
                                backupPath);

                        var backupNote =
                            keepBackup
                                ? $"\n\nADT preserved the rollback copy at:\n{backupPath}"
                                : "";

                        throw new IOException(
                            "ADT bridge installation failed and rollback could not be fully verified." +
                            backupNote,
                            new AggregateException(
                                installException,
                                rollbackFailure));
                    }
                }

                throw TranslateInstallException(
                    installException);
            }
            finally
            {
                TryDeleteFile(
                    stagePath);

                if (
                    backupCreated &&
                    !keepBackup)
                {
                    TryDeleteFile(
                        backupPath);
                }
            }
        }
    }

    private static Exception TranslateInstallException(
        Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return new UnauthorizedAccessException(
                "Windows denied access to the SimHub folder. Use ADT's administrator bridge-install prompt and try again.",
                exception);
        }

        if (
            exception is IOException ioException &&
            exception is not InvalidDataException)
        {
            return new IOException(
                "ADT could not install the SimHub bridge. Make sure SimHub is fully closed and the selected SimHub folder is writable, then try again.",
                ioException);
        }

        return exception;
    }

    private static Exception? TryRollback(
        string destination,
        string backupPath,
        bool backupCreated,
        string? backupHash)
    {
        try
        {
            if (backupCreated)
            {
                if (!IsRegularFile(
                        backupPath))
                {
                    throw new IOException(
                        "The rollback copy is missing or invalid.");
                }

                File.Move(
                    backupPath,
                    destination,
                    overwrite:
                        true);

                if (
                    string.IsNullOrWhiteSpace(
                        backupHash) ||
                    !FixedTimeHashEquals(
                        ComputeSha256(
                            destination),
                        backupHash))
                {
                    throw new InvalidDataException(
                        "The previous bridge file was restored, but rollback verification failed.");
                }

                return null;
            }

            if (File.Exists(
                    destination))
            {
                File.Delete(
                    destination);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void ValidateInstalledBridge(
        string destination,
        string expectedSha256)
    {
        if (!IsRegularFile(
                destination))
        {
            throw new IOException(
                "The ADT bridge publish completed without producing a regular destination DLL.");
        }

        ValidateBridgeAssembly(
            destination);

        var destinationHash =
            ComputeSha256(
                destination);

        if (!FixedTimeHashEquals(
                destinationHash,
                expectedSha256))
        {
            throw new InvalidDataException(
                "ADT bridge installation verification failed. The installed DLL does not match the packaged bridge payload.");
        }
    }

    private static string NormalizeRequiredSimHubRoot(
        string simHubRoot)
    {
        if (string.IsNullOrWhiteSpace(
                simHubRoot))
        {
            throw new DirectoryNotFoundException(
                "Choose the SimHub folder containing SimHubWPF.exe and SimHub.Plugins.dll.");
        }

        string normalized;

        try
        {
            normalized =
                Path.GetFullPath(
                    simHubRoot
                        .Trim()
                        .Trim('"'));
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

        if (!SafeIsValidSimHubRoot(
                normalized))
        {
            throw new DirectoryNotFoundException(
                "Choose the SimHub folder containing SimHubWPF.exe and SimHub.Plugins.dll.");
        }

        return normalized;
    }

    private static string NormalizeOptionalPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(
                path
                    .Trim()
                    .Trim('"'));
        }
        catch
        {
            return path.Trim();
        }
    }

    private static void EnsureInstallTargetSafety(
        string simHubRoot)
    {
        if (!Directory.Exists(
                simHubRoot))
        {
            throw new DirectoryNotFoundException(
                "The selected SimHub folder does not exist.");
        }

        if (
            simHubRoot.StartsWith(
                @"\\",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ADT bridge install/repair does not write to UNC/network SimHub folders. Select a local SimHub installation.");
        }

        FileAttributes attributes;

        try
        {
            attributes =
                File.GetAttributes(
                    simHubRoot);
        }
        catch (Exception ex)
        {
            throw new IOException(
                "ADT could not inspect the selected SimHub folder.",
                ex);
        }

        if (
            (attributes &
             FileAttributes.ReparsePoint) !=
            0)
        {
            throw new InvalidOperationException(
                "ADT bridge install/repair does not write through a SimHub folder junction or symbolic link. Select the physical SimHub installation folder or install the bridge manually.");
        }

        var destination =
            Path.GetFullPath(
                Path.Combine(
                    simHubRoot,
                    BridgeFileName));

        EnsureDirectChild(
            simHubRoot,
            destination);
    }

    private static void EnsureDirectChild(
        string directory,
        string path)
    {
        var normalizedDirectory =
            Path.GetFullPath(
                    directory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        var normalizedPath =
            Path.GetFullPath(
                path);

        if (!normalizedPath.StartsWith(
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ADT refused a bridge path outside the selected SimHub folder.");
        }
    }

    private static void EnsureSafeExistingDestination(
        string destination)
    {
        if (!File.Exists(
                destination))
        {
            return;
        }

        FileAttributes attributes;

        try
        {
            attributes =
                File.GetAttributes(
                    destination);
        }
        catch (Exception ex)
        {
            throw new IOException(
                "ADT could not inspect the currently installed bridge file.",
                ex);
        }

        if (
            (attributes &
             FileAttributes.Directory) !=
                0 ||
            (attributes &
             FileAttributes.ReparsePoint) !=
                0)
        {
            throw new InvalidOperationException(
                "ADT refused to overwrite the existing bridge path because it is not a normal local file.");
        }
    }

    private static void EnsureDestinationNotInUse(
        string destination)
    {
        try
        {
            using var stream =
                new FileStream(
                    destination,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

            _ =
                stream.Length;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new IOException(
                "The installed ADT bridge DLL is currently in use. Fully exit SimHub, including its tray process, before installing or repairing the bridge.",
                ex);
        }
    }

    private static void EnsureSimHubStopped()
    {
        if (!TryGetSimHubRunningState(
                out var running))
        {
            throw new InvalidOperationException(
                "ADT could not verify whether SimHub is running. Close SimHub and try again before modifying bridge files.");
        }

        if (running)
        {
            throw new InvalidOperationException(
                "SimHub is currently running. Fully exit SimHub, including its tray process, before installing or repairing the ADT bridge.");
        }
    }

    private static bool TryGetSimHubRunningState(
        out bool running)
    {
        running =
            false;

        try
        {
            var processes =
                Process.GetProcessesByName(
                    "SimHubWPF");

            try
            {
                running =
                    processes.Length >
                    0;

                return true;
            }
            finally
            {
                foreach (
                    var process in
                    processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static void ValidatePackagedBridge(
        string path)
    {
        if (!IsRegularFile(
                path))
        {
            throw new FileNotFoundException(
                "This ADT build does not contain a valid packaged bridge payload. Use an installer/portable package produced by the ADT beta packaging pipeline, or install the bridge manually from a developer build.",
                path);
        }

        var info =
            new FileInfo(
                path);

        if (
            info.Length <=
                0 ||
            info.Length >
                MaximumBridgePayloadBytes)
        {
            throw new InvalidDataException(
                $"The packaged ADT SimHub Bridge DLL has an invalid size ({info.Length:N0} bytes).");
        }

        ValidateBridgeAssembly(
            path);
    }

    private static bool TryValidatePackagedBridge(
        string path)
    {
        try
        {
            ValidatePackagedBridge(
                path);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateBridgeAssembly(
        string path)
    {
        try
        {
            if (!IsRegularFile(
                    path))
            {
                return false;
            }

            ValidateBridgeAssembly(
                path);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateBridgeAssembly(
        string path)
    {
        try
        {
            using (
                var stream =
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
            {
                if (
                    stream.Length <
                    2 ||
                    stream.ReadByte() !=
                    'M' ||
                    stream.ReadByte() !=
                    'Z')
                {
                    throw new BadImageFormatException(
                        "The file does not have a Windows PE header.");
                }
            }

            var assemblyName =
                AssemblyName.GetAssemblyName(
                    path);

            if (!string.Equals(
                    assemblyName.Name,
                    ExpectedBridgeAssemblyName,
                    StringComparison.Ordinal))
            {
                throw new BadImageFormatException(
                    $"The DLL assembly name is '{assemblyName.Name ?? "(missing)"}', not '{ExpectedBridgeAssemblyName}'.");
            }

            _ =
                FileVersionInfo.GetVersionInfo(
                    path);
        }
        catch (Exception ex)
            when (
                ex is BadImageFormatException ||
                ex is FileLoadException ||
                ex is FileNotFoundException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "The ADT SimHub Bridge DLL could not be validated as the expected managed bridge assembly.",
                ex);
        }
    }

    private static bool IsRegularFile(
        string? path)
    {
        if (
            string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(
                path))
        {
            return false;
        }

        try
        {
            var attributes =
                File.GetAttributes(
                    path);

            return
                (attributes &
                 FileAttributes.Directory) ==
                    0 &&
                (attributes &
                 FileAttributes.ReparsePoint) ==
                    0;
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
            .ToHexString(
                hash);
    }

    private static bool FixedTimeHashEquals(
        string left,
        string right)
    {
        if (
            !IsSha256Hex(
                left) ||
            !IsSha256Hex(
                right))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(
                left),
            Convert.FromHexString(
                right));
    }

    private static bool IsSha256Hex(
        string? value)
    {
        if (
            string.IsNullOrWhiteSpace(
                value) ||
            value.Length !=
                64)
        {
            return false;
        }

        foreach (
            var character in
            value)
        {
            var valid =
                character is
                    >= '0' and <= '9' or
                    >= 'a' and <= 'f' or
                    >= 'A' and <= 'F';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Best-effort cleanup only. Rollback failures are handled separately
            // and never hidden by temporary-file cleanup.
        }
    }

    private static string FileVersion(
        string? path)
    {
        if (
            string.IsNullOrWhiteSpace(
                path) ||
            !IsRegularFile(
                path))
        {
            return "not installed";
        }

        try
        {
            var version =
                FileVersionInfo
                    .GetVersionInfo(
                        path)
                    .FileVersion;

            return
                string.IsNullOrWhiteSpace(
                    version)
                    ? "present"
                    : version;
        }
        catch
        {
            return "present";
        }
    }

    private static bool SafeIsValidSimHubRoot(
        string? path)
    {
        try
        {
            return
                SimHubLocator.IsValidRoot(
                    path);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        return string.Equals(
            Path.GetFullPath(
                    left)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            Path.GetFullPath(
                    right)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
