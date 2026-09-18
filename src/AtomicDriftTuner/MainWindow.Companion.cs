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
                string.Equals(_telemetryWindow.DriverBox.Text.Trim(), _workflow.Preferences().DriverName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                CompanionPlanMatches();
        }
        catch { return false; }
    }

    private async Task<CompanionStatus> GetCompanionStatusAsync(CancellationToken cancellationToken)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshGuidedWorkflow(); // Remote Desired Behavior edits must also refresh the next-step guidance.
            var hub = _telemetryHub.GetSnapshot();
            return new CompanionStatus
            {
                TelemetryConnected = hub.Connected, TelemetryStale = hub.Stale,
                NextStep = _guidedReady ? GuidedStepText.Text : "Set up your workflow in desktop ADT.",
                Instructions = GuidedInstructionsText.Text,
                Details = GuidedDetailsText.Text,
                Completion = GuidedDoneText.Text,
                Progress = GuidedProgressText.Text,
                Recorder = _telemetryWindow?.GetCompanionState(CompanionContextMatches()) ?? new(),
                Workflow = BuildCompanionWorkflowState(),
                SetupCapture = _telemetryWindow?.OfferSetupCapture()
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

    private async Task<RemoteActionResponse> ReceiveCompanionSetupAsync(LiveSetupCaptureRequest request, CancellationToken cancellationToken) =>
        await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _telemetryWindow?.ReceiveSetupCapture(request)
                ?? new RemoteActionResponse { Ok = false, Message = "Open the desktop recorder first." };
        }).Task;

    private async Task<RemoteActionResponse> ExecuteCompanionWorkflowCommandAsync(CompanionWorkflowCommand command, CancellationToken cancellationToken)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteCompanionWorkflowCommand(command);
        }).Task;
    }
}
