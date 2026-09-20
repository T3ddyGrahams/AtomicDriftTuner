using System.Globalization;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Validates and snapshots desktop recommendations without writing any setup file.</summary>
public sealed class PitSetupPlanService
{
    public const int MaximumChanges = 64;
    public const int MaximumParameters = LiveSetupCaptureService.MaximumNumericSections;
    public const int MaximumBaselineBytes = LiveSetupCaptureService.MaximumIniBytes;
    public const double NumericTolerance = 0.000001;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public PitSetupPlan Create(CarSetupAnalysis analysis, string carId, string label)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        CarPhysicsService.EnsureUnchanged(analysis.Physics);
        if (string.IsNullOrWhiteSpace(carId) ||
            !string.Equals(analysis.CarFolderName, carId, StringComparison.OrdinalIgnoreCase))
            throw Invalid("The selected car does not match the analyzed setup. Load its baseline again.");
        if (string.IsNullOrWhiteSpace(label) || label.Length > 160 || label.Any(char.IsControl))
            throw Invalid("The pit setup label must contain 1–160 characters without control characters.");
        if (analysis.Parameters.Count is 0 or > MaximumParameters)
            throw Invalid("A pit setup must contain 1–512 numeric baseline parameters.");

        // The same strict INI parser used for live evidence prevents disagreement about duplicate
        // sections, units, numeric syntax and CAR/MODEL. This local envelope is discarded immediately.
        var request = new LiveSetupCaptureRequest
        {
            Available = true, ProtocolVersion = 1, Source = LiveSetupCaptureService.CaptureSource,
            CarId = carId, TrackId = "pit_plan_validation", SessionIndex = 0, SessionType = 1,
            SessionGeneration = 0, SetupRevision = 0, CaptureSequence = 1, SimTimeMs = 0, Frame = 0,
            SetupIni = ReadBaseline(analysis.BaselinePath)
        };
        if (!LiveSetupCaptureService.TryParse(request, out var source, out var error) || source is null)
            throw Invalid("The baseline cannot be staged for the pits: " + error);
        if (source.Values.Count != analysis.Parameters.Count)
            throw Invalid("The baseline has changed since it was analyzed. Load it again before staging a plan.");

        var baseline = new Dictionary<string, double>(StringComparer.Ordinal);
        var changes = new List<PitSetupChange>();
        foreach (var parameter in analysis.Parameters)
        {
            if (parameter is null || !Identifier(parameter.Section))
                throw Invalid("A setup parameter has an invalid section name.");
            var section = parameter.Section.ToUpperInvariant();
            if (!baseline.TryAdd(section, 0))
                throw Invalid("The analyzed setup contains duplicate parameter sections.");
            if (parameter.CurrentValue is not double current || !Numeric(current) ||
                parameter.RecommendedValue is not double recommended || !Numeric(recommended) ||
                !TryNumeric(parameter.CurrentRaw, out var rawCurrent) || !NumbersEqual(current, rawCurrent))
                throw Invalid($"{section} has an invalid or inconsistent numeric value.");
            if (!source.Values.TryGetValue("ACSetup." + section, out var saved) || !NumbersEqual(saved, current))
                throw Invalid($"The baseline value for {section} has changed. Load the baseline again.");

            // Keep the exact source value for the companion's whole-baseline stale check.
            baseline[section] = saved;
            if (NumbersEqual(current, recommended)) continue;

            var formatted = parameter.RecommendedRaw;
            if (!TryNumeric(formatted, out var after) || !NumbersEqual(after, recommended))
                throw Invalid($"{section} cannot be represented reliably in a saved setup VALUE.");
            ValidateRange(parameter, saved, after);
            if (NumbersEqual(saved, after))
                throw Invalid($"{section} does not produce a distinct saved setup VALUE.");
            changes.Add(new PitSetupChange(section, saved, after));
            if (changes.Count > MaximumChanges)
                throw Invalid("A pit setup plan cannot change more than 64 parameters at once.");
        }

        if (changes.Count == 0)
            throw Invalid("There are no supported setup changes to stage for the pits.");
        return new PitSetupPlan(carId, label.Trim(), changes.OrderBy(c => c.Section, StringComparer.Ordinal), baseline);
    }

    /// <summary>The protocol uses an absolute tolerance in raw saved VALUE units, never displayed units.</summary>
    public static bool NumbersEqual(double first, double second) =>
        double.IsFinite(first) && double.IsFinite(second) && Math.Abs(first - second) <= NumericTolerance;

    private static void ValidateRange(CarSetupParameter parameter, double before, double after)
    {
        var range = parameter.Range;
        // SHOW_CLICKS describes display/physical ranges, not a proven mapping to the saved VALUE.
        // The tuning engine also treats those mappings as unknown: applying guessed indexes is unsafe.
        if (range is null || range.ShowClicks ||
            !string.Equals(range.Section, parameter.Section, StringComparison.OrdinalIgnoreCase) ||
            range.Min is not double min || !Numeric(min) ||
            range.Max is not double max || !Numeric(max) || min > max ||
            range.Step is not double step || !double.IsFinite(step) || step <= 0)
            throw Invalid($"{parameter.Section} needs a known numeric minimum, maximum and step before it can be applied in the pits.");
        if (!InRange(before, min, max) || !InRange(after, min, max) ||
            !OnStep(before, min, step) || !OnStep(after, min, step))
            throw Invalid($"{parameter.Section} is outside its supported range or does not match a legal setup step.");
    }

    private static bool InRange(double value, double min, double max) =>
        value >= min - NumericTolerance && value <= max + NumericTolerance;

    private static bool OnStep(double value, double min, double step)
    {
        var position = (value - min) / step;
        if (!double.IsFinite(position) || Math.Abs(position) > LiveSetupCaptureService.MaximumSafeCounter) return false;
        return NumbersEqual(value, min + Math.Round(position, MidpointRounding.AwayFromZero) * step);
    }

    private static string ReadBaseline(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Invalid("Load a saved baseline before staging a pit setup.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBaselineBytes) throw Invalid("The baseline exceeds the 64 KiB pit setup limit.");
        // Bound the read itself as well as the initial file length.
        var bytes = new byte[MaximumBaselineBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = stream.Read(bytes, length, bytes.Length - length);
            if (count == 0) break;
            length += count;
        }
        if (length > MaximumBaselineBytes) throw Invalid("The baseline exceeds the 64 KiB pit setup limit.");
        try { return StrictUtf8.GetString(bytes, 0, length); }
        catch (DecoderFallbackException) { throw Invalid("The baseline contains invalid UTF-8 text."); }
    }

    private static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static bool Numeric(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= LiveSetupCaptureService.MaximumAbsoluteValue;

    private static bool TryNumeric(string? text, out double value)
    {
        value = 0;
        if (text is null || text.Length is 0 or > 128 ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !Numeric(value)) return false;
        return value != 0 || !text.Split('e', 'E')[0].Any(c => c is >= '1' and <= '9');
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
