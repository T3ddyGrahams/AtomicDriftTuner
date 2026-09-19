using System.Diagnostics;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class MainWindow
{
    private readonly PitSetupCoordinator _pitSetup = new();

    private string PitSetupContext(TuneInput input) => RunHistoryStore.ContextKey(input) + "|" +
        CurrentGuidedDriver().Id + "|" + _workflow.Preferences().Focus + "|" +
        GuidedWorkflowStore.GoalSignature(_behaviorStore.Load(input));

    internal string StagePitSetup(TuneInput input, CarSetupAnalysis analysis, string label)
    {
        Dispatcher.VerifyAccess();
        if (RunHistoryStore.ContextKey(BuildInput()) != RunHistoryStore.ContextKey(input))
            throw new InvalidOperationException("The selected car or rig changed. Reopen the car setup tuner for the current selection.");
        if (_telemetryWindow?.IsRecordingForPitSetup == true)
            throw new InvalidOperationException("Stop recording before staging a car setup.");
        var plan = new PitSetupPlanService().Create(analysis, input.Car.SourceFolderName ?? "", label);
        _pitSetup.Stage(plan, PitSetupContext(input));
        return _pitSetup.Status(PitSetupContext(input), plan.CarId, false).Message +
            (_remoteServer.IsRunning ? "" : " Start ADT Remote and pair the updated companion first.");
    }

    private PitSetupStatus BuildPitSetupStatus()
    {
        try
        {
            var input = BuildInput();
            return _pitSetup.Status(PitSetupContext(input), input.Car.SourceFolderName ?? "", _telemetryWindow?.IsRecordingForPitSetup == true);
        }
        catch { return _pitSetup.Status("unavailable", "", _telemetryWindow?.IsRecordingForPitSetup == true); }
    }

    private async Task<PitSetupResponse> ExecutePitSetupAsync(PitSetupCommand command, CancellationToken cancellationToken) =>
        await Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = "unavailable"; var carId = "";
            try { var input = BuildInput(); context = PitSetupContext(input); carId = input.Car.SourceFolderName ?? ""; } catch { }
            var wasBusy = _pitSetup.Busy;
            var result = _pitSetup.Execute(command, context, carId, _telemetryWindow?.IsRecordingForPitSetup == true);
            if (result.Ok && (command.Action == "prepare" || wasBusy && !_pitSetup.Busy))
                _telemetryWindow?.InvalidateSetupAfterPitAction();
            return result;
        }).Task;

    internal string ClearPendingPitSetup()
    {
        Dispatcher.VerifyAccess();
        foreach (var name in new[] { "acs", "acs_x86" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) throw new InvalidOperationException("Use Sync result in the companion first. To clear an unknown result manually, close Assetto Corsa completely, then try again."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        _pitSetup.ClearAfterGameExit(); _telemetryWindow?.InvalidateSetupAfterPitAction();
        return "Pending pit state cleared after AC closed. Restart the game, generate and stage a setup again.";
    }
}
