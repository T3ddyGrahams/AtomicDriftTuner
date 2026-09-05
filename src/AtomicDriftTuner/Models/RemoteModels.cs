namespace AtomicDriftTuner.Models;

public sealed class RemoteTuneContext
{
    private string _wheelbase =
        string.Empty;

    private string _steeringWheel =
        string.Empty;

    private string _driftPack =
        string.Empty;

    private string _car =
        string.Empty;

    private string _intent =
        string.Empty;

    private List<string> _notes =
        [];

    public string Wheelbase
    {
        get => _wheelbase;

        set =>
            _wheelbase =
                value?.Trim() ??
                string.Empty;
    }

    public string SteeringWheel
    {
        get => _steeringWheel;

        set =>
            _steeringWheel =
                value?.Trim() ??
                string.Empty;
    }

    public string DriftPack
    {
        get => _driftPack;

        set =>
            _driftPack =
                value?.Trim() ??
                string.Empty;
    }

    public string Car
    {
        get => _car;

        set =>
            _car =
                value?.Trim() ??
                string.Empty;
    }

    public string Intent
    {
        get => _intent;

        set =>
            _intent =
                value?.Trim() ??
                string.Empty;
    }

    public bool HasGeneratedTune { get; set; }

    public AzomSettings? RecommendedAzom { get; set; }

    public AssettoCorsaSettings? RecommendedAc { get; set; }

    public int SelfSteerScore { get; set; }

    public int StabilityScore { get; set; }

    public int DetailScore { get; set; }

    public double EstimatedPeakWheelTorqueNm { get; set; }

    public List<string> Notes
    {
        get => _notes;

        set =>
            _notes =
                value ??
                [];
    }
}

public sealed class RemoteAzomSettingView
{
    private string _propertyName =
        string.Empty;

    private string _displayName =
        string.Empty;

    private string _unit =
        string.Empty;

    public string PropertyName
    {
        get => _propertyName;

        set =>
            _propertyName =
                value?.Trim() ??
                string.Empty;
    }

    public string DisplayName
    {
        get => _displayName;

        set =>
            _displayName =
                value?.Trim() ??
                string.Empty;
    }

    public int? Current { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }

    public string Unit
    {
        get => _unit;

        set =>
            _unit =
                value?.Trim() ??
                string.Empty;
    }

    public bool Writable { get; set; }
}

public sealed class RemoteAzomWriteRequest
{
    private string _propertyName =
        string.Empty;

    public string PropertyName
    {
        get => _propertyName;

        set =>
            _propertyName =
                value?.Trim() ??
                string.Empty;
    }

    public int Value { get; set; }
}

public sealed class RemoteAzomWriteResponse
{
    private string _propertyName =
        string.Empty;

    private string _message =
        string.Empty;

    public bool Ok { get; set; }

    public bool Verified { get; set; }

    public string PropertyName
    {
        get => _propertyName;

        set =>
            _propertyName =
                value?.Trim() ??
                string.Empty;
    }

    public int? RequestedValue { get; set; }

    public int? LiveValue { get; set; }

    public string Message
    {
        get => _message;

        set =>
            _message =
                value ??
                string.Empty;
    }
}

public sealed class RemoteTelemetrySampleView
{
    public int PacketId { get; set; }

    public double? SpeedKmh { get; set; }

    public double? SlipAngleDeg { get; set; }

    public double? SteeringAngleDeg { get; set; }

    public double? FinalFfb { get; set; }
}

public sealed class RemoteTelemetryView
{
    private string? _error;

    public bool Connected { get; set; }

    public string? Error
    {
        get => _error;

        set =>
            _error =
                string.IsNullOrWhiteSpace(value)
                    ? null
                    : value.Trim();
    }

    public RemoteTelemetrySampleView? Sample { get; set; }

    public bool IsDrifting { get; set; }

    public DateTimeOffset ServerTimeUtc { get; set; } =
        DateTimeOffset.UtcNow;
}

public sealed class RemoteStatusView
{
    private string _atomicVersion =
        string.Empty;

    private string _lastActivity =
        string.Empty;

    private RemoteTuneContext _tune =
        new();

    public string AtomicVersion
    {
        get => _atomicVersion;

        set =>
            _atomicVersion =
                value?.Trim() ??
                string.Empty;
    }

    public bool RemoteWritesEnabled { get; set; }

    public string LastActivity
    {
        get => _lastActivity;

        set =>
            _lastActivity =
                value ??
                string.Empty;
    }

    public RemoteTuneContext Tune
    {
        get => _tune;

        set =>
            _tune =
                value ??
                new RemoteTuneContext();
    }
}

public sealed class RemoteAzomChangedEventArgs : EventArgs
{
    private string _propertyName =
        string.Empty;

    public string PropertyName
    {
        get => _propertyName;

        init =>
            _propertyName =
                value?.Trim() ??
                string.Empty;
    }

    public int? Value { get; init; }

    public bool Verified { get; init; }
}

public sealed class RemoteIntentOption
{
    private string _name =
        string.Empty;

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }

    public bool Selected { get; set; }
}

public sealed class RemoteIntentRequest
{
    private string _name =
        string.Empty;

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }
}

public sealed class RemoteActionResponse
{
    private string _message =
        string.Empty;

    public bool Ok { get; set; }

    public string Message
    {
        get => _message;

        set =>
            _message =
                value ??
                string.Empty;
    }
}

public sealed class RemoteBehaviorView
{
    private string _error =
        string.Empty;

    private string _displayName =
        string.Empty;

    public bool Ok { get; set; }

    public string Error
    {
        get => _error;

        set =>
            _error =
                value ??
                string.Empty;
    }

    public string DisplayName
    {
        get => _displayName;

        set =>
            _displayName =
                value?.Trim() ??
                string.Empty;
    }

    public DateTime UpdatedUtc { get; set; }

    public int FrontEndBite { get; set; }

    public int RearGrip { get; set; }

    public int SelfSteerSpeed { get; set; }

    public int TransitionSpeed { get; set; }

    public int AngleStability { get; set; }

    public int ThrottleSteering { get; set; }

    public int InitiationSharpness { get; set; }
}

public sealed class RemoteBehaviorUpdateRequest
{
    public int FrontEndBite { get; set; }

    public int RearGrip { get; set; }

    public int SelfSteerSpeed { get; set; }

    public int TransitionSpeed { get; set; }

    public int AngleStability { get; set; }

    public int ThrottleSteering { get; set; }

    public int InitiationSharpness { get; set; }
}