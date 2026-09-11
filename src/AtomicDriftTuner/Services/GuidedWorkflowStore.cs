using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Small resumable workflow state; raw runs and immutable reviews remain in their own stores.</summary>
public sealed class GuidedWorkflowStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;
    public GuidedWorkflowStore(string? root = null) => _root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner", "GuidedWorkflow");
    public GuidedPreferences Preferences() => Read(Path.Combine(_root, "preferences.json"), new GuidedPreferences(), ValidPreferences);
    public void SavePreferences(GuidedPreferences preferences)
    {
        if (!ValidPreferences(preferences)) throw new InvalidDataException("Choose Yes, No or Not sure, and a driver name of 1–80 characters.");
        lock (Gate) Write(Path.Combine(_root, "preferences.json"), preferences);
    }
    public static string GoalSignature(CarBehaviorTarget goal) => string.Join("/", goal.FrontEndBite, goal.RearGrip, goal.SelfSteerSpeed,
        goal.TransitionSpeed, goal.AngleStability, goal.ThrottleSteering, goal.InitiationSharpness);
    private static string Key(TuneInput input) => RunHistoryStore.ContextKey(input) + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Intent.Kind.ToString())));
    private string PathFor(TuneInput input, string driver)
    {
        if (!Guid.TryParseExact(driver, "N", out _)) throw new InvalidDataException("Select a valid driver profile.");
        return Path.Combine(_root, Key(input) + "-" + driver + ".json");
    }
    public GuidedJourney Journey(TuneInput input, string driver)
    {
        var key = Key(input);
        return Read(PathFor(input, driver), new GuidedJourney { ContextKey = key, DriverId = driver },
            j => j.Schema == "adt/guided-journey/1" && j.ContextKey == key && j.DriverId == driver &&
                 j.GoalSignature is not null && j.BaselineId is not null && j.AfterId is not null && j.Recommendation is not null && j.SetupPath is not null && j.Conditions is not null);
    }
    public void Update(TuneInput input, string driver, Action<GuidedJourney> update)
    {
        lock (Gate)
        {
            var journey = Journey(input, driver);
            update(journey);
            Write(PathFor(input, driver), journey);
        }
    }
    public void Reset(TuneInput input, string driver) => Update(input, driver, j =>
    {
        j.GoalSignature = j.GeneratedSignature = ""; j.TuneGenerated = j.TuneReady = j.Reviewed = false;
        j.BaselineId = j.AfterId = j.Recommendation = "";
    });
    private static bool ValidPreferences(GuidedPreferences p) => p.Schema == "adt/guided-preferences/1" &&
        new[] { "Yes", "No", "Not sure" }.Contains(p.SimHub) && new[] { "Yes", "No", "Not sure" }.Contains(p.Azom) &&
        !string.IsNullOrWhiteSpace(p.DriverName) && p.DriverName.Trim().Length <= 80 && !p.DriverName.Any(char.IsControl);
    private static T Read<T>(string path, T fallback, Func<T, bool> valid)
    {
        if (!File.Exists(path)) return fallback;
        if (new FileInfo(path).Length is <= 0 or > 100_000) throw new InvalidDataException("Guided workflow data is unreadable. The file was preserved: " + path);
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        if (value is null || !valid(value)) throw new InvalidDataException("Guided workflow data is invalid. The file was preserved: " + path);
        return value;
    }
    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
