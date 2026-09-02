namespace AtomicDriftTuner.Models;

public sealed class MachineDetectionResult
{
    public string? SimHubRoot { get; set; }
    public string? AssettoCorsaRoot { get; set; }
    public string? AssettoCorsaDocumentsRoot { get; set; }

    public bool SimHubValid { get; set; }
    public bool AssettoCorsaValid { get; set; }
    public bool AssettoCorsaDocumentsValid { get; set; }
}

public sealed class BridgeInstallStatus
{
    public string SimHubRoot { get; set; } = "";
    public string InstalledPath { get; set; } = "";
    public string PackagedPath { get; set; } = "";
    public bool SimHubValid { get; set; }
    public bool SimHubRunning { get; set; }
    public bool PackagedBridgeAvailable { get; set; }
    public bool BridgeInstalled { get; set; }
    public string InstalledVersion { get; set; } = "unknown";
    public string PackagedVersion { get; set; } = "unknown";
}

public sealed class DiagnosticItem
{
    public string Area { get; set; } = "";
    public string Check { get; set; } = "";
    public string Value { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class SystemDiagnosticsReport
{
    public string Schema { get; set; } = Services.DistributionInfo.SupportSchema;
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public string AtomicVersion { get; set; } = Services.DistributionInfo.Version;
    public string WindowsVersion { get; set; } = "";
    public string ProcessArchitecture { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public List<DiagnosticItem> Items { get; set; } = [];
}
