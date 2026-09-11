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
        var unsaved = !_sessionSaved && _session.Samples.Count > 0;
        var connected = _telemetry.GetSnapshot().Connected;
        var ready = DriverBox.Text.Trim().Length > 0 && ConditionsBox.Text.Trim().Length > 0;
        var state = _recording ? "recording" : unsaved ? "unsaved" : _session.Samples.Count > 0 ? "saved" : "ready";
        var message = _recording ? "Recording in ADT. Stop when your run is complete."
            : unsaved ? _analysis is null ? "Captured samples need attention in desktop ADT; analysis did not complete." : _sessionInterrupted ? "Recording interrupted. Save the partial run or review it in ADT." : "Recording stopped. Save this run before recording again."
            : !contextMatches ? "The selected car/rig or driver changed. Prepare the matching recorder in desktop ADT."
            : !ready ? "Enter a driver and conditions/driving task in the desktop recorder."
            : !connected ? "Waiting for live AC telemetry. Enter an on-track session."
            : _session.Samples.Count > 0 ? "Run saved. Ready for another recording; confirm any changed setup in ADT."
            : "Ready to record. ADT will use the details prepared in the desktop recorder.";
        // A delayed command must not act on a replacement run or edited capture plan.
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            _companionRevision, DriverBox.Text, Conditions = ConditionsBox.Text, Label = TuneLabelBox.Text,
            Change = TestedChangeBox.Text, Baseline = (RecommendationRunBox.SelectedItem as SavedTelemetrySession)?.Session.Id,
            Setup = _setupSnapshotPath, Confirmed = TuneInUseCheck.IsChecked, contextMatches
        }))));
        return new CompanionRecorderState
        {
            WindowId = _companionWindowId, SessionId = _session.Id, ControlVersion = version,
            Car = _input.Car.DisplayName, Driver = _recording || unsaved ? _session.Context?.DriverName ?? DriverBox.Text : DriverBox.Text,
            State = state, Message = message, Samples = _session.Samples.Count, ElapsedSeconds = _clock.Elapsed.TotalSeconds,
            CanStart = !_recording && !unsaved && ready && connected && contextMatches,
            CanStop = _recording, CanSave = !_recording && unsaved && _analysis is not null
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
