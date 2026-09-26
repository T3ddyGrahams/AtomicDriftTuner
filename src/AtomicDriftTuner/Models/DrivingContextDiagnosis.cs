namespace AtomicDriftTuner.Models;

public sealed class DrivingContextDiagnosis
{
    public string Version { get; set; } = "driving-context/1";
    public List<DrivingContextObservation> Observations { get; set; } = [];
    public string Limitations { get; set; } = "Speed and direction describe these recorded sections, not their cause. Different corners, lines, tyre state and inputs can explain differences. Wheel-slip share is not tyre force; steering motion does not establish hands-off self-steer. Known backward travel is excluded; unavailable travel direction remains unknown.";
}

public sealed class DrivingContextObservation
{
    public string MetricKey { get; set; } = "";
    public string Phase { get; set; } = "";
    public int SpeedBand { get; set; }
    public string Direction { get; set; } = "";
    public double? Value { get; set; }
    public double EvidenceSeconds { get; set; }
    public int Events { get; set; } = -1;
    public string Confidence { get; set; } = "LOW";
    public double? ThrottlePct { get; set; }
    public double? BrakePct { get; set; }
    public double? ClutchSignalPct { get; set; }
    public double KnownPedalSeconds { get; set; }
    public double UnknownTravelSeconds { get; set; }
    public string SpeedLabel => SpeedBand switch { 0 => "below 50 km/h", 1 => "50–<90 km/h", 2 => "90+ km/h", _ => "crosses speed bands" };
    public string Context => $"{Phase} · {SpeedLabel} · {Direction}";
    public string DisplayContext(bool mph) => !mph ? Context : $"{Phase} · {SpeedBand switch {
        0 => "below ~31 mph", 1 => "~31–56 mph", 2 => "~56+ mph", _ => "crosses speed bands" }} · {Direction}";
    public string Evidence => $"{EvidenceSeconds:0.0}s in this condition" + (Events >= 0 ? $"; {Events} complete event(s)" : "") +
        (ThrottlePct is double throttle ? $". Mean inputs: throttle {throttle:0}%, brake {BrakePct:0}%, raw clutch signal {ClutchSignalPct:0}% (not a clutch-kick diagnosis)." : ". Pedal coverage is insufficient; inputs are unknown.") +
        (UnknownTravelSeconds > 0 ? $" Travel direction unverified for {UnknownTravelSeconds:0.0}s." : "") +
        " Durations accumulate across clean sections; no single long continuous drift is required.";
}
