namespace AtomicDriftTuner.Models;

public sealed record GearingTarget
{
    public int Gear { get; init; } = 3;
    public double MinimumSpeedKmh { get; init; } = 60;
    public double MaximumSpeedKmh { get; init; } = 100;
    public double MinimumRpm { get; init; } = 4000;
    public double MaximumRpm { get; init; } = 6500;
    public bool DisplayMph { get; init; }
    public GearingCornerTarget? Sweeper { get; init; }
    public string RpmSource { get; init; } = "Driver target";
    public string? RpmSourceFingerprint { get; init; }

    public IEnumerable<(string Label, GearingTarget Target)> Goals()
    {
        yield return (Sweeper is null ? "Drift target" : "Tight corners", this with { Sweeper = null });
        if (Sweeper is { } s) yield return ("Long sweepers", this with { Gear = s.Gear, MinimumSpeedKmh = s.MinimumSpeedKmh, MaximumSpeedKmh = s.MaximumSpeedKmh, Sweeper = null });
    }

    public void Validate()
    {
        if (Gear is < 1 or > 10 || !Valid(MinimumSpeedKmh, 5, 500) || !Valid(MaximumSpeedKmh, 5, 500) || MinimumSpeedKmh >= MaximumSpeedKmh ||
            !Valid(MinimumRpm, 500, 30000) || !Valid(MaximumRpm, 500, 30000) || MinimumRpm >= MaximumRpm)
            throw new InvalidDataException("Choose a forward gear (1–10), an increasing speed range, and an increasing RPM range (500–30,000).");
        if (RpmSource is null || RpmSource.Length > 300 || RpmSource.Any(char.IsControl) ||
            RpmSourceFingerprint is not null && (RpmSourceFingerprint.Length != 64 || !RpmSourceFingerprint.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The RPM target source is invalid. Set the RPM target again.");
        if (Sweeper is { } s && (s.Gear is < 1 or > 10 || !Valid(s.MinimumSpeedKmh, 5, 500) || !Valid(s.MaximumSpeedKmh, 5, 500) || s.MinimumSpeedKmh >= s.MaximumSpeedKmh))
            throw new InvalidDataException("Choose a forward gear and an increasing speed range for long sweepers.");
    }
    private static bool Valid(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
}

public sealed record GearingCornerTarget(int Gear, double MinimumSpeedKmh, double MaximumSpeedKmh);
public sealed record GearingRpmEstimate(double? MinimumRpm, double? MaximumRpm, string Explanation)
{
    public bool Available => MinimumRpm is not null && MaximumRpm is not null;
}
public sealed record FinalDriveRatio(int Index, string Label, double Ratio);
public sealed record GearingData(
    string BaselinePath, string CarPath, IReadOnlyList<FinalDriveRatio> FinalDrives,
    int CurrentIndex, int Gear, double GearRatio, double TyreRadius, double LimiterRpm,
    string GearSource, string TyreSource, IReadOnlyDictionary<string, string> Fingerprints)
{
    public CarDataEvidence? CarDataEvidence { get; init; }
    public int GearCount { get; init; }
    public string GearboxKind { get; init; } = "Fixed individual gears";
    public int SelectedGearChoices { get; init; } = 1;
    public bool FinalDriveAdjustable { get; init; } = true;
    public IReadOnlyList<string> DefinitionWarnings { get; init; } = Array.Empty<string>();
    public GearingRpmEstimate RpmEstimate { get; init; } = new(null, null, "No engine curve was read.");
}
public sealed record GearingOption(FinalDriveRatio FinalDrive, double LowRpm, double HighRpm,
    double LimiterSpeedKmh, double Score, bool FitsTarget)
{
    public IReadOnlyList<GearingGoalEstimate> Goals { get; init; } = [];
    public bool BelowLimiter => Goals.All(g => g.BelowLimiter);
}
public sealed record GearingGoalEstimate(string Label, int Gear, double LowRpm, double HighRpm,
    double LimiterSpeedKmh, bool FitsTarget, bool BelowLimiter);
public sealed record GearingPlan(GearingData Data, GearingTarget Target, GearingOption Current,
    GearingOption Recommended, IReadOnlyList<GearingOption> Options)
{
    public bool HasChange => Math.Abs(Current.FinalDrive.Ratio - Recommended.FinalDrive.Ratio) > 0.000001;
    public GearingData? SweeperData { get; init; }
}
