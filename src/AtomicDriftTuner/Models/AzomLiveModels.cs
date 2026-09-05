using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public sealed class AzomLiveConnectionSettings
{
    private string _pipeName =
        "AtomicDriftTuner.AzomBridge.v1";

    public string? SimHubExePath { get; set; }

    public string PipeName
    {
        get => _pipeName;

        set =>
            _pipeName =
                string.IsNullOrWhiteSpace(value)
                    ? "AtomicDriftTuner.AzomBridge.v1"
                    : value.Trim();
    }

    public int ActionDelayMs { get; set; } =
        70;
}

public sealed class AzomLiveSnapshot
{
    private string _bridgeVersion =
        "unknown";

    private string _propertyNamespace =
        string.Empty;

    private string _readSource =
        string.Empty;

    private List<string> _publishedProperties =
        [];

    public string BridgeVersion
    {
        get => _bridgeVersion;

        set =>
            _bridgeVersion =
                string.IsNullOrWhiteSpace(value)
                    ? "unknown"
                    : value.Trim();
    }

    public DateTime CapturedUtc { get; set; } =
        DateTime.UtcNow;

    public bool AzomAvailable { get; set; }

    public bool PluginDetected { get; set; }

    public bool SettingsReadable { get; set; }

    public string PropertyNamespace
    {
        get => _propertyNamespace;

        set =>
            _propertyNamespace =
                value?.Trim() ??
                string.Empty;
    }

    public string ReadSource
    {
        get => _readSource;

        set =>
            _readSource =
                value?.Trim() ??
                string.Empty;
    }

    public bool? BaseConnected { get; set; }

    public int AzomPropertyCount { get; set; }

    public int LegacyMozaPropertyCount { get; set; }

    public int SettingsPropertyCount { get; set; }

    public List<string> PublishedProperties
    {
        get => _publishedProperties;

        set =>
            _publishedProperties =
                value ??
                [];
    }

    public int? FfbStrength { get; set; }

    public int? Torque { get; set; }

    public int? Rotation { get; set; }

    public int? WheelSpeedLimit { get; set; }

    public int? Interpolation { get; set; }

    public int? GearshiftVibration { get; set; }

    public int? Damper { get; set; }

    public int? Friction { get; set; }

    public int? Inertia { get; set; }

    public int? Spring { get; set; }

    public int? GameDamper { get; set; }

    public int? GameFriction { get; set; }

    public int? GameInertia { get; set; }

    public int? GameSpring { get; set; }

    public int? NaturalInertia { get; set; }

    public int? SoftLimitStiffness { get; set; }

    public int? SpeedDamping { get; set; }

    public int? SpeedDampingPoint { get; set; }

    public int? RoadSensitivity { get; set; }

    public int? Equalizer1 { get; set; }

    public int? Equalizer2 { get; set; }

    public int? Equalizer3 { get; set; }

    public int? Equalizer4 { get; set; }

    public int? Equalizer5 { get; set; }

    public int? Equalizer6 { get; set; }

    public int? Equalizer7 { get; set; }

    public int? Equalizer8 { get; set; }

    public int? Equalizer9 { get; set; }

    public int? Equalizer10 { get; set; }

    public int? FfbCurveX1 { get; set; }

    public int? FfbCurveX2 { get; set; }

    public int? FfbCurveX3 { get; set; }

    public int? FfbCurveX4 { get; set; }

    public int? FfbCurveY1 { get; set; }

    public int? FfbCurveY2 { get; set; }

    public int? FfbCurveY3 { get; set; }

    public int? FfbCurveY4 { get; set; }

    public int? FfbCurveY5 { get; set; }

    public bool? Protection { get; set; }

    public bool? FfbReverse { get; set; }

    public bool? SoftLimitRetain { get; set; }

    public bool? PerformanceOutput { get; set; }

    public bool? BaseStatusLed { get; set; }

    public bool? Bluetooth { get; set; }

    public int? WorkMode { get; set; }

    [JsonIgnore]
    public bool HasLegacySixBandEqualizer =>
        !Equalizer7.HasValue ||
        Equalizer7.Value < 0;
}

public enum AzomApplyItemKind
{
    Numeric,
    Toggle,
    Unsupported
}

public sealed class AzomApplyPlanItem
{
    public string Group { get; set; } =
        string.Empty;

    public string DisplayName { get; set; } =
        string.Empty;

    public string PropertyName { get; set; } =
        string.Empty;

    public AzomApplyItemKind Kind { get; set; }

    public string CurrentDisplay { get; set; } =
        "N/A";

    public string TargetDisplay { get; set; } =
        "N/A";

    public int? CurrentInt { get; set; }

    public int? TargetInt { get; set; }

    public bool? CurrentBool { get; set; }

    public bool? TargetBool { get; set; }

    public string? ActionBase { get; set; }

    public string? ToggleAction { get; set; }

    public int FineStep { get; set; }

    public int CoarseStep { get; set; }

    public bool CanApply { get; set; }

    public string Note { get; set; } =
        string.Empty;

    public int EstimatedActions { get; set; }

    public bool IsSelectedForApply { get; set; }

    [JsonIgnore]
    public bool IsDifferent =>
        Kind switch
        {
            AzomApplyItemKind.Numeric =>
                CurrentInt.HasValue &&
                TargetInt.HasValue &&
                CurrentInt.Value != TargetInt.Value,

            AzomApplyItemKind.Toggle =>
                CurrentBool.HasValue &&
                TargetBool.HasValue &&
                CurrentBool.Value != TargetBool.Value,

            _ =>
                false
        };
}

public sealed class AzomApplyResult
{
    private List<string> _warnings =
        [];

    private List<AzomApplyAuditItem> _audit =
        [];

    public int ActionsTriggered { get; set; }

    public int SettingsChanged { get; set; }

    public int VerifiedSettingsChanged { get; set; }

    public int BridgeActionsTriggered { get; set; }

    public int CliFallbackActionsTriggered { get; set; }

    public int DirectFallbackSettingsTriggered { get; set; }

    public List<string> Warnings
    {
        get => _warnings;

        set =>
            _warnings =
                value ??
                [];
    }

    public AzomLiveSnapshot? After { get; set; }

    public List<AzomApplyAuditItem> Audit
    {
        get => _audit;

        set =>
            _audit =
                value ??
                [];
    }
}

public sealed class AzomApplyAuditItem
{
    public string Group { get; set; } =
        string.Empty;

    public string Setting { get; set; } =
        string.Empty;

    public string Before { get; set; } =
        "N/A";

    public string Target { get; set; } =
        "N/A";

    public string After { get; set; } =
        "N/A";

    public bool Verified { get; set; }

    public string Transport { get; set; } =
        string.Empty;

    public string Note { get; set; } =
        string.Empty;
}

public sealed class AzomRevertRecord
{
    public const string CurrentSchema =
        "atomic-drift-tuner/azom-revert/v1";

    private string _schema =
        CurrentSchema;

    private AzomLiveSnapshot _snapshot =
        new();

    private List<string> _changedProperties =
        [];

    public string Schema
    {
        get => _schema;

        set =>
            _schema =
                string.IsNullOrWhiteSpace(value)
                    ? CurrentSchema
                    : value.Trim();
    }

    public DateTime SavedUtc { get; set; } =
        DateTime.UtcNow;

    public AzomLiveSnapshot Snapshot
    {
        get => _snapshot;

        set =>
            _snapshot =
                value ??
                new AzomLiveSnapshot();
    }

    public List<string> ChangedProperties
    {
        get => _changedProperties;

        set =>
            _changedProperties =
                value ??
                [];
    }
}