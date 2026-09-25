using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed partial class GearingDataService
{
    // A reproducible starting heuristic, not a dyno reconstruction or an optimal drift band.
    // The referenced AC curve is torque; RPM × torque is proportional to base power.
    private static GearingRpmEstimate ReadRpmEstimate(CarDataSource source, Ini engine, double limiter)
    {
        try
        {
            var file = engine.Required("HEADER", "POWER_CURVE");
            if (!file.EndsWith(".lut", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The engine curve is not a supported LUT file.");
            var points = new List<(double Rpm, double Torque)>();
            foreach (var raw in ReadData(source, file).Split('\n'))
            {
                var line = raw.Split(';')[0].Split('#')[0].Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                var pair = line.Split('|');
                if (pair.Length != 2) throw new InvalidDataException("The engine curve contains an unsupported row.");
                var rpm = Number(pair[0], 0, 100000, "engine-curve RPM");
                var torque = Number(pair[1], 0, 100000, "engine-curve torque");
                if (points.Count > 0 && rpm <= points[^1].Rpm) throw new InvalidDataException("Engine curve RPM points repeat or are unordered.");
                points.Add((rpm, torque));
                if (points.Count > 4096) throw new InvalidDataException("The engine curve contains too many points.");
            }
            if (points.Count < 3) throw new InvalidDataException("At least three readable engine-curve points are needed.");
            var idle = engine.Optional("ENGINE_DATA", "MINIMUM") is { } min ? Number(min, 0, 30000, "engine minimum RPM") : 500;
            var low = Math.Ceiling(Math.Max(Math.Max(500, idle), points[0].Rpm) / 100) * 100;
            var high = Math.Floor(Math.Min(limiter * .95, points[^1].Rpm) / 100) * 100;
            if (high - low < 500) throw new InvalidDataException("The readable curve has too little usable RPM coverage below the setup rev limit.");
            var samples = new List<(double Rpm, double Power)>();
            int right = 1;
            for (var rpm = low; rpm <= high; rpm += 100)
            {
                while (right < points.Count - 1 && points[right].Rpm < rpm) right++;
                var a = points[right - 1]; var b = points[right];
                var torque = a.Torque + (b.Torque - a.Torque) * (rpm - a.Rpm) / (b.Rpm - a.Rpm);
                samples.Add((rpm, rpm * torque));
            }
            var peak = samples.Select((s, i) => (s.Power, i)).OrderByDescending(p => p.Power).First();
            if (peak.Power <= 0) throw new InvalidDataException("The engine curve has no positive base power.");
            int start = peak.i, end = peak.i;
            while (start > 0 && samples[start - 1].Power >= peak.Power * .8) start--;
            while (end < samples.Count - 1 && samples[end + 1].Power >= peak.Power * .8) end++;
            if (samples[end].Rpm - samples[start].Rpm < 500) throw new InvalidDataException("The curve's high-power region is too narrow for a useful starting band.");
            return new(samples[start].Rpm, samples[end].Rpm,
                $"Starting estimate from {file}: the continuous region around peak base power at or above 80% of that peak, sampled every 100 RPM and capped at 95% of the setup rev limit. No extrapolation. Turbo boost, ECU maps, hybrid systems and scripts are not included; confirm or adjust this band after driving.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        { return new(null, null, "Automatic RPM target unavailable: " + ex.Message + " Enter a known range under Advanced; the rev limit alone is not a power band."); }
    }
}
