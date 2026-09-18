namespace AtomicDriftTuner.Models;

/// <summary>Provisional recording guidance, separate from saved findings and improvement verdicts.</summary>
public sealed record RecordingEvidenceProgress
{
    public string State { get; init; } = "collecting";
    public string Message { get; init; } = "Collecting useful driving evidence.";
    public string Details { get; init; } = "Drive normally. Stop and save when you are ready to review the recording.";
    public double UsableDriftSeconds { get; init; }
    public double TargetDriftSeconds { get; init; } = 20;
    public int Entries { get; init; }
    public int Transitions { get; init; }
    public int CompletedAngleAttempts { get; init; }
    public bool ReadyToReview { get; init; }
    public bool IsProvisional { get; init; } = true;
}
