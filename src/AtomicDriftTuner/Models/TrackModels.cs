namespace AtomicDriftTuner.Models;

// Immutable so recording mailbox copies cannot share mutable position evidence.
public sealed record TrackPosition
{
    public string Source { get; init; } = "csp-track/1";
    public string Track { get; init; } = "";
    public string Layout { get; init; } = ""; // Empty is an explicitly captured default layout.
    public string Car { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double SourceTimeMs { get; init; }
    public double AlignmentUncertaintySeconds { get; init; }
    public double? SplineProgress { get; init; }
    public double? SplineX { get; init; }
    public double? SplineY { get; init; }
    public double? SplineZ { get; init; }
    public double? LeftBoundaryM { get; init; }
    public double? RightBoundaryM { get; init; }
}

public sealed record RoutePoint(double X, double Y, double Z);

// A new goal is a new immutable section revision. No edit rewrites a recording or review.
public sealed class TrackSection
{
    public string Schema { get; set; } = "adt/track-section/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Name { get; set; } = "";
    public string Track { get; set; } = "";
    public string Layout { get; set; } = "";
    public string ReferenceSessionId { get; set; } = "";
    public double ReferenceStartSeconds { get; set; }
    public double ReferenceEndSeconds { get; set; }
    public List<RoutePoint> Route { get; set; } = [];
    public double? MinimumAngle { get; set; }
    public double? MaximumAngle { get; set; }
    public int? PreferredGear { get; set; } // Driver-facing forward gear (AC raw gear minus one).
    public double TargetOffsetM { get; set; } // Signed X/Z map offset, displayed as the green aim.
    public string LineAim { get; set; } = "Follow reference";
    public override string ToString() => $"{Name} · {Track}/{(Layout.Length == 0 ? "default" : Layout)} · {Id[..Math.Min(6, Id.Length)]}";
}

public sealed class SectionPass
{
    public string SessionId { get; init; } = "";
    public string SectionId { get; init; } = "";
    public string Direction { get; init; } = "Reference direction";
    public double Start { get; init; }
    public double End { get; init; }
    public double Coverage { get; init; }
    public double Speed { get; init; }
    public double Throttle { get; init; }
    public double Brake { get; init; }
    public double Clutch { get; init; }
    public double BodyAngle { get; init; }
    public double? AngleInGoalPct { get; init; }
    public double? PreferredGearPct { get; init; }
    public double LineOffset { get; init; }
    public double LineError { get; init; }
    public double AlignmentUncertainty { get; init; }
    public List<RoutePoint> Path { get; init; } = [];
    public double[] Speeds { get; init; } = [];
    public double[] Throttles { get; init; } = [];
    public double[] Brakes { get; init; } = [];
    public double[] Clutches { get; init; } = [];
    public double[] LeftShares { get; init; } = [];
    public string Display => $"{Start:0.00}–{End:0.00}s · {Speed:0.0} km/h · {BodyAngle:0.0}° body · {Coverage:P0} coverage";
    public override string ToString() => Display;
}

public sealed class SectionPassResult
{
    public List<SectionPass> Passes { get; } = [];
    public string Summary { get; set; } = "";
}

public sealed record SectionComparison(bool Comparable, string Findings);
