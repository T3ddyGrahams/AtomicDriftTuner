using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class LogitechG27Support
{
    public const string HardwareId = "logitech-g27";
    public const string WheelId = "logitech-g27-integrated";
    public static bool IsG27(HardwareProfile hardware) => hardware.Id == HardwareId;
    public static string Instructions(bool detailed) => detailed
        ? "LOGITECH G27 — LEGACY LOGITECH GAMING SOFTWARE / PROFILER\n1. Select Logitech G27 as your wheelbase and its integrated rim on the dashboard. Use the legacy software that already recognizes your G27. Logitech lists the G27 under LGS 5.10; this is separate from the G HUB workflow.\n2. Open Wheelbase Settings in ADT. Copy your current Logitech and AC FFB values into the matching boxes, or explicitly use ADT's provisional starting point. Save in ADT records your plan only.\n3. In Logitech Profiler, open Options → Global Device Settings, or the equivalent device settings for your AC game profile. Check which profile is active: per-game settings can override global settings. Manually enter the values you intend to test and accept that dialog.\n4. In Content Manager → Settings → Assetto Corsa → Controls, match steering rotation and verify full steering, throttle, brake and clutch travel. Enter AC FFB on the Force Feedback page. Keep combined pedals off when configuring separate throttle and brake axes.\n5. Drive a short check, then confirm the actual settings in ADT's recorder. Record a baseline, change one thing, record again and compare. Lower AC gain if it clips; investigate dead zone/minimum force in small steps rather than assuming a fixed value works for every G27.\n\nThis version does not read or apply Logitech settings. SimHub/AZOM and Pit House are not needed. Car diagnosis and setup tuning remain available. Software/driver behavior still needs testing on the user's G27."
        : "G27: choose the G27 base and integrated rim → Wheelbase Settings → enter your Logitech/AC values → save the plan in ADT → enter and verify the same values in legacy Logitech Profiler and AC → record and compare. Saving in ADT does not apply to the wheel. SimHub/AZOM and Pit House are not required. Enable more explanation for the menus and checks.";

    public static string Summary(LogitechG27Settings s) =>
        $"Logitech G27 • manual settings\nOverall effects: {s.OverallEffectsStrength}%\nSpring effect: {s.SpringEffectStrength}%\nDamper effect: {s.DamperEffectStrength}%\nCentering spring: {(s.EnableCenteringSpring ? "on" : "off")} ({s.CenteringSpringStrength}%)\nRotation: {s.DegreesOfRotation}°\nCombined pedals: {(s.ReportCombinedPedals ? "on" : "off")}\nAllow game to adjust: {(s.AllowGameToAdjustSettings ? "on" : "off")}\nEnter and verify these in Logitech Profiler. ADT has not read or applied them.";
}

public sealed class LogitechG27Store
{
    private readonly string _path;
    public LogitechG27Store(string? directory = null) => _path = Path.Combine(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner"), "logitech-g27.json");
    public LogitechG27Settings Load()
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 32000) throw new InvalidDataException("The saved G27 plan is too large.");
        var value = JsonSerializer.Deserialize<LogitechG27Settings>(File.ReadAllText(_path)) ?? throw new InvalidDataException("The G27 plan is empty.");
        value.Validate(); return value;
    }
    public void Save(LogitechG27Settings settings)
    {
        settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
