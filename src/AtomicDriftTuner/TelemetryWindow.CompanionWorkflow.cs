using System.Security.Cryptography;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TelemetryWindow
{
    internal sealed record CompanionPreparation(string Conditions, string SetupName, string SetupFingerprint,
        string ConfirmationText, bool Confirmed, bool SetupAvailable, bool Busy, string BaselineId);

    internal CompanionPreparation GetCompanionPreparation()
    {
        Dispatcher.VerifyAccess();
        RefreshSetupCapture();
        if (AutomaticSetup)
        {
            var current = FreshSetup();
            return new(ConditionsBox.Text.Trim(), current is null ? "Automatic capture waiting" : "Current CSP setup",
                current?.Sha256 ?? "", RecorderConfirmationText.Text, TuneInUseCheck.IsChecked == true && current is not null,
                current is not null, _recording || !_sessionSaved && _session.Samples.Count > 0,
                (RecommendationRunBox.SelectedItem as SavedTelemetrySession)?.Session.Id ?? "");
        }
        var fingerprint = "";
        if (!string.IsNullOrWhiteSpace(_setupSnapshotPath) && File.Exists(_setupSnapshotPath))
        {
            try
            {
                if (new FileInfo(_setupSnapshotPath).Length <= 2_000_000)
                    fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_setupSnapshotPath)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new(ConditionsBox.Text.Trim(), Path.GetFileName(_setupSnapshotPath ?? ""), fingerprint,
            RecorderConfirmationText.Text, TuneInUseCheck.IsChecked == true, fingerprint.Length > 0,
            _recording || !_sessionSaved && _session.Samples.Count > 0,
            (RecommendationRunBox.SelectedItem as SavedTelemetrySession)?.Session.Id ?? "");
    }

    internal SavedTelemetrySession? GetCompanionSavedRun()
    {
        Dispatcher.VerifyAccess();
        return _lastSavedForGuide is not null && _sessionSaved ? _lastSavedForGuide : null;
    }

    internal bool CompanionPlanMatches(RecordingPlan plan) => _recordingPlan is { } current &&
        current.DriverId == plan.DriverId && current.Focus == plan.Focus && current.FfbProvider == plan.FfbProvider && current.BaselineId == plan.BaselineId &&
        current.Recommendation == plan.Recommendation && current.SetupPath == plan.SetupPath &&
        string.Equals(DriverBox.Text.Trim(), plan.DriverName.Trim(), StringComparison.OrdinalIgnoreCase) &&
        ((RecommendationRunBox.SelectedItem as SavedTelemetrySession)?.Session.Id ?? "") == plan.BaselineId &&
        TestedChangeBox.Text.Replace("\r\n", "\n").Trim() == plan.Recommendation.Replace("\r\n", "\n").Trim();

    private List<SavedTelemetrySession> CompanionPlanSessions(RecordingPlan plan)
    {
        if (plan.BaselineId.Length == 0) return [];
        var baseline = _lastSavedForGuide?.Session.Id == plan.BaselineId ? _lastSavedForGuide :
            CompanionSavedRunLookup.Find(_sessionStore, plan.BaselineId);
        return baseline?.Session.Context is { Tune: not null } context && RunHistoryStore.ValidContext(context) &&
            context.DriverId == plan.DriverId && context.Focus == plan.Focus &&
            (!TuningFocusOptions.IncludesFfb(plan.Focus) || context.Tune.FfbProvider == plan.FfbProvider) && context.Tune.ContextKey == RunHistoryStore.ContextKey(_input)
            ? [baseline] : [];
    }

    internal void ConfirmCompanionPreparation()
    {
        var preparation = GetCompanionPreparation();
        if (preparation.Busy) throw new InvalidOperationException("Stop and save the captured run before changing its preparation.");
        if (!preparation.SetupAvailable || preparation.Conditions.Length == 0)
            throw new InvalidOperationException("Wait for current setup capture or attach the setup manually, and enter your conditions in desktop ADT first.");
        TuneInUseCheck.IsChecked = true;
        _companionRevision++;
    }
}
