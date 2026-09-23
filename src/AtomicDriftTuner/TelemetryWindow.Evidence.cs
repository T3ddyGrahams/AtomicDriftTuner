using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using System.Windows;

namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    private RecordingEvidenceProgress _evidence = new();
    private Task<RecordingEvidenceProgress>? _evidenceTask;
    private string _evidenceSessionId = "";
    private double _nextEvidenceSeconds;
    private readonly RecordingReadyNotification _readyNotification = new();
    private Action _playReadyChime = () => System.Media.SystemSounds.Asterisk.Play();
    private bool _evidencePreferencesReady;

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
            return _evidence with { State = _sessionSaved ? "saved" : "stopped", ReadyToReview = false, NeededEvidence = Array.Empty<string>(), Message = _sessionSaved ? "Run saved. Review the findings for your next step."
                : "Recording stopped. Save the run to review its findings." };
        if (!_recording) return new() { State = "idle", Message = "Start a run to see what evidence is still needed." };
        return _evidence;
    }

    private void RenderEvidence()
    {
        var progress = CurrentEvidence();
        EvidenceHeadingText.Text = progress.Heading;
        EvidenceMessageText.Text = progress.Message;
        EvidenceDetailsText.Text = progress.Details;
        EvidenceNeededText.Text = string.Join("\n", progress.NeededEvidence.Skip(1).Select(x => "• " + x));
        EvidenceNeededText.Visibility = progress.NeededEvidence.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        EvidenceBanner.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty,
            progress.ReadyToReview ? "AccentBrush" : "BorderBrush");
        if (_readyNotification.Observe(_session.Id, _recording, progress, ReadyChimeCheck.IsChecked == true))
        {
            try { _playReadyChime(); }
            catch { ReadyChimeStatusText.Text = "Sound is unavailable. The recording guidance remains active."; }
        }
    }

    private void ReadyChime_Changed(object sender, RoutedEventArgs e)
    {
        if (!_evidencePreferencesReady) return;
        try
        {
            var store = new AppSettingsStore();
            var settings = store.Load();
            settings.RecordingReadyChime = ReadyChimeCheck.IsChecked == true;
            store.Save(settings);
            ReadyChimeStatusText.Text = "";
        }
        catch { ReadyChimeStatusText.Text = "This sound choice applies now, but could not be saved for next time."; }
    }
}
