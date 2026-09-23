using System.Security.Cryptography;
using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    private CapturedCarSetup? _liveSetup;
    private CapturedCarSetup? _runSetup;
    private string _setupNonce = "";
    private DateTime _setupChallengeUtc;
    private DateTime _lastSetupMonitorUtc;
    private DateTime _liveSetupDeadlineUtc;
    private string _setupChallengeSession = "";
    private string _setupCaptureMessage = "Pair the updated in-game companion to capture the current setup, or attach a setup file manually.";
    private const double SetupFreshSeconds = 5;
    private bool AutomaticSetup => UseAutomaticSetupCheck.IsChecked == true;

    private void MonitorSetupCapture()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSetupMonitorUtc).TotalSeconds is >= 0 and < .5) return;
        _lastSetupMonitorUtc = now;
        RefreshSetupCapture();
    }

    internal SetupCaptureOffer? OfferSetupCapture()
    {
        Dispatcher.VerifyAccess();
        RefreshSetupCapture();
        if (!AutomaticSetup || !_telemetry.GetSnapshot().Connected) return null;
        // Reuse the outstanding challenge so simultaneous phone status polls cannot
        // invalidate the in-game client's response. A response can consume it once.
        if (_setupNonce.Length == 0 || (DateTime.UtcNow - _setupChallengeUtc).TotalSeconds > SetupFreshSeconds || _setupChallengeSession != _session.Id)
        {
            _setupNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _setupChallengeUtc = DateTime.UtcNow;
            _setupChallengeSession = _session.Id;
        }
        return new() { Nonce = _setupNonce, WindowId = _companionWindowId };
    }

    internal RemoteActionResponse ReceiveSetupCapture(LiveSetupCaptureRequest request)
    {
        Dispatcher.VerifyAccess();
        if (!AutomaticSetup || request.WindowId != _companionWindowId || request.Nonce != _setupNonce || _setupNonce.Length == 0 ||
            _setupChallengeSession != _session.Id || (DateTime.UtcNow - _setupChallengeUtc).TotalSeconds is < 0 or > SetupFreshSeconds)
            return new() { Ok = false, Message = "Setup request expired. Read current recorder status." };
        _setupNonce = "";
        var beforeRevision = _companionRevision;
        var beforeFingerprint = _liveSetup?.Sha256;
        var identity = _readSessionIdentity();
        var parsed = LiveSetupCaptureService.TryParse(request, out var captured, out var error);
        if (!_telemetry.GetSnapshot().Connected || identity is null || !parsed || captured is null ||
            !string.Equals(identity.CarModel, _input.Car.SourceFolderName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(captured.CarId, identity.CarModel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(captured.TrackId, identity.Track, StringComparison.OrdinalIgnoreCase))
        {
            LoseSetupCapture(!parsed ? error : "The capture does not match live AC telemetry and this recorder's car/track.");
            // A valid challenge with unavailable/unsupported evidence is an acknowledged
            // read, not a failed command. Never keep the previous snapshot as current.
            return new() { Ok = true, Message = _setupCaptureMessage, RefreshStatus = beforeRevision != _companionRevision || beforeFingerprint is not null };
        }
        var previous = _liveSetup;
        _liveSetup = captured;
        // Conservatively age the observation from the challenge, so request latency
        // cannot turn a delayed snapshot into five additional seconds of freshness.
        _liveSetupDeadlineUtc = _setupChallengeUtc.AddSeconds(SetupFreshSeconds);
        if (_recording && _runSetup is not null &&
            (captured.Sha256 != _runSetup.Sha256 || !SameSetupSession(captured, _runSetup) ||
             previous is not null && (captured.CaptureSequence <= previous.CaptureSequence || captured.Frame < previous.Frame || captured.SimTimeMs < previous.SimTimeMs)))
            InvalidateRunSetup("The setup or game session changed during recording. Save this run for inspection and start a clean run.");
        if (!_recording && (previous is null || captured.Sha256 != previous.Sha256 || !SameSetupSession(captured, previous)))
        {
            TuneInUseCheck.IsChecked = false;
            _companionRevision++;
        }
        _setupCaptureMessage = $"Current setup captured from CSP: {captured.Values.Count} numeric values. Unsaved pit edits are included by supported CSP versions.";
        if (captured.UnassignedValue.HasValue)
            _setupCaptureMessage += " One unnamed value is also monitored. Its control is unidentified, so ADT can review this run but cannot attribute improvement to a specific setup change.";
        RefreshSetupCapture();
        return new() { Ok = true, Message = _setupCaptureMessage,
            RefreshStatus = beforeRevision != _companionRevision || beforeFingerprint != _liveSetup?.Sha256 };
    }

    private static bool SameSetupSession(CapturedCarSetup a, CapturedCarSetup b) =>
        a.CarId == b.CarId && a.TrackId == b.TrackId && a.TrackLayout == b.TrackLayout &&
        a.SessionIndex == b.SessionIndex && a.SessionType == b.SessionType && a.SessionGeneration == b.SessionGeneration;

    private CapturedCarSetup? FreshSetup()
    {
        if (!AutomaticSetup || _liveSetup is null || DateTime.UtcNow > _liveSetupDeadlineUtc || (DateTime.UtcNow - _liveSetup.ReceivedUtc).TotalSeconds is < 0 or > SetupFreshSeconds ||
            !_telemetry.GetSnapshot().Connected) return null;
        var identity = _readSessionIdentity();
        return identity is not null && string.Equals(identity.CarModel, _input.Car.SourceFolderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.CarModel, _liveSetup.CarId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.Track, _liveSetup.TrackId, StringComparison.OrdinalIgnoreCase) ? _liveSetup : null;
    }

    private void LoseSetupCapture(string reason)
    {
        if (_liveSetup is not null) { _companionRevision++; TuneInUseCheck.IsChecked = false; }
        _liveSetup = null;
        _setupCaptureMessage = "Automatic setup unavailable. " + reason + " Attach the setup file manually if needed.";
        if (_recording && _runSetup is not null) InvalidateRunSetup("Automatic setup monitoring was lost during recording. The saved setup can no longer be confirmed for the whole run.");
        RefreshSetupCapture();
    }

    private void InvalidateRunSetup(string reason)
    {
        if (_session.Context is not { } context || context.SetupCaptureIssue.Length > 0) return;
        context.SetupCaptureIssue = reason;
        context.TuneConfirmedInUse = false;
        // Keep all samples and the immutable start snapshot. Comparison will expose
        // this limitation instead of attributing a mixed run to the original setup.
        StatusText.Text = reason;
    }

    private void RefreshSetupCapture()
    {
        if (!AutomaticSetup) return;
        if (_liveSetup is not null && FreshSetup() is null)
        {
            LoseSetupCapture("Waiting for fresh setup evidence from the in-game companion.");
            return;
        }
        SetupSnapshotText.Text = _recording && _session.Context?.SetupCaptureIssue.Length > 0
            ? _session.Context.SetupCaptureIssue : _setupCaptureMessage;
        RecorderConfirmationText.Text = RecordingFocus == TuningFocus.CarSetupOnly
            ? "I confirm these are the settings I intend to test, and my in-game FFB and wheelbase settings are unchanged."
            : "I confirm I am using the generated ADT FFB targets and intend to test the current captured car setup. Hardware settings are not read automatically.";
    }

    private void AutomaticSetup_Click(object sender, RoutedEventArgs e)
    {
        _setupNonce = ""; _liveSetup = null; TuneInUseCheck.IsChecked = false; _companionRevision++;
        if (AutomaticSetup) RefreshSetupCapture();
        else
        {
            SetupSnapshotText.Text = string.IsNullOrWhiteSpace(_setupSnapshotPath) ? "Attach the saved setup actually loaded in AC. Unsaved edits are not included in a file attachment."
                : "Manual attachment: " + Path.GetFileName(_setupSnapshotPath) + ". Confirm this matches the setup loaded in AC.";
            RecorderConfirmationText.Text = RecordingFocus == TuningFocus.CarSetupOnly
                ? "I confirm the attached setup is loaded in AC and my in-game FFB and wheelbase settings are unchanged."
                : "I confirm I am using the generated ADT FFB targets and the attached setup for this run.";
        }
    }
}
