namespace AtomicDriftTuner.Models;

public sealed class RemoteTuneContext
{
    public string Wheelbase { get; set; } = "";
    public string SteeringWheel { get; set; } = "";
    public string DriftPack { get; set; } = "";
    public string Car { get; set; } = "";
    public string Intent { get; set; } = "";
    public bool HasGeneratedTune { get; set; }
    public AzomSettings? RecommendedAzom { get; set; }
    public AssettoCorsaSettings? RecommendedAc { get; set; }
    public int SelfSteerScore { get; set; }
    public int StabilityScore { get; set; }
    public int DetailScore { get; set; }
    public double EstimatedPeakWheelTorqueNm { get; set; }
    public List<string> Notes { get; set; } = [];
}

public sealed class RemoteAzomSettingView
{
    public string PropertyName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int? Current { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public string Unit { get; set; } = "";
    public bool Writable { get; set; }
}

public sealed class RemoteAzomWriteRequest
{
    public string PropertyName { get; set; } = "";
    public int Value { get; set; }
}

public sealed class RemoteAzomWriteResponse
{
    public bool Ok { get; set; }
    public bool Verified { get; set; }
    public string PropertyName { get; set; } = "";
    public int? RequestedValue { get; set; }
    public int? LiveValue { get; set; }
    public string Message { get; set; } = "";
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
    public bool Connected { get; set; }
    public string? Error { get; set; }
    public RemoteTelemetrySampleView? Sample { get; set; }
    public bool IsDrifting { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RemoteStatusView
{
    public string AtomicVersion { get; set; } = "";
    public bool RemoteWritesEnabled { get; set; }
    public string LastActivity { get; set; } = "";
    public RemoteTuneContext Tune { get; set; } = new();
}

public sealed class RemoteAzomChangedEventArgs : EventArgs
{
    public string PropertyName { get; init; } = "";
    public int? Value { get; init; }
    public bool Verified { get; init; }
}


public sealed class RemoteIntentOption
{
    public string Name { get; set; } = "";
    public bool Selected { get; set; }
}

public sealed class RemoteIntentRequest
{
    public string Name { get; set; } = "";
}

public sealed class RemoteActionResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

public sealed class RemoteBehaviorView
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    public string DisplayName { get; set; } = "";
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
