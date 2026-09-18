using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    private RecordingEvidenceProgress _evidence = new();
    private Task<RecordingEvidenceProgress>? _evidenceTask;
    private string _evidenceSessionId = "";
    private double _nextEvidenceSeconds;

    private void ResetLiveEvidence()
    {
        _evidence = new(); _nextEvidenceSeconds = 0;
        RenderEvidence();
    }

    private void UpdateLiveEvidence(double elapsed)
    {
        if (_evidenceTask?.IsCompleted == true)
        {
            if (_evidenceTask.IsCompletedSuccessfully && _evidenceSessionId == _session.Id)
                _evidence = _evidenceTask.Result;
            else if (_evidenceTask.IsFaulted) _ = _evidenceTask.Exception; // Presentation failure never stops recording.
            _evidenceTask = null;
            RenderEvidence();
        }
        if (_evidenceTask is not null || elapsed < _nextEvidenceSeconds) return;
        _nextEvidenceSeconds = elapsed + (_session.Samples.Count >= RecordingEvidenceService.LongRecordingSamples ? 5 : 2);
        if (_session.Samples.Count > RecordingEvidenceService.MaximumLiveSamples)
        {
            _evidence = _evidence with { State = "review-on-save", ReadyToReview = false,
                Message = "Long recording. Stop and save for the full review.",
                Details = "Live counters are paused. Every captured sample remains available to the saved-run analysis." };
            RenderEvidence(); return;
        }
        // Captured samples are never mutated after append. Copy the list and context
        // on the UI thread, then analyze the isolated snapshot away from the 50 Hz sampler.
        var snapshot = new TelemetrySession { Id = _session.Id, Samples = _session.Samples.ToList(),
            Context = _session.Context is null ? null : RunHistoryStore.Clone(_session.Context) };
        _evidenceSessionId = _session.Id;
        _evidenceTask = Task.Run(() => new RecordingEvidenceService().Update(snapshot, elapsed, force: true));
    }

    private RecordingEvidenceProgress CurrentEvidence()
    {
        if (_session.Context?.SetupCaptureIssue.Length > 0)
            return _evidence with { State = "setup-unverified", ReadyToReview = false,
                Message = _sessionSaved ? "Run saved with a setup limitation. Review it, then record a fresh run."
                    : "Setup evidence changed or was lost. Save this run for inspection.", Details = _session.Context.SetupCaptureIssue + " " + _evidence.Details };
        if (_telemetryUnavailableSince is not null)
            return _evidence with { State = "waiting", ReadyToReview = false,
                Message = "Waiting for telemetry. Missing time does not count toward evidence." };
        if (!_recording && _session.Samples.Count > 0)
            return _evidence with { Message = _sessionSaved ? "Run saved. Review the findings for your next step."
                : "Recording stopped. Save the run to review its findings." };
        return _evidence;
    }

    private void RenderEvidence()
    {
        var progress = CurrentEvidence();
        EvidenceMessageText.Text = progress.Message;
        EvidenceDetailsText.Text = progress.Details;
    }
}
