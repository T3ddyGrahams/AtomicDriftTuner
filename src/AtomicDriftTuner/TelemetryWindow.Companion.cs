using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    private readonly string _companionWindowId = Guid.NewGuid().ToString("N");
    private long _companionRevision;

    public CompanionRecorderState GetCompanionState(bool contextMatches)
    {
        Dispatcher.VerifyAccess();
        RefreshSetupCapture();
        var unsaved = !_sessionSaved && _session.Samples.Count > 0;
        var connected = _telemetry.GetSnapshot().Connected;
        var ready = DriverBox.Text.Trim().Length > 0 && ConditionsBox.Text.Trim().Length > 0;
        var state = _recording ? "recording" : unsaved ? "unsaved" : _session.Samples.Count > 0 ? "saved" : "ready";
        var message = _recording ? _telemetryUnavailableSince is not null
                ? $"Waiting for fresh AC telemetry; recording will resume if it recovers within {TelemetryRecoverySeconds:0} seconds. Return to driving, or stop and save this partial run."
                : "Recording in ADT. Stop when your run is complete."
            : unsaved ? _analysis is null ? "Captured samples need attention in desktop ADT; analysis did not complete." : _sessionInterrupted
                ? (_session.StopReason.Length > 0 ? _session.StopReason + " " : "Recording interrupted. ") + "Save the partial run or review it in ADT."
                : "Recording stopped. Save this run before recording again."
            : _sessionInterrupted && _session.Samples.Count == 0 ? _session.StopReason + " No frames were captured. Start again when live AC telemetry returns."
            : !contextMatches ? "The car, rig, driver or test plan changed. Check Your Next Step and prepare the matching recording before starting."
            : !ready ? "Enter a driver and conditions/driving task in the desktop recorder."
            : !connected ? "Waiting for live AC telemetry. Enter an on-track session."
            : _session.Samples.Count > 0 ? "Run saved. Ready for another recording; confirm any changed setup in ADT."
            : "Ready to record. ADT will use the details prepared in the desktop recorder.";
        // A delayed command must not act on a replacement run or edited capture plan.
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            _companionRevision, RecordingFocus, DriverBox.Text, Conditions = ConditionsBox.Text, Label = TuneLabelBox.Text,
            Change = TestedChangeBox.Text, Baseline = (RecommendationRunBox.SelectedItem as SavedTelemetrySession)?.Session.Id,
            Setup = AutomaticSetup ? _liveSetup?.Sha256 : _setupSnapshotPath, AutomaticSetup, Confirmed = TuneInUseCheck.IsChecked, contextMatches
        }))));
        return new CompanionRecorderState
        {
            WindowId = _companionWindowId, SessionId = _session.Id, ControlVersion = version,
            Car = _input.Car.DisplayName, Driver = _recording || unsaved ? _session.Context?.DriverName ?? DriverBox.Text : DriverBox.Text,
            State = state, Message = message, Samples = _session.Samples.Count, ElapsedSeconds = _clock.Elapsed.TotalSeconds,
            CanStart = !_recording && !unsaved && ready && connected && contextMatches,
            CanStop = _recording, CanSave = !_recording && unsaved && _analysis is not null,
            Evidence = _recording || _session.Samples.Count > 0 ? CurrentEvidence() : null,
            SetupMessage = _session.Context?.SetupCaptureIssue.Length > 0 ? _session.Context.SetupCaptureIssue : SetupSnapshotText.Text
        };
    }

    public RemoteActionResponse ExecuteCompanionCommand(CompanionCommand command, bool contextMatches)
    {
        Dispatcher.VerifyAccess();
        var rejection = CompanionCommandRules.Reject(command, GetCompanionState(contextMatches));
        if (rejection is not null) return new() { Ok = false, Message = rejection };
        try
        {
            switch (command.Action)
            {
                case "start": StartRecording(); break;
                case "stop": Stop_Click(this, new RoutedEventArgs()); break;
                case "save": SaveAnalyzedSession(); break;
            }
            var message = command.Action == "save" ? "Session saved in ADT (JSON and CSV)."
                : command.Action == "start" ? "Recording started." : GetCompanionState(contextMatches).Message;
            return new() { Ok = true, Message = message };
        }
        catch (Exception ex)
        {
            StatusText.Text = "Companion recording command failed: " + ex.Message;
            return new() { Ok = false, Message = ex is InvalidOperationException ? ex.Message : "ADT could not complete the recording command. Your captured samples are retained; check the desktop recorder for details." };
        }
    }
}
