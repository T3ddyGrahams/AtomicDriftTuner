namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    public Func<string?>? RecordingBlockReason { get; set; }
    internal bool IsRecordingForPitSetup => _recording;

    internal void InvalidateSetupAfterPitAction()
    {
        // Completed prior runs keep their immutable snapshots.
        if (_recording) return;
        _liveSetup = null; _setupNonce = ""; _companionRevision++;
        TuneInUseCheck.IsChecked = false;
        _setupCaptureMessage = "Pit setup action: wait for a new capture and confirm the setup before your next run. Manual attachments must match the setup now loaded in AC.";
        if (AutomaticSetup) RefreshSetupCapture();
        else
        {
            _setupSnapshotPath = "";
            SetupSnapshotText.Text = _setupCaptureMessage;
        }
    }
}
