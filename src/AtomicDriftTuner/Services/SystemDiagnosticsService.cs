using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class SystemDiagnosticsService
{
    private const int MaximumLogFiles =
        10;

    private const long MaximumLogBytesPerFile =
        1024 * 1024;

    private const long MaximumSupportPackageLogBytes =
        5 * 1024 * 1024;

    private const int MaximumDiagnosticValueLength =
        2048;

    private readonly AppSettingsStore _settingsStore =
        new();

    private readonly MachineConfigurationService _machine =
        new();

    private readonly BridgeManagerService _bridge =
        new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented =
                true
        };

    public async Task<SystemDiagnosticsReport> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings =
            _settingsStore.Load();

        var detection =
            _machine.Detect(
                settings);

        var report =
            new SystemDiagnosticsReport
            {
                WindowsVersion =
                    Environment.OSVersion.VersionString,

                ProcessArchitecture =
                    RuntimeInformation.ProcessArchitecture.ToString(),

                DotNetVersion =
                    Environment.Version.ToString()
            };

        Add(
            report,
            "ADT",
            "Build",
            DistributionInfo.Version,
            "OK");

        Add(
            report,
            "ADT",
            "Data folder",
            RedactPath(
                AdtDataFolder()),
            "OK");

        Add(
            report,
            "SimHub",
            "Install",
            RedactPath(
                detection.SimHubRoot),
            detection.SimHubValid
                ? "OK"
                : "NOT FOUND");

        CollectBridgeStatus(
            report,
            detection.SimHubRoot);

        Add(
            report,
            "Live AZOM",
            "ADT write safety",
            "Serialized explicit apply, write spacing, duplicate suppression, interactive debounce, readback verification, and stop-on-failure handling",
            "ENABLED");

        CollectAssettoCorsaInstallation(
            report,
            detection.AssettoCorsaRoot,
            detection.AssettoCorsaValid);

        Add(
            report,
            "Assetto Corsa",
            "User data",
            RedactPath(
                detection.AssettoCorsaDocumentsRoot),
            detection.AssettoCorsaDocumentsValid
                ? "OK"
                : "NOT CREATED / NOT FOUND");

        CollectTelemetryStatus(
            report);

        await CollectAzomStatusAsync(
            report,
            settings.AzomLive?.PipeName ??
            "AtomicDriftTuner.AzomBridge.v1",
            cancellationToken);

        return report;
    }

    public string ToPlainText(
        SystemDiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(
            report);

        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"Atomic Drift Tuner {DistributionInfo.DisplayVersion}");

        builder.AppendLine(
            $"Captured: {report.CapturedUtc:O}");

        builder.AppendLine(
            $"Windows: {SanitizeDiagnosticValue(report.WindowsVersion)}");

        builder.AppendLine(
            $"Architecture: {SanitizeDiagnosticValue(report.ProcessArchitecture)}");

        builder.AppendLine(
            $".NET: {SanitizeDiagnosticValue(report.DotNetVersion)}");

        builder.AppendLine();

        foreach (var item in report.Items)
        {
            if (item is null)
            {
                continue;
            }

            builder.AppendLine(
                $"[{SanitizeDiagnosticValue(item.Status)}] " +
                $"{SanitizeDiagnosticValue(item.Area)} / " +
                $"{SanitizeDiagnosticValue(item.Check)}: " +
                $"{SanitizeDiagnosticValue(item.Value)}");
        }

        return builder.ToString();
    }

    public async Task<string> ExportSupportPackageAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                outputPath))
        {
            throw new ArgumentException(
                "Support package output path is required.",
                nameof(outputPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var report =
            await CollectAsync(
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var settings =
            _settingsStore.Load();

        var outputFull =
            NormalizeOutputPath(
                outputPath);

        var outputDirectory =
            Path.GetDirectoryName(
                outputFull)
            ?? throw new InvalidOperationException(
                "Invalid support package destination.");

        Directory.CreateDirectory(
            outputDirectory);

        var temporaryPath =
            Path.Combine(
                outputDirectory,
                $".{Path.GetFileName(outputFull)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.WriteThrough))
            using (
                var archive =
                    new ZipArchive(
                        stream,
                        ZipArchiveMode.Create,
                        leaveOpen: false))
            {
                WriteTextEntry(
                    archive,
                    "diagnostics.json",
                    JsonSerializer.Serialize(
                        CreateRedactedReport(
                            report),
                        Json));

                WriteTextEntry(
                    archive,
                    "diagnostics.txt",
                    RedactText(
                        ToPlainText(
                            report)));

                var redactedSettings =
                    new
                    {
                        settings.FirstRunCompleted,

                        AssettoCorsaRoot =
                            RedactPath(
                                settings.AssettoCorsaRoot),

                        AssettoCorsaDocumentsRoot =
                            RedactPath(
                                settings.AssettoCorsaDocumentsRoot),

                        SimHubRoot =
                            RedactPath(
                                settings.SimHubRoot),

                        AzomLive =
                            new
                            {
                                SimHubExePath =
                                    RedactPath(
                                        settings.AzomLive?.SimHubExePath),

                                PipeName =
                                    NormalizeDiagnosticSetting(
                                        settings.AzomLive?.PipeName),

                                settings.AzomLive?.ActionDelayMs
                            },

                        Theme =
                            new
                            {
                                PresetName =
                                    NormalizeDiagnosticSetting(
                                        settings.Theme?.PresetName)
                            }
                    };

                WriteTextEntry(
                    archive,
                    "settings-redacted.json",
                    JsonSerializer.Serialize(
                        redactedSettings,
                        Json));

                cancellationToken.ThrowIfCancellationRequested();

                AddLogs(
                    archive,
                    cancellationToken);

                WriteTextEntry(
                    archive,
                    "privacy-note.txt",
                    BuildPrivacyNote());

                WriteTextEntry(
                    archive,
                    "package-info.txt",
                    BuildPackageInfo());
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                outputFull,
                overwrite: true);

            return outputFull;
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    public static string RedactPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return "(not set)";
        }

        return RedactText(
            path);
    }

    private void CollectBridgeStatus(
        SystemDiagnosticsReport report,
        string? simHubRoot)
    {
        try
        {
            var bridgeStatus =
                _bridge.GetStatus(
                    simHubRoot);

            Add(
                report,
                "SimHub",
                "Bridge file",
                bridgeStatus.BridgeInstalled
                    ? $"{bridgeStatus.InstalledVersion} • {RedactPath(bridgeStatus.InstalledPath)}"
                    : RedactPath(
                        bridgeStatus.InstalledPath),
                bridgeStatus.BridgeInstalled
                    ? "INSTALLED"
                    : "MISSING");

            Add(
                report,
                "Distribution",
                "Packaged bridge payload",
                bridgeStatus.PackagedBridgeAvailable
                    ? $"{bridgeStatus.PackagedVersion} • available"
                    : "not present in this build",
                bridgeStatus.PackagedBridgeAvailable
                    ? "OK"
                    : "DEV BUILD");
        }
        catch (Exception ex)
            when (IsRecoverableDiagnosticException(
                ex))
        {
            Add(
                report,
                "SimHub",
                "Bridge status",
                SafeExceptionMessage(
                    ex),
                "CHECK");
        }
    }

    private static void CollectAssettoCorsaInstallation(
        SystemDiagnosticsReport report,
        string? assettoCorsaRoot,
        bool assettoCorsaValid)
    {
        Add(
            report,
            "Assetto Corsa",
            "Install",
            RedactPath(
                assettoCorsaRoot),
            assettoCorsaValid
                ? "OK"
                : "NOT FOUND");

        if (!assettoCorsaValid)
        {
            Add(
                report,
                "Assetto Corsa",
                "Installed cars",
                "not checked",
                "N/A");

            return;
        }

        try
        {
            var carsDirectory =
                Path.Combine(
                    assettoCorsaRoot!,
                    "content",
                    "cars");

            var options =
                new EnumerationOptions
                {
                    RecurseSubdirectories =
                        false,

                    IgnoreInaccessible =
                        true,

                    AttributesToSkip =
                        FileAttributes.ReparsePoint
                };

            var carCount =
                Directory
                    .EnumerateDirectories(
                        carsDirectory,
                        "*",
                        options)
                    .Count();

            Add(
                report,
                "Assetto Corsa",
                "Installed cars",
                carCount.ToString(),
                "OK");
        }
        catch (Exception ex)
            when (IsRecoverableDiagnosticException(
                ex))
        {
            Add(
                report,
                "Assetto Corsa",
                "Installed cars",
                SafeExceptionMessage(
                    ex),
                "UNAVAILABLE");
        }
    }

    private static void CollectTelemetryStatus(
        SystemDiagnosticsReport report)
    {
        try
        {
            using var telemetry =
                new AssettoCorsaTelemetryReader();

            var telemetryAvailable =
                telemetry.TryConnect();

            Add(
                report,
                "Assetto Corsa",
                "Live telemetry shared memory",
                telemetryAvailable
                    ? @"Local\acpmf_physics available"
                    : "not currently available",
                telemetryAvailable
                    ? "CONNECTED"
                    : "OFF-TRACK");
        }
        catch (Exception ex)
            when (IsRecoverableDiagnosticException(
                ex))
        {
            Add(
                report,
                "Assetto Corsa",
                "Live telemetry shared memory",
                SafeExceptionMessage(
                    ex),
                "UNAVAILABLE");
        }
    }

    private static async Task CollectAzomStatusAsync(
        SystemDiagnosticsReport report,
        string pipeName,
        CancellationToken cancellationToken)
    {
        try
        {
            var live =
                await new AzomBridgeClient(
                        pipeName)
                    .ReadSnapshotAsync(
                        1400,
                        cancellationToken);

            Add(
                report,
                "Live AZOM",
                "Bridge connection",
                $"bridge {live.BridgeVersion} • source {live.ReadSource}",
                "CONNECTED");

            Add(
                report,
                "Live AZOM",
                "AZOM settings",
                $"namespace {live.PropertyNamespace} • {live.SettingsPropertyCount} readable settings",
                live.SettingsReadable
                    ? "READABLE"
                    : "NOT READABLE");

            Add(
                report,
                "Live AZOM",
                "Wheelbase",
                live.BaseConnected == true
                    ? "connected"
                    : "not reported / disconnected",
                live.BaseConnected == true
                    ? "CONNECTED"
                    : "CHECK");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (IsRecoverableDiagnosticException(
                ex))
        {
            Add(
                report,
                "Live AZOM",
                "Bridge connection",
                SafeExceptionMessage(
                    ex),
                "NOT CONNECTED");
        }
    }

    private static void AddLogs(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var logsDirectory =
            Path.Combine(
                AdtDataFolder(),
                "Logs");

        if (!Directory.Exists(
                logsDirectory))
        {
            return;
        }

        List<FileInfo> logs;

        try
        {
            var options =
                new EnumerationOptions
                {
                    RecurseSubdirectories =
                        false,

                    IgnoreInaccessible =
                        true,

                    AttributesToSkip =
                        FileAttributes.ReparsePoint
                };

            logs =
                Directory
                    .EnumerateFiles(
                        logsDirectory,
                        "*.log",
                        options)
                    .Select(
                        path =>
                            new FileInfo(
                                path))
                    .Where(
                        info =>
                            info.Exists &&
                            !IsReparsePoint(
                                info))
                    .OrderByDescending(
                        info =>
                            SafeLastWriteTimeUtc(
                                info))
                    .Take(
                        MaximumLogFiles)
                    .ToList();
        }
        catch (Exception ex)
            when (IsRecoverableDiagnosticException(
                ex))
        {
            return;
        }

        long includedBytes =
            0;

        foreach (var log in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (includedBytes >=
                MaximumSupportPackageLogBytes)
            {
                break;
            }

            try
            {
                var remainingBudget =
                    MaximumSupportPackageLogBytes -
                    includedBytes;

                var maximumForThisFile =
                    Math.Min(
                        MaximumLogBytesPerFile,
                        remainingBudget);

                if (maximumForThisFile <= 0)
                {
                    break;
                }

                var text =
                    ReadLogTail(
                        log.FullName,
                        maximumForThisFile);

                text =
                    RedactText(
                        text);

                if (log.Length >
                    maximumForThisFile)
                {
                    text =
                        $"[ADT support export: log truncated to the newest {maximumForThisFile:N0} bytes.]" +
                        Environment.NewLine +
                        text;
                }

                var safeName =
                    SafeArchiveFileName(
                        log.Name);

                WriteTextEntry(
                    archive,
                    $"logs/{safeName}",
                    text);

                includedBytes +=
                    Math.Min(
                        log.Length,
                        maximumForThisFile);
            }
            catch (Exception ex)
                when (IsRecoverableDiagnosticException(
                    ex))
            {
                // One locked or unreadable log must not prevent the support
                // package from being created.
            }
        }
    }

    private static object CreateRedactedReport(
        SystemDiagnosticsReport report)
    {
        return new
        {
            report.CapturedUtc,

            WindowsVersion =
                RedactText(
                    report.WindowsVersion ?? string.Empty),

            ProcessArchitecture =
                RedactText(
                    report.ProcessArchitecture ?? string.Empty),

            DotNetVersion =
                RedactText(
                    report.DotNetVersion ?? string.Empty),

            Items =
                report.Items
                    .Where(
                        item =>
                            item is not null)
                    .Select(
                        item =>
                            new
                            {
                                Area =
                                    SanitizeDiagnosticValue(
                                        RedactText(
                                            item.Area ?? string.Empty)),

                                Check =
                                    SanitizeDiagnosticValue(
                                        RedactText(
                                            item.Check ?? string.Empty)),

                                Value =
                                    SanitizeDiagnosticValue(
                                        RedactText(
                                            item.Value ?? string.Empty)),

                                Status =
                                    SanitizeDiagnosticValue(
                                        RedactText(
                                            item.Status ?? string.Empty))
                            })
                    .ToList()
        };
    }

    private static string ReadLogTail(
        string path,
        long maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            return string.Empty;
        }

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        var start =
            Math.Max(
                0,
                stream.Length -
                maximumBytes);

        stream.Seek(
            start,
            SeekOrigin.Begin);

        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: start == 0);

        return reader.ReadToEnd();
    }

    private static string BuildPrivacyNote()
    {
        return
            "Atomic Drift Tuner support package privacy information" +
            Environment.NewLine +
            Environment.NewLine +
            "ADT attempts to redact the current Windows user-profile path, LocalAppData path, username, and computer name from included diagnostic text and logs." +
            Environment.NewLine +
            Environment.NewLine +
            "The package intentionally does not include:" +
            Environment.NewLine +
            "- saved ADT tune profiles" +
            Environment.NewLine +
            "- telemetry CSV/session data" +
            Environment.NewLine +
            "- Assetto Corsa setup files" +
            Environment.NewLine +
            "- Desired Behavior profile contents" +
            Environment.NewLine +
            "- arbitrary files from Assetto Corsa or SimHub folders" +
            Environment.NewLine +
            Environment.NewLine +
            "Log inclusion is bounded and limited to recent ADT .log files. Logs are text-redacted before being added. Because free-form log messages can contain unexpected data, users should still review a support package before sharing it publicly.";
    }

    private static string BuildPackageInfo()
    {
        return
            $"ADT version: {DistributionInfo.DisplayVersion}" +
            Environment.NewLine +
            $"Created UTC: {DateTime.UtcNow:O}" +
            Environment.NewLine +
            $"Maximum log files: {MaximumLogFiles}" +
            Environment.NewLine +
            $"Maximum bytes per log: {MaximumLogBytesPerFile}" +
            Environment.NewLine +
            $"Maximum combined log bytes: {MaximumSupportPackageLogBytes}";
    }

    private static string NormalizeOutputPath(
        string path)
    {
        try
        {
            return Path.GetFullPath(
                Environment
                    .ExpandEnvironmentVariables(
                        path
                            .Trim()
                            .Trim('"')));
        }
        catch (Exception ex)
            when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            throw new InvalidDataException(
                "ADT could not normalize the support package output path.",
                ex);
        }
    }

    private static string RedactText(
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return value;
        }

        var result =
            value;

        var userProfile =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        result =
            ReplaceSensitiveToken(
                result,
                localAppData,
                "%LOCALAPPDATA%");

        result =
            ReplaceSensitiveToken(
                result,
                userProfile,
                "%USERPROFILE%");

        // Replace these after full profile paths so common path fragments are
        // redacted using the more useful path tokens first.
        var userName =
            Environment.UserName;

        if (!string.IsNullOrWhiteSpace(
                userName) &&
            userName.Length >= 3)
        {
            result =
                ReplaceSensitiveToken(
                    result,
                    userName,
                    "%USERNAME%");
        }

        var machineName =
            Environment.MachineName;

        if (!string.IsNullOrWhiteSpace(
                machineName) &&
            machineName.Length >= 3)
        {
            result =
                ReplaceSensitiveToken(
                    result,
                    machineName,
                    "%COMPUTERNAME%");
        }

        return result;
    }

    private static string ReplaceSensitiveToken(
        string source,
        string? sensitive,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(
                sensitive))
        {
            return source;
        }

        return source.Replace(
            sensitive,
            replacement,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeExceptionMessage(
        Exception exception)
    {
        var message =
            exception switch
            {
                UnauthorizedAccessException =>
                    "Access denied while checking this component.",

                FileNotFoundException =>
                    "A required file was not found.",

                DirectoryNotFoundException =>
                    "A required folder was not found.",

                IOException =>
                    "A file, folder, process, or IPC resource could not be accessed.",

                TimeoutException =>
                    "The diagnostic check timed out.",

                _ =>
                    string.IsNullOrWhiteSpace(
                        exception.Message)
                        ? "The diagnostic check failed."
                        : exception.Message
            };

        return SanitizeDiagnosticValue(
            RedactText(
                message));
    }

    private static string SanitizeDiagnosticValue(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var builder =
            new StringBuilder(
                Math.Min(
                    value.Length,
                    MaximumDiagnosticValueLength));

        var previousWasSpace =
            false;

        foreach (var character in value)
        {
            if (builder.Length >=
                MaximumDiagnosticValueLength)
            {
                break;
            }

            if (
                character is '\r' or '\n' or '\t' ||
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
                character == ' ';
        }

        return builder
            .ToString()
            .Trim();
    }

    private static string? NormalizeDiagnosticSetting(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        return SanitizeDiagnosticValue(
            RedactText(
                value));
    }

    private static string SafeArchiveFileName(
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            return
                $"adt-log-{Guid.NewGuid():N}.log";
        }

        var fileName =
            Path.GetFileName(
                name);

        var cleaned =
            new string(
                fileName
                    .Where(
                        character =>
                            !char.IsControl(
                                character) &&
                            character != '/' &&
                            character != '\\')
                    .ToArray());

        return string.IsNullOrWhiteSpace(
                cleaned)
            ? $"adt-log-{Guid.NewGuid():N}.log"
            : cleaned;
    }

    private static DateTime SafeLastWriteTimeUtc(
        FileInfo file)
    {
        try
        {
            return file.LastWriteTimeUtc;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool IsReparsePoint(
        FileInfo file)
    {
        try
        {
            return (
                file.Attributes &
                FileAttributes.ReparsePoint
            ) !=
            0;
        }
        catch
        {
            return true;
        }
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
            // Cleanup failure must not hide the original export result.
        }
    }

    private static bool IsRecoverableDiagnosticException(
        Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            TimeoutException or
            System.ComponentModel.Win32Exception;
    }

    private static string AdtDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AtomicDriftTuner");
    }

    private static void Add(
        SystemDiagnosticsReport report,
        string area,
        string check,
        string value,
        string status)
    {
        report.Items.Add(
            new DiagnosticItem
            {
                Area =
                    SanitizeDiagnosticValue(
                        area),

                Check =
                    SanitizeDiagnosticValue(
                        check),

                Value =
                    SanitizeDiagnosticValue(
                        RedactText(
                            value)),

                Status =
                    SanitizeDiagnosticValue(
                        status)
            });
    }

    private static void WriteTextEntry(
        ZipArchive archive,
        string name,
        string text)
    {
        var entry =
            archive.CreateEntry(
                name,
                CompressionLevel.Optimal);

        using var stream =
            entry.Open();

        using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

        writer.Write(
            text);
    }
}
