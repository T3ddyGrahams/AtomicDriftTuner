namespace AtomicDriftTuner.Models;

/// <summary>A verified scalar setup definition expressed in saved VALUE coordinates.
/// Displayed setup units describe a control, not a measured suspension geometry or tyre force.</summary>
public sealed record SetupValueMapping(double Minimum, double Maximum, double Step, double Scale, double Offset)
{
    private const double Tolerance = .000001;
    private const double MaximumNumber = 1e12;

    public static bool IsScalarControl(string section)
    {
        var s = section.ToUpperInvariant();
        return s is "ARB_FRONT" or "ARB_REAR" or "DIFF_POWER" or "DIFF_COAST" or "DIFF_PRELOAD" or
            "FRONT_BIAS" or "BRAKE_BIAS" or "BRAKE_POWER_MULT" or "FUEL" ||
            new[] { "PRESSURE", "TOE_OUT", "SPRING_RATE", "DAMP_BUMP", "DAMP_REBOUND", "DAMP_FAST_BUMP", "DAMP_FAST_REBOUND", "ROD_LENGTH", "PACKER_RANGE" }
                .Any(prefix => new[] { "LF", "RF", "LR", "RR" }.Any(corner => s == prefix + "_" + corner));
    }

    public static bool TryCreate(string section, SetupRangeDefinition? definition, out SetupValueMapping mapping)
    {
        mapping = null!;
        if (definition is null || definition.UnavailableReason is not null ||
            !section.Equals(definition.Section, StringComparison.OrdinalIgnoreCase)) return false;
        if (CamberSetupValues.IsCamber(section))
        {
            if (!CamberSetupValues.TryRawRange(definition, out var raw)) return false;
            mapping = new(raw.Min!.Value, raw.Max!.Value, 1, .1, definition.CamberValueMode == 2 ? definition.Min!.Value : 0);
            return true;
        }
        var mode = definition.ScalarValueMode;
        if (mode is not (0 or 1 or 2) || definition.ShowClicks != (mode != 0) ||
            definition.Min is not double min || !Number(min) || definition.Max is not double max || !Number(max) || min > max ||
            definition.Step is not double step || !Number(step) || step <= 0) return false;
        var scale = mode == 0 ? 1 : step;
        var offset = mode == 2 ? min : 0;
        var low = (min - offset) / scale;
        var high = (max - offset) / scale;
        var rawStep = mode == 0 ? step : 1;
        // Normalized clicks need an integral starting click. Unknown conventions are held.
        if (!Number(low) || !Number(high) || mode != 0 && !Near(low, Math.Round(low))) return false;
        var positions = (high - low) / rawStep;
        if (!double.IsFinite(positions) || positions > MaximumNumber) return false;
        var last = low + Math.Floor(positions + 1e-9) * rawStep;
        if (!Number(last) || last > high + Tolerance) return false;
        mapping = new(low, last, rawStep, scale, offset);
        return true;
    }

    public bool IsLegal(double raw)
    {
        if (!Number(raw) || raw < Minimum - Tolerance || raw > Maximum + Tolerance) return false;
        var position = (raw - Minimum) / Step;
        return double.IsFinite(position) && Math.Abs(position) <= MaximumNumber &&
            Near(raw, Minimum + Math.Round(position, MidpointRounding.AwayFromZero) * Step);
    }

    public double SetupValue(double raw) => raw * Scale + Offset;

    public double Adjust(double current, double rawDelta, out string outcome)
    {
        outcome = "Left unchanged: the baseline has no verified legal range/step. Reload a valid saved setup.";
        if (!IsLegal(current) || !double.IsFinite(rawDelta) || !double.IsFinite(current + rawDelta)) return current;
        var target = Math.Clamp(current + rawDelta, Minimum, Maximum);
        var next = Minimum + Math.Round((target - Minimum) / Step, MidpointRounding.AwayFromZero) * Step;
        next = Math.Clamp(next, Minimum, Maximum);
        if (!IsLegal(next) || !Near(next, current) && Math.Sign(next - current) != Math.Sign(rawDelta)) return current;
        if (Near(next, current))
        {
            outcome = rawDelta > 0 && Near(current, Maximum) || rawDelta < 0 && Near(current, Minimum)
                ? "Left unchanged: this control is already at its legal limit in the requested direction."
                : "Left unchanged: the requested adjustment rounds to the same legal step. ADT has not enlarged it to force a change.";
            return current;
        }
        outcome = $"Saved value {current:0.####} → {next:0.####}; {Math.Abs((next - current) / Step):0.####} legal step(s) {(next > current ? "higher" : "lower")}.";
        return next;
    }

    public string Describe() => $"{Minimum:0.####} .. {Maximum:0.####} saved VALUE; step {Step:0.####}" +
        (Scale != 1 || Offset != 0 ? $"; setup value = saved × {Scale:0.####} + {Offset:0.####}" : "");
    private static bool Number(double value) => double.IsFinite(value) && Math.Abs(value) <= MaximumNumber;
    private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;
}
