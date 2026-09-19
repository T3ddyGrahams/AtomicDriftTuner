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
    public GuidedPreferences Preferences()
    {
        var preferences = Read(Path.Combine(_root, "preferences.json"), new GuidedPreferences(), ValidPreferences);
        // Legacy FFB-only history remains readable through its explicit journey path.
        // Reading preferences must never rewrite that history or assume a new choice.
        if (preferences.Focus == TuningFocus.FfbOnly)
        {
            preferences.Focus = TuningFocus.Both;
            preferences.FocusChoiceConfirmed = false;
        }
        return preferences;
    }
    public void SavePreferences(GuidedPreferences preferences)
    {
        if (!ValidPreferences(preferences)) throw new InvalidDataException("Choose Yes, No or Not sure, and a driver name of 1–80 characters.");
        lock (Gate)
        {
            _ = Preferences();
            var saved = RunHistoryStore.Clone(preferences);
            if (saved.Focus == TuningFocus.FfbOnly)
            {
                saved.Focus = TuningFocus.Both;
                saved.FocusChoiceConfirmed = false;
            }
            Write(Path.Combine(_root, "preferences.json"), saved);
        }
    }
    public static string GoalSignature(CarBehaviorTarget goal)
    {
        var handling = string.Join("/", goal.FrontEndBite, goal.RearGrip, goal.SelfSteerSpeed,
            goal.TransitionSpeed, goal.AngleStability, goal.ThrottleSteering, goal.InitiationSharpness);
        return !goal.HasAngleGoal ? handling : handling + FormattableString.Invariant($"/angle/{(int)goal.SustainedAngle}/{goal.AngleMinDeg}/{goal.AngleMaxDeg}");
    }
    private static string Key(TuneInput input) => RunHistoryStore.ContextKey(input) + "-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Intent.Kind.ToString())));
    private string PathFor(TuneInput input, string driver, TuningFocus focus)
    {
        if (!Guid.TryParseExact(driver, "N", out _)) throw new InvalidDataException("Select a valid driver profile.");
        if (!Enum.IsDefined(focus)) throw new InvalidDataException("Choose a valid tuning mode.");
        // Preserve the original Both-mode path so existing progress opens without migration.
        return Path.Combine(_root, Key(input) + "-" + driver + (focus == TuningFocus.Both ? "" : "-" + focus) + ".json");
    }
    public GuidedJourney Journey(TuneInput input, string driver, TuningFocus? focus = null)
    {
        var key = Key(input);
        var selected = focus ?? Preferences().Focus;
        return Read(PathFor(input, driver, selected), new GuidedJourney { ContextKey = key, DriverId = driver, Focus = selected, FfbProvider = Preferences().FfbProvider },
            j => j.Schema == "adt/guided-journey/1" && j.ContextKey == key && j.DriverId == driver &&
                 j.Focus == selected && Enum.IsDefined(j.FfbProvider) &&
                 j.GoalSignature is not null && j.BaselineId is not null && j.AfterId is not null && j.Recommendation is not null && j.SetupPath is not null && j.Conditions is not null);
    }
    public void Update(TuneInput input, string driver, Action<GuidedJourney> update, TuningFocus? focus = null)
    {
        lock (Gate)
        {
            var selected = focus ?? Preferences().Focus;
            var journey = Journey(input, driver, selected);
            update(journey);
            if (journey.Focus != selected) throw new InvalidDataException("The journey's tuning mode cannot change in place.");
            Write(PathFor(input, driver, selected), journey);
        }
    }
    public void Reset(TuneInput input, string driver, TuningFocus? focus = null) => Update(input, driver, j =>
    {
        j.FfbProvider = Preferences().FfbProvider;
        j.GoalSignature = j.GeneratedSignature = ""; j.TuneGenerated = j.TuneReady = j.Reviewed = false;
        j.BaselineId = j.AfterId = j.Recommendation = "";
    }, focus);
    private static bool ValidPreferences(GuidedPreferences p) => p.Schema == "adt/guided-preferences/1" &&
        Enum.IsDefined(p.Focus) && Enum.IsDefined(p.FfbProvider) && p.MozaSdkFolder is not null &&
        p.MozaSdkFolder.Length <= 1024 && !p.MozaSdkFolder.Any(char.IsControl) &&
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
