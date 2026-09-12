namespace AtomicDriftTuner.Models;

public sealed record GearingTarget
{
    public int Gear { get; init; } = 3;
    public double MinimumSpeedKmh { get; init; } = 60;
    public double MaximumSpeedKmh { get; init; } = 100;
    public double MinimumRpm { get; init; } = 4000;
    public double MaximumRpm { get; init; } = 6500;
    public bool DisplayMph { get; init; }

    public void Validate()
    {
        if (Gear is < 1 or > 10 || !Valid(MinimumSpeedKmh, 5, 500) ||
            !Valid(MaximumSpeedKmh, 5, 500) || MinimumSpeedKmh >= MaximumSpeedKmh ||
            !Valid(MinimumRpm, 500, 30000) || !Valid(MaximumRpm, 500, 30000) || MinimumRpm >= MaximumRpm)
            throw new InvalidDataException("Choose a forward gear (1–10), an increasing speed range (5–500 km/h), and an increasing RPM range (500–30,000).");
    }

    private static bool Valid(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
}

public sealed record FinalDriveRatio(int Index, string Label, double Ratio);

public sealed record GearingData(
    string BaselinePath, string CarPath, IReadOnlyList<FinalDriveRatio> FinalDrives,
    int CurrentIndex, int Gear, double GearRatio, double TyreRadius, double LimiterRpm,
    string GearSource, string TyreSource, IReadOnlyDictionary<string, string> Fingerprints);

public sealed record GearingOption(FinalDriveRatio FinalDrive, double LowRpm, double HighRpm,
    double LimiterSpeedKmh, double Score, bool FitsTarget);

public sealed record GearingPlan(GearingData Data, GearingTarget Target, GearingOption Current,
    GearingOption Recommended, IReadOnlyList<GearingOption> Options)
{
    public bool HasChange => Math.Abs(Current.FinalDrive.Ratio - Recommended.FinalDrive.Ratio) > 0.000001;
}
