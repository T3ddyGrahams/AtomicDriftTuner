using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class RecommendationTestService
{
    public const string Adt = "ADT focused setup";
    public const string Driver = "Driver-defined";
    public static bool Same(RecommendationTest? a, RecommendationTest? b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    public static bool Equal(double a, double b) => Math.Abs(a - b) < .000001;
    public static string Fingerprint(TuneVersion tune) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(tune.Settings.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray())))).ToLowerInvariant();

    public static List<ExpectedSettingChange> Changes(TuneVersion a, TuneVersion b) => a.Settings
        .Where(p => b.Settings.TryGetValue(p.Key, out var value) && !Equal(p.Value, value))
        .Select(p => new ExpectedSettingChange(p.Key, p.Value, b.Settings[p.Key])).ToList();

    public static bool Valid(RecommendationTest? test) => test is not null && test.Schema == "adt/recommendation-test/1" &&
        Guid.TryParseExact(test.Id, "N", out _) && Guid.TryParseExact(test.BaselineSessionId, "N", out _) &&
        Guid.TryParseExact(test.BaselineTuneId, "N", out _) && Guid.TryParseExact(test.DriverId, "N", out _) &&
        test.Origin is Adt or Driver && !string.IsNullOrWhiteSpace(test.Description) && test.Description.Length <= 2000 &&
        !string.IsNullOrWhiteSpace(test.MetricKey) && test.MetricKey.Length <= 100 &&
        test.BaselineSettingsFingerprint is { Length: 64 } && test.BaselineSettingsFingerprint.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(test.ContextKey) && test.ContextKey.Length <= 200 &&
        !string.IsNullOrWhiteSpace(test.GoalSignature) && test.GoalSignature.Length <= 200 &&
        test.Changes is { Count: > 0 and <= 64 } && test.Changes.All(c => c is not null &&
            c.Key is { Length: > 0 and <= 250 } && !c.Key.Any(char.IsControl) &&
            c.Key.StartsWith("ACSetup.", StringComparison.Ordinal) && double.IsFinite(c.Before) && double.IsFinite(c.After) && !Equal(c.Before, c.After)) &&
        test.Changes.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == test.Changes.Count;

    public static RecommendationTest Create(SavedTelemetrySession baseline, string description, string metric,
        IEnumerable<ExpectedSettingChange> changes, string origin = Adt)
    {
        if (!RunHistoryStore.ValidContext(baseline.Session.Context)) throw new InvalidDataException("A recorded baseline with car and driver context is required.");
        var c = baseline.Session.Context!; var tune = c.Tune!;
        var test = new RecommendationTest { Origin = origin, Description = description, MetricKey = metric,
            BaselineSessionId = baseline.Session.Id, BaselineTuneId = tune.Id, BaselineSettingsFingerprint = Fingerprint(tune),
            DriverId = c.DriverId, ContextKey = tune.ContextKey, GoalSignature = GuidedWorkflowStore.GoalSignature(tune.DesiredBehavior),
            Changes = changes.ToList() };
        if (!Valid(test) || test.Changes.Any(x => !tune.Settings.TryGetValue(x.Key, out var old) || !Equal(old, x.Before)))
            throw new InvalidDataException("The test needs exact, named before/after values matching the recorded baseline.");
        return test;
    }

    public static RecommendationTest CreateDriverTest(SavedTelemetrySession baseline, TuneVersion current, string description)
    {
        var old = baseline.Session.Context?.Tune ?? throw new InvalidDataException("Select a recorded baseline for your test.");
        if (old.Settings.Keys.Except(current.Settings.Keys).Any() || current.Settings.Keys.Except(old.Settings.Keys).Any())
            throw new InvalidDataException("Use matching setting coverage before defining your own test.");
        var changes = Changes(old, current);
        if (changes.Any(c => !c.Key.StartsWith("ACSetup.", StringComparison.Ordinal)))
            throw new InvalidDataException("For a driver-defined car test, hold FFB settings fixed.");
        return Create(baseline, description, "driver-defined", changes, Driver);
    }

    public static RecommendationTest? ForRecording(RecommendationTest? planned, SavedTelemetrySession? baseline,
        TuneVersion current, string description, bool ownTest)
    {
        if (ownTest)
            return CreateDriverTest(baseline ?? throw new InvalidDataException("Select the baseline for your own car test."), current, description);
        // Editing the note or selecting another baseline must not retain an unrelated ADT claim.
        return Valid(planned) && planned!.BaselineSessionId == baseline?.Session.Id && description == planned.Description
            ? RunHistoryStore.Clone(planned) : null;
    }

    public static string Mismatch(RecommendationTest? test, SavedTelemetrySession baseline, TuneVersion current)
    {
        if (!Valid(test)) return "No exact test plan was recorded. Free-text notes are kept as observations; use Review one car setup change, or explicitly record your own car test.";
        var c = baseline.Session.Context!; var old = c.Tune!;
        if (old.Settings.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != old.Settings.Count ||
            current.Settings.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != current.Settings.Count)
            return "The captured settings contain ambiguous duplicate control names; prepare a fresh baseline.";
        if (old.SetupSource is not ("manual-file" or "csp-current-setup") || current.SetupSource != old.SetupSource)
            return "Matching known setup capture methods are required to verify this test. Older observations remain available.";
        if (test!.BaselineSessionId != baseline.Session.Id || test.BaselineTuneId != old.Id || test.DriverId != c.DriverId ||
            test.ContextKey != old.ContextKey || test.GoalSignature != GuidedWorkflowStore.GoalSignature(old.DesiredBehavior) ||
            test.BaselineSettingsFingerprint != Fingerprint(old))
            return "The recorded test plan belongs to a different baseline, driver, car or goal. Prepare a new test from the intended baseline.";
        if (!old.Settings.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(current.Settings.Keys))
            return "Captured setting coverage differs from the test baseline; missing values are not verified setting changes.";
        var actual = Changes(old, current);
        if (actual.Count != test.Changes.Count || test.Changes.Any(e => !actual.Any(a =>
            a.Key == e.Key && Equal(a.Before, e.Before) && Equal(a.After, e.After))))
            return "The recorded settings do not match the planned test. A setting was unchanged, changed by a different amount/direction, or changed in addition to the plan.";
        return "";
    }
}
