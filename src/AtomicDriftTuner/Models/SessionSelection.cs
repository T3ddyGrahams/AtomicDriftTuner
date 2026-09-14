namespace AtomicDriftTuner.Models;

/// <summary>Only this Windows user's dashboard choices; no generated tune or applied-setting claims.</summary>
public sealed class SessionSelection
{
    public string Schema { get; set; } = "adt/session-selection/1";
    public string? HardwareId { get; set; }
    public string? WheelId { get; set; }
    public string? PackId { get; set; }
    public string? CarId { get; set; }
    public string? CarFolder { get; set; }
    public bool CarIsInstalled { get; set; }
    public DriftStyleKind? Intent { get; set; }
    public GripLevel? Grip { get; set; }
    public Dictionary<string, double> Numbers { get; set; } = [];

    public static readonly string[] HardwareFields = ["PeakTorqueBox"];
    public static readonly string[] WheelFields = ["WheelDiameterBox", "WheelInertiaBox"];
    public static readonly string[] CarFields = ["CarMassBox", "CarPowerBox", "CasterBox", "LockBox", "FrontTireBox"];

    public SessionSelection Clean()
    {
        if (Schema != "adt/session-selection/1") return new();
        static string? Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl) ? value : null;
        return new SessionSelection
        {
            HardwareId = Id(HardwareId), WheelId = Id(WheelId), PackId = Id(PackId), CarId = Id(CarId), CarFolder = Id(CarFolder), CarIsInstalled = CarIsInstalled,
            Intent = Intent is { } intent && Enum.IsDefined(intent) ? intent : null,
            Grip = Grip is { } grip && Enum.IsDefined(grip) ? grip : null,
            Numbers = (Numbers ?? []).Where(p => HardwareFields.Concat(WheelFields).Concat(CarFields).Contains(p.Key) &&
                double.IsFinite(p.Value) && Math.Abs(p.Value) <= 100_000).ToDictionary(p => p.Key, p => p.Value)
        };
    }
}
