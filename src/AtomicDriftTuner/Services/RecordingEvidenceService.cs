using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Read-only, throttled presentation of the existing diagnosis. Call on the recorder's owning
/// thread, after adding samples; do not analyze its mutable sample list on another thread.
/// The saved-run analyzer remains authoritative and is never replaced by this guide.
/// </summary>
public sealed class RecordingEvidenceService
{
    public const int MaximumLiveSamples = 100_000;
    public const int LongRecordingSamples = 15_000;
    // A review opportunity, not a replacement for any diagnosis or comparison threshold.
    public const double PartialReviewDriftSeconds = 60;
    private readonly DriftDiagnosisEngine _diagnosis = new();
    private TelemetrySession? _session;
    private string _sessionId = "";
    private RunContext? _capturedContext;
    private TelemetryAnalysis? _analysis;
    private int _analyzedCount = -1;
    private double _lastUpdateSeconds = double.NegativeInfinity;

    public RecordingEvidenceProgress Current { get; private set; } = new();

    public void Reset()
    {
        _session = null; _sessionId = ""; _capturedContext = null; _analysis = null;
        _analyzedCount = -1; _lastUpdateSeconds = double.NegativeInfinity;
        Current = new();
    }

    /// <param name="elapsedSeconds">The recorder's monotonic elapsed time, not a telemetry timestamp.</param>
    /// <param name="force">Refresh after stopping even when the normal update interval has not elapsed.</param>
    public RecordingEvidenceProgress Update(TelemetrySession session, double elapsedSeconds, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!ReferenceEquals(session, _session) || session.Id != _sessionId || session.Samples.Count < _analyzedCount)
        {
            Reset(); _session = session; _sessionId = session.Id;
            // Recordings have a fixed goal snapshot. Later desktop goal edits must not reinterpret them.
            _capturedContext = RunHistoryStore.ValidContext(session.Context) ? RunHistoryStore.Clone(session.Context) : null;
        }
        if (session.Context?.Interrupted == true)
            return Current = WithCounts(_analysis) with { State = "interrupted", Message = "Recording interrupted. Save it, then start a fresh run.",
                Details = "Captured evidence is preserved for inspection. An interrupted run cannot establish whether a change improved the car." };
        if (session.Samples.Count > MaximumLiveSamples)
            return Current = WithCounts(_analysis) with { State = "review-on-save", Message = "Long recording. Stop and save for the full review.",
                Details = "Live evidence updates are paused to keep recording responsive. All captured samples remain available to the normal saved-run analysis; the counters shown are from the last live update." };
        var interval = session.Samples.Count >= LongRecordingSamples ? 5 : 2;
        if (!force && (_analyzedCount == session.Samples.Count || elapsedSeconds >= _lastUpdateSeconds && elapsedSeconds - _lastUpdateSeconds < interval))
            return Current;
        // Only the lightweight run diagnosis is used: no tune generation, file IO or history scans.
        var snapshot = new TelemetrySession { Context = _capturedContext, Samples = session.Samples };
        _analysis = _diagnosis.Analyze(snapshot);
        _analyzedCount = session.Samples.Count; _lastUpdateSeconds = elapsedSeconds;
        return Current = FromAnalysis(snapshot, _analysis);
    }

    private static RecordingEvidenceProgress WithCounts(TelemetryAnalysis? analysis) => new()
    {
        UsableDriftSeconds = analysis?.DriftTimeSeconds ?? 0,
        Entries = analysis?.DriftEntries ?? 0,
        Transitions = analysis?.TransitionCount ?? 0,
        CompletedAngleAttempts = analysis?.Diagnosis.AngleGoal.CompletedAttempts ?? 0
    };

    /// <summary>Project an existing final or background analysis without analyzing the samples again.</summary>
    public static RecordingEvidenceProgress FromAnalysis(TelemetrySession session, TelemetryAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        var progress = WithCounts(analysis);
        if (session.Context?.Interrupted == true)
            return progress with { State = "interrupted", Message = "Recording interrupted. Save it, then start a fresh run.",
                Details = "Captured evidence is preserved for inspection. An interrupted run cannot establish whether a change improved the car." };
        var summary = $"{progress.UsableDriftSeconds:0.0}s useful drift; {progress.Entries} entries; {progress.Transitions} transitions.";
        var quality = analysis.Diagnosis;
        var details = summary + " Only valid, continuous driving counts; gaps, pit/AI driving, off-track frames and impact windows are excluded when the signals are available. " +
            "Drift and cornering time add up across shorter sections; they do not need one uninterrupted ten-second corner or drift. Individual entries, transitions and angle attempts still need complete clean windows. " +
            "This guide updates periodically. Enough to review means evidence is available, not that the tune is better; the full analysis may still request another run.";
        if (quality.TimelineReset)
            return progress with { State = "interrupted", Message = "The telemetry session restarted. Save it, then record a fresh run.", Details = details };
        if (analysis.DriftTimeSeconds < 20)
            return progress with { Message = $"Collecting useful drift: {analysis.DriftTimeSeconds:0.0} / 20 s.", Details = details };
        if (!DriftDiagnosisEngine.Reliable(session, analysis))
            return progress with { State = "more-evidence", Message = "Capture a cleaner continuous run before comparing changes.",
                Details = summary + $" Sample rate {analysis.EffectiveSampleRateHz:0.0} Hz; {quality.Discontinuities} continuity breaks; {quality.InvalidSamples} invalid frames. " +
                    "Use a fresh recording if the signal stays patchy. You can still stop and save this run for inspection." };

        var goal = session.Context?.Tune?.DesiredBehavior;
        var needed = new List<string>();
        bool Missing(string key) => quality.Metric(key) is not { Value: not null, Confidence: "MEDIUM" or "HIGH" };
        string Seconds(string key) => (Math.Floor(Math.Max(0, quality.Metric(key)?.EvidenceSeconds ?? 0) * 10) / 10).ToString("0.0");
        if (goal is not null && TuningFocusOptions.IncludesCar(session.Context!.Focus))
        {
            if (goal.HasAngleGoal && Missing("angle-recovery"))
                needed.Add($"For your angle goal, record complete holds and returns below the target band ({quality.AngleGoal.CompletedAttempts} / 3 complete attempts). Keep driving for two seconds after each return.");
            if (goal.InitiationSharpness != 0 && Missing("initiation"))
                needed.Add($"For your initiation goal, include clean entries ({analysis.DriftEntries} / 3 recorded).");
            if (goal.TransitionSpeed != 0 && Missing("transition"))
                needed.Add($"For your transition goal, include clean direction changes ({analysis.TransitionCount} / 3 recorded).");
            if (goal.SelfSteerSpeed != 0 && Missing("self-steer"))
                needed.Add("For your steering-response goal, include more clean transition time. At least three transitions and ten seconds total across transitions are needed.");
            if (goal.AngleStability != 0 && Missing("stability"))
                needed.Add("For your stability goal, include more sustained drift between entries and transitions.");
            if (goal.RearGrip != 0 && Missing("rear-slip-share"))
                needed.Add("For your rear-grip goal, include more sustained drift with useful wheel-slip readings.");
            if (goal.FrontEndBite != 0)
            {
                if (Missing("front-response"))
                {
                    needed.Add($"Front response: {Seconds("front-response")} / {DriftDiagnosisEngine.MinimumMetricConfidenceSeconds:0} s total. Drive normal corners at {DriftDiagnosisEngine.FrontResponseMinimumSpeedKmh:0}+ km/h without drifting.");
                    details += $" Front-response time counts valid samples at {DriftDiagnosisEngine.FrontResponseMinimumSpeedKmh:0}+ km/h, body slip at most {DriftDiagnosisEngine.FrontResponseMaximumSlipDeg:0}°, and absolute recorded steering angle {DriftDiagnosisEngine.FrontResponseMinimumSteeringDeg:0}–{DriftDiagnosisEngine.FrontResponseMaximumSteeringDeg:0}°. Collect {DriftDiagnosisEngine.MinimumMetricConfidenceSeconds:0} seconds total; drifting alone does not complete this measurement.";
                }
                if (Missing("front-slip-share"))
                {
                    needed.Add($"Axle-slip evidence: {Seconds("front-slip-share")} / {DriftDiagnosisEngine.MinimumMetricConfidenceSeconds:0} s total. Include sustained drift with usable front and rear wheel-slip readings.");
                    if (quality.InvalidWheelSlipSamples > 0)
                        details += " Some wheel-slip readings are unusable; extra driving cannot guarantee this measurement will become available for this car.";
                }
            }
            if (goal.ThrottleSteering != 0 && Missing("throttle-rotation"))
                needed.Add("For your throttle-response goal, include more sustained drift on power.");
        }
        if (needed.Count > 0)
        {
            if (analysis.DriftTimeSeconds >= PartialReviewDriftSeconds)
                return progress with { State = "ready", ReadyToReview = true, HasGoalLimitations = true,
                    Message = "Useful drift is ready for partial review. Stop and save, or continue for the missing goals. " + needed[0],
                    NeededEvidence = needed.AsReadOnly(),
                    Details = "The listed goals remain insufficient. Saving lets you review the available evidence; it does not fill missing measurements or prove improvement.\n" + string.Join("\n", needed) + "\n" + details };
            return progress with { State = "more-evidence", Message = needed[0], NeededEvidence = needed.AsReadOnly(),
                Details = string.Join("\n", needed) + "\n" + details + $" You may stop and save at any time. With reliable telemetry, partial-review guidance appears after {PartialReviewDriftSeconds:0} seconds of useful drift even if some goals remain incomplete." };
        }
        return progress with { State = "ready", ReadyToReview = true, Message = "Enough evidence to review. Stop and save when ready.", Details = details };
    }
}
