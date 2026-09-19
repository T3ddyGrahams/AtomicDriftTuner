using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>MOZA SDK 1.0.1.8 ranges, not AZOM's similarly named property ranges.</summary>
public static class PitHouseCatalog
{
    public sealed record Setting(string Key, string Label, int Min, int Max, Func<AzomSettings, int> Target)
    {
        public bool Accepts(int value) => value >= Min && value <= Max;
    }
    public static IReadOnlyList<Setting> Settings { get; } = Array.AsReadOnly(new[] {
        new Setting("FfbStrength", "Game FFB strength (%)", 0, 100, a => a.Core.GameFfbStrengthPct),
        new Setting("PeakTorque", "Maximum output torque (%)", 50, 100, a => a.Core.BaseTorqueOutputPct),
        new Setting("LimitWheelSpeed", "Maximum wheel speed (%)", 10, 100, a => a.Core.MaximumWheelSpeedPct),
        new Setting("NaturalDamper", "Natural damping (%)", 0, 100, a => a.WheelbaseEffects.WheelDamperPct),
        new Setting("NaturalFriction", "Natural friction (%)", 0, 100, a => a.WheelbaseEffects.WheelFrictionPct),
        new Setting("NaturalInertia", "Natural inertia", 100, 500, a => a.WheelbaseEffects.NaturalInertia),
        new Setting("SpringStrength", "Spring strength (%)", 0, 100, a => a.WheelbaseEffects.WheelSpringPct),
        new Setting("NaturalInertiaRatio", "Steering wheel inertia ratio", 100, 4000, a => a.Protection.SteeringWheelInertia),
        new Setting("SpeedDamping", "Speed-dependent damping (%)", 0, 100, a => a.HighSpeedDamping.DampingLevelPct),
        new Setting("SpeedDampingStartPoint", "Speed damping start (km/h)", 0, 400, a => a.HighSpeedDamping.TriggerSpeedKph)
    });
    public static Setting Find(string key) => Settings.SingleOrDefault(x => x.Key == key)
        ?? throw new InvalidDataException("Unsupported Pit House setting: " + key);
    public const string Unsupported = "Manual / not mapped: steering rotation and game maximum angle; road sensitivity and equalizer bands; FFB output curve; interpolation; game-effect controls; soft limits; gearshift vibration; protection and other preferences. The SDK and AZOM use different controls/ranges. ADT does not send these controls. A target outside the SDK range is blocked, never silently clamped.";
}
