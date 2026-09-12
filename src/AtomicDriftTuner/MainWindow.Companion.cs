using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class MainWindow
{
    private bool CompanionContextMatches()
    {
        try
        {
            return _telemetryWindow is not null && EmbeddedContextMatches(_telemetryWindow, BuildInput()) &&
                _telemetryWindow.RecordingFocus == _workflow.Preferences().Focus &&
                string.Equals(_telemetryWindow.DriverBox.Text.Trim(), _workflow.Preferences().DriverName.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task<CompanionStatus> GetCompanionStatusAsync(CancellationToken cancellationToken)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hub = _telemetryHub.GetSnapshot();
            return new CompanionStatus
            {
                TelemetryConnected = hub.Connected, TelemetryStale = hub.Stale,
                NextStep = _guidedReady ? GuidedStepText.Text : "Set up your workflow in desktop ADT.",
                Instructions = GuidedInstructionsText.Text,
                Recorder = _telemetryWindow?.GetCompanionState(CompanionContextMatches()) ?? new()
            };
        }).Task;
    }

    private async Task<RemoteActionResponse> ExecuteCompanionCommandAsync(CompanionCommand command, CancellationToken cancellationToken)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _telemetryWindow?.ExecuteCompanionCommand(command, CompanionContextMatches())
                ?? new RemoteActionResponse { Ok = false, Message = "Prepare Telemetry Recorder in desktop ADT first." };
        }).Task;
    }
}
