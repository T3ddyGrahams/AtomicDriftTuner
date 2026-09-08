using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Append-only versions and reviews. No existing tune or telemetry file is overwritten.</summary>
public sealed class RunHistoryStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string RootDirectory { get; }
    public List<string> Warnings { get; } = [];
    public RunHistoryStore(string? root = null)
    {
        if (root is null)
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData)) throw new InvalidOperationException("Windows did not provide a LocalAppData folder for ADT.");
            root = Path.Combine(localData, "AtomicDriftTuner", "RunHistory");
        }
        RootDirectory = Path.GetFullPath(root);
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    public static bool SameBehavior(CarBehaviorTarget? a, CarBehaviorTarget? b) =>
        a is not null && b is not null && a.FrontEndBite == b.FrontEndBite && a.RearGrip == b.RearGrip && a.SelfSteerSpeed == b.SelfSteerSpeed &&
        a.TransitionSpeed == b.TransitionSpeed && a.AngleStability == b.AngleStability && a.ThrottleSteering == b.ThrottleSteering && a.InitiationSharpness == b.InitiationSharpness;
    public static string ContextKey(TuneInput input) => Hash(JsonSerializer.Serialize(new[] { input.Hardware.Id, input.Wheel.Id,
        input.DriftPack.Id, input.Car.SourceFolderName ?? input.Car.Id }.Select(s => s.Trim().ToLowerInvariant())));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Text(string? value, string label, int maximum = 160)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 || clean.Length > maximum || clean.Any(char.IsControl)) throw new InvalidDataException($"{label} must contain 1–{maximum} characters without control characters.");
        return clean;
    }
    private static void Id(string value) { if (!Guid.TryParseExact(value, "N", out _)) throw new InvalidDataException("Invalid history identifier."); }

    public List<DriverIdentity> ListDrivers() => Read<DriverIdentity>("drivers").Where(d => Guid.TryParseExact(d.Id, "N", out _) && !string.IsNullOrWhiteSpace(d.Name)).OrderBy(d => d.Name).ToList();
    public DriverIdentity GetOrCreateDriver(string name)
    {
        name = Text(name, "Driver name", 80);
        var directory = Path.Combine(RootDirectory, "drivers");
        var path = Path.Combine(directory, Hash(name.ToLowerInvariant()) + ".json");
        Directory.CreateDirectory(directory);
        if (File.Exists(path)) return LoadDriver(path);
        var identity = new DriverIdentity { Name = name };
        try { WriteNew(path, identity); }
        catch (IOException) when (File.Exists(path)) { return LoadDriver(path); }
        return identity;
    }
    private static DriverIdentity LoadDriver(string path)
    {
        if (new FileInfo(path).Length is <= 0 or > 100_000) throw new InvalidDataException("Driver history is damaged; the existing file was preserved.");
        var identity = JsonSerializer.Deserialize<DriverIdentity>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Driver history is damaged.");
        Id(identity.Id); Text(identity.Name, "Driver name", 80); return identity;
    }

    public TuneVersion CaptureTune(TuneInput input, DriverIdentity driver, string label, CarBehaviorTarget behavior,
        CalibrationProfile? calibration, string? setupPath = null, AzomUserPreferences? preferences = null)
    {
        Id(driver.Id);
        var version = new TuneVersion { DriverId = driver.Id, ContextKey = ContextKey(input), Label = Text(label, "Tune version name"), DesiredBehavior = Clone(behavior) };
        version.DesiredBehavior.Normalize();
        var tune = new TuningEngine().Generate(input, calibration, preferences);
        void Flatten(JsonElement element, string key)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var p in element.EnumerateObject()) Flatten(p.Value, key + "." + p.Name);
            else if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value)) version.Settings[key] = value;
            else if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) version.Settings[key] = element.GetBoolean() ? 1 : 0;
        }
        Flatten(JsonSerializer.SerializeToElement(tune.Ac), "Generated.ACFFB");
        Flatten(JsonSerializer.SerializeToElement(tune.Azom), "Generated.AZOM");
        if (!string.IsNullOrWhiteSpace(setupPath))
        {
            var info = new FileInfo(setupPath);
            if (info.Length is <= 0 or > 2_000_000) throw new InvalidDataException("Choose an AC setup INI smaller than 2 MB.");
            using var source = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var baseline = new AssettoCorsaSetupService().LoadBaseline(setupPath, input.Car);
            if (baseline.Parameters.Select(p => p.Section).Distinct(StringComparer.OrdinalIgnoreCase).Count() != baseline.Parameters.Count)
                throw new InvalidDataException("The attached setup has duplicate VALUE sections and cannot be snapshotted unambiguously.");
            foreach (var p in baseline.Parameters.Where(p => p.CurrentValue is double v && double.IsFinite(v)))
                version.Settings["ACSetup." + p.Section] = p.CurrentValue!.Value;
            version.SetupFileName = Path.GetFileName(setupPath);
            version.SetupSha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        }
        SaveTune(version);
        return Clone(version);
    }
    public void SaveTune(TuneVersion version)
    {
        Id(version.Id); Id(version.DriverId); Text(version.Label, "Tune version name"); Text(version.ContextKey, "Tune context");
        if (!ValidTune(version))
            throw new InvalidDataException("Unsupported or invalid tune snapshot.");
        WriteNew(Path.Combine(RootDirectory, "tunes", version.Id + ".json"), version);
    }
    public List<TuneVersion> ListTunes(TuneInput input, string? driverId = null) => Read<TuneVersion>("tunes")
        .Where(v => ValidTune(v) && v.ContextKey == ContextKey(input) &&
            (driverId is null || v.DriverId == driverId)).OrderByDescending(v => v.CreatedUtc).Take(200).ToList();

    public void SaveReview(RunReview review)
    {
        Id(review.Id); Id(review.SessionId); Id(review.DriverId);
        if (!ValidReview(review)) throw new InvalidDataException("Unsupported or invalid run review.");
        if (review.BaselineSessionId.Length > 0) Id(review.BaselineSessionId);
        if (review.Notes.Length > 4000 || !new[] { "Not rated", "Better", "Worse", "No noticeable difference", "Tradeoff" }.Contains(review.DriverRating))
            throw new InvalidDataException("Invalid driver rating or notes longer than 4,000 characters.");
        WriteNew(Path.Combine(RootDirectory, "reviews", review.Id + ".json"), review);
    }
    public List<RunReview> ListReviews(TuneInput input, string? driverId = null) => Read<RunReview>("reviews")
        .Where(r => ValidReview(r) && r.ContextKey == ContextKey(input) && (driverId is null || r.DriverId == driverId))
        .OrderByDescending(r => r.ReviewedUtc).Take(200).ToList();

    public static bool ValidContext(RunContext? c) => c is not null && c.Schema == "adt/run-context/1" &&
        Guid.TryParseExact(c.DriverId, "N", out _) && c.DriverName is not null && c.TrackId is not null && c.Conditions is not null &&
        c.RecommendationSessionId is not null && c.TestedRecommendations is not null && c.TestedRecommendations.All(x => !string.IsNullOrWhiteSpace(x)) &&
        c.Tune is not null && ValidTune(c.Tune) && c.Tune.DriverId == c.DriverId;

    private static bool ValidReview(RunReview r) => r.Schema == "adt/run-review/1" && Guid.TryParseExact(r.Id, "N", out _) &&
        Guid.TryParseExact(r.SessionId, "N", out _) && Guid.TryParseExact(r.DriverId, "N", out _) && r.BaselineSessionId is not null &&
        r.Notes is not null && r.Notes.Length <= 4000 && !string.IsNullOrWhiteSpace(r.ContextKey) &&
        new[] { "Not rated", "Better", "Worse", "No noticeable difference", "Tradeoff" }.Contains(r.DriverRating) &&
        r.Comparison is not null && r.Comparison.Limitations is not null && r.Comparison.Metrics is not null && r.Comparison.TuneChanges is not null;

    private static bool ValidTune(TuneVersion v) => v.Schema == "adt/tune-version/1" && Guid.TryParseExact(v.Id, "N", out _) &&
        Guid.TryParseExact(v.DriverId, "N", out _) && !string.IsNullOrWhiteSpace(v.ContextKey) && v.Label is not null && v.SetupFileName is not null &&
        v.SetupSha256 is not null && v.Settings is not null && v.DesiredBehavior is not null && v.Settings.Count <= 2000 && v.Settings.Values.All(double.IsFinite) &&
        new[] { v.DesiredBehavior.FrontEndBite, v.DesiredBehavior.RearGrip, v.DesiredBehavior.SelfSteerSpeed, v.DesiredBehavior.TransitionSpeed,
            v.DesiredBehavior.AngleStability, v.DesiredBehavior.ThrottleSteering, v.DesiredBehavior.InitiationSharpness }.All(x => x is >= -2 and <= 2);

    private IEnumerable<T> Read<T>(string directory)
    {
        var path = Path.Combine(RootDirectory, directory);
        if (!Directory.Exists(path)) yield break;
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            T? value = default;
            try
            {
                if (new FileInfo(file).Length is <= 0 or > 2_000_000) throw new InvalidDataException("Unsupported size");
                value = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
                if (value is null || value is TuneVersion tune && !ValidTune(tune) || value is RunReview review && !ValidReview(review))
                    throw new InvalidDataException("Invalid history entry");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                value = default;
                var warning = $"Skipped an unreadable {directory} entry; the file was preserved.";
                if (!Warnings.Contains(warning)) Warnings.Add(warning);
            }
            if (value is not null) yield return value;
        }
    }
    private static void WriteNew<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > 2_000_000) throw new InvalidDataException("History entry exceeds 2 MB.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
