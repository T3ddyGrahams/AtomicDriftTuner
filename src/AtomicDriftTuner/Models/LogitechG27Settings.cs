namespace AtomicDriftTuner.Models;

/// <summary>Driver-entered legacy Logitech settings, never hardware readback.</summary>
public sealed class LogitechG27Settings
{
    public int OverallEffectsStrength { get; set; } = 100;
    public int SpringEffectStrength { get; set; }
    public int DamperEffectStrength { get; set; }
    public bool EnableCenteringSpring { get; set; }
    public int CenteringSpringStrength { get; set; }
    public int DegreesOfRotation { get; set; } = 900;
    public bool ReportCombinedPedals { get; set; }
    public bool AllowGameToAdjustSettings { get; set; } = true;
    public AssettoCorsaSettings Ac { get; set; } = new() { GainPct = 50 };
    public string SoftwareVersion { get; set; } = "";

    public void Validate()
    {
        if (new[] { OverallEffectsStrength, SpringEffectStrength, DamperEffectStrength, CenteringSpringStrength }.Any(v => v is < 0 or > 150) ||
            DegreesOfRotation is < 40 or > 900 || Ac is null ||
            new[] { Ac.GainPct, Ac.FilterPct, Ac.MinimumForcePct, Ac.KerbPct, Ac.RoadPct, Ac.SlipPct, Ac.AbsPct }.Any(v => v is < 0 or > 100) ||
            SoftwareVersion is null || SoftwareVersion.Length > 100 || SoftwareVersion.Any(char.IsControl))
            throw new InvalidDataException("G27 strengths must be 0–150%, rotation 40–900°, and AC FFB values 0–100%. Software version must be at most 100 characters.");
    }
}
