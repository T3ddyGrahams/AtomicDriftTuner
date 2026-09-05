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
    private string _simHubRoot =
        string.Empty;

    private string _installedPath =
        string.Empty;

    private string _packagedPath =
        string.Empty;

    private string _installedVersion =
        "unknown";

    private string _packagedVersion =
        "unknown";

    public string SimHubRoot
    {
        get => _simHubRoot;

        set =>
            _simHubRoot =
                value?.Trim() ??
                string.Empty;
    }

    public string InstalledPath
    {
        get => _installedPath;

        set =>
            _installedPath =
                value?.Trim() ??
                string.Empty;
    }

    public string PackagedPath
    {
        get => _packagedPath;

        set =>
            _packagedPath =
                value?.Trim() ??
                string.Empty;
    }

    public bool SimHubValid { get; set; }

    public bool SimHubRunning { get; set; }

    public bool PackagedBridgeAvailable { get; set; }

    public bool BridgeInstalled { get; set; }

    public string InstalledVersion
    {
        get => _installedVersion;

        set =>
            _installedVersion =
                string.IsNullOrWhiteSpace(value)
                    ? "unknown"
                    : value.Trim();
    }

    public string PackagedVersion
    {
        get => _packagedVersion;

        set =>
            _packagedVersion =
                string.IsNullOrWhiteSpace(value)
                    ? "unknown"
                    : value.Trim();
    }
}

public sealed class DiagnosticItem
{
    private string _area =
        string.Empty;

    private string _check =
        string.Empty;

    private string _value =
        string.Empty;

    private string _status =
        string.Empty;

    public string Area
    {
        get => _area;

        set =>
            _area =
                value?.Trim() ??
                string.Empty;
    }

    public string Check
    {
        get => _check;

        set =>
            _check =
                value?.Trim() ??
                string.Empty;
    }

    public string Value
    {
        get => _value;

        set =>
            _value =
                value ??
                string.Empty;
    }

    public string Status
    {
        get => _status;

        set =>
            _status =
                value?.Trim() ??
                string.Empty;
    }
}

public sealed class SystemDiagnosticsReport
{
    private string _schema =
        Services.DistributionInfo.SupportSchema;

    private string _atomicVersion =
        Services.DistributionInfo.Version;

    private string _windowsVersion =
        string.Empty;

    private string _processArchitecture =
        string.Empty;

    private string _dotNetVersion =
        string.Empty;

    private List<DiagnosticItem> _items =
        [];

    public string Schema
    {
        get => _schema;

        set =>
            _schema =
                string.IsNullOrWhiteSpace(value)
                    ? Services.DistributionInfo.SupportSchema
                    : value.Trim();
    }

    public DateTime CapturedUtc { get; set; } =
        DateTime.UtcNow;

    public string AtomicVersion
    {
        get => _atomicVersion;

        set =>
            _atomicVersion =
                string.IsNullOrWhiteSpace(value)
                    ? Services.DistributionInfo.Version
                    : value.Trim();
    }

    public string WindowsVersion
    {
        get => _windowsVersion;

        set =>
            _windowsVersion =
                value?.Trim() ??
                string.Empty;
    }

    public string ProcessArchitecture
    {
        get => _processArchitecture;

        set =>
            _processArchitecture =
                value?.Trim() ??
                string.Empty;
    }

    public string DotNetVersion
    {
        get => _dotNetVersion;

        set =>
            _dotNetVersion =
                value?.Trim() ??
                string.Empty;
    }

    public List<DiagnosticItem> Items
    {
        get => _items;

        set =>
            _items =
                value ??
                [];
    }
}