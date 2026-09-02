using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class SystemDiagnosticsService
{
    private readonly AppSettingsStore _settingsStore = new();
    private readonly MachineConfigurationService _machine = new();
    private readonly BridgeManagerService _bridge = new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented = true
        };

    public async Task<SystemDiagnosticsReport> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Load();
        var detection = _machine.Detect(settings);
        var report = new SystemDiagnosticsReport
        {
            WindowsVersion = Environment.OSVersion.VersionString,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            DotNetVersion = Environment.Version.ToString()
        };

        Add(report, "Atomic", "Build", DistributionInfo.Version, "OK");
        Add(report, "Atomic", "Data folder", RedactPath(AtomicDataFolder()), "OK");

        Add(
            report,
            "SimHub",
            "Install",
            RedactPath(detection.SimHubRoot),
            detection.SimHubValid ? "OK" : "NOT FOUND");

        var bridgeStatus = _bridge.GetStatus(detection.SimHubRoot);
        Add(
            report,
            "SimHub",
            "Bridge file",
            bridgeStatus.BridgeInstalled
                ? $"{bridgeStatus.InstalledVersion} • {RedactPath(bridgeStatus.InstalledPath)}"
                : RedactPath(bridgeStatus.InstalledPath),
            bridgeStatus.BridgeInstalled ? "INSTALLED" : "MISSING");

        Add(
            report,
            "Live AZOM",
            "Atomic write guard",
            "Explicit Apply single-flight • 120 ms minimum direct-write spacing • duplicate suppression • 500 ms interactive debounce API",
            "ENABLED");

        Add(
            report,
            "Distribution",
            "Packaged bridge payload",
            bridgeStatus.PackagedBridgeAvailable
                ? $"{bridgeStatus.PackagedVersion} • available"
                : "not present in this build",
            bridgeStatus.PackagedBridgeAvailable ? "OK" : "DEV BUILD");

        Add(
            report,
            "Assetto Corsa",
            "Install",
            RedactPath(detection.AssettoCorsaRoot),
            detection.AssettoCorsaValid ? "OK" : "NOT FOUND");

        int carCount = 0;
        if (detection.AssettoCorsaValid)
        {
            try
            {
                carCount =
                    Directory.EnumerateDirectories(
                        Path.Combine(
                            detection.AssettoCorsaRoot!,
                            "content",
                            "cars"))
                    .Count();
            }
            catch { }
        }

        Add(
            report,
            "Assetto Corsa",
            "Installed cars",
            carCount.ToString(),
            detection.AssettoCorsaValid ? "OK" : "N/A");

        Add(
            report,
            "Assetto Corsa",
            "User data",
            RedactPath(detection.AssettoCorsaDocumentsRoot),
            detection.AssettoCorsaDocumentsValid ? "OK" : "NOT CREATED / NOT FOUND");

        using (var telemetry = new AssettoCorsaTelemetryReader())
        {
            bool telemetryAvailable = telemetry.TryConnect();
            Add(
                report,
                "Assetto Corsa",
                "Live telemetry shared memory",
                telemetryAvailable ? "Local\\acpmf_physics available" : "not currently available",
                telemetryAvailable ? "CONNECTED" : "OFF-TRACK");
        }

        try
        {
            var live =
                await new AzomBridgeClient(
                    settings.AzomLive?.PipeName ??
                    "AtomicDriftTuner.AzomBridge.v1")
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
                live.SettingsReadable ? "READABLE" : "NOT READABLE");

            Add(
                report,
                "Live AZOM",
                "Wheelbase",
                live.BaseConnected == true ? "connected" : "not reported / disconnected",
                live.BaseConnected == true ? "CONNECTED" : "CHECK");
        }
        catch (Exception ex)
        {
            Add(
                report,
                "Live AZOM",
                "Bridge connection",
                ex.Message,
                "NOT CONNECTED");
        }

        return report;
    }

    public string ToPlainText(SystemDiagnosticsReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Atomic Drift Tuner {DistributionInfo.DisplayVersion}");
        sb.AppendLine($"Captured: {report.CapturedUtc:O}");
        sb.AppendLine($"Windows: {report.WindowsVersion}");
        sb.AppendLine($"Architecture: {report.ProcessArchitecture}");
        sb.AppendLine($".NET: {report.DotNetVersion}");
        sb.AppendLine();

        foreach (var item in report.Items)
        {
            sb.AppendLine(
                $"[{item.Status}] {item.Area} / {item.Check}: {item.Value}");
        }

        return sb.ToString();
    }

    public async Task<string> ExportSupportPackageAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var report = await CollectAsync(cancellationToken);
        var settings = _settingsStore.Load();

        var outputFull = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(outputFull) ??
            throw new InvalidOperationException("Invalid support package destination."));

        if (File.Exists(outputFull))
            File.Delete(outputFull);

        using var archive =
            ZipFile.Open(
                outputFull,
                ZipArchiveMode.Create);

        WriteTextEntry(
            archive,
            "diagnostics.json",
            JsonSerializer.Serialize(report, Json));

        WriteTextEntry(
            archive,
            "diagnostics.txt",
            ToPlainText(report));

        var redactedSettings = new
        {
            settings.FirstRunCompleted,
            AssettoCorsaRoot = RedactPath(settings.AssettoCorsaRoot),
            AssettoCorsaDocumentsRoot = RedactPath(settings.AssettoCorsaDocumentsRoot),
            SimHubRoot = RedactPath(settings.SimHubRoot),
            AzomLive = new
            {
                SimHubExePath = RedactPath(settings.AzomLive?.SimHubExePath),
                settings.AzomLive?.PipeName,
                settings.AzomLive?.ActionDelayMs
            },
            Theme = new
            {
                settings.Theme?.PresetName
            }
        };

        WriteTextEntry(
            archive,
            "settings-redacted.json",
            JsonSerializer.Serialize(redactedSettings, Json));

        var logs =
            Path.Combine(
                AtomicDataFolder(),
                "Logs");

        if (Directory.Exists(logs))
        {
            foreach (var file in Directory.EnumerateFiles(logs, "*.log"))
            {
                try
                {
                    var name =
                        "logs/" +
                        Path.GetFileName(file);

                    var text =
                        RedactText(
                            File.ReadAllText(file));

                    WriteTextEntry(
                        archive,
                        name,
                        text);
                }
                catch
                {
                    // A locked log should not prevent support export.
                }
            }
        }

        WriteTextEntry(
            archive,
            "privacy-note.txt",
            "Atomic Drift Tuner support packages redact the current Windows user profile and LocalAppData path tokens. " +
            "They intentionally do not include saved tune profiles, telemetry CSV files, Assetto Corsa setup files, or car-behavior profile contents.");

        return outputFull;
    }

    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(not set)";

        return RedactText(path);
    }

    private static string RedactText(string value)
    {
        var result = value;

        var user =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        var local =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(local))
            result =
                result.Replace(
                    local,
                    "%LOCALAPPDATA%",
                    StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(user))
            result =
                result.Replace(
                    user,
                    "%USERPROFILE%",
                    StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static string AtomicDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AtomicDriftTuner");

    private static void Add(
        SystemDiagnosticsReport report,
        string area,
        string check,
        string value,
        string status) =>
        report.Items.Add(
            new DiagnosticItem
            {
                Area = area,
                Check = check,
                Value = value,
                Status = status
            });

    private static void WriteTextEntry(
        ZipArchive archive,
        string name,
        string text)
    {
        var entry =
            archive.CreateEntry(
                name,
                CompressionLevel.Optimal);

        using var stream = entry.Open();
        using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(false));

        writer.Write(text);
    }
}
