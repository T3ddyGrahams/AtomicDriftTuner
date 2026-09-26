using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow
{
    private void RenderSetupComparison(SavedTelemetrySession selected, SavedTelemetrySession? previous)
    {
        if (previous is null) { RecordedSetupComparison.Clear("Choose an earlier baseline above to compare each recorded setup setting."); return; }
        var before = previous.Session.Context;
        var after = selected.Session.Context;
        var context = $"Before: {previous.DisplayName}\nRecorded after: {selected.DisplayName}\n" +
            "These are the settings saved with each run. Setup differences alone do not prove improvement; review the run comparison and how the car felt.";
        if (before?.TuneConfirmedInUse != true || after?.TuneConfirmedInUse != true)
            context += "\nSetup use was not confirmed for both runs.";
        if (before?.SetupCaptureIssue.Length > 0 || after?.SetupCaptureIssue.Length > 0)
            context += "\nSetup capture issue: " + string.Join("; ", new[] { before?.SetupCaptureIssue, after?.SetupCaptureIssue }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (before?.Tune?.HasUnassignedSetupValues == true || after?.Tune?.HasUnassignedSetupValues == true)
            context += "\nSome saved values could not be assigned to a named control; coverage is incomplete.";
        if (after?.TestedRecommendations.Count > 0)
            context += "\nRecorded test plan: " + string.Join("; ", after.TestedRecommendations) +
                (after.RecommendationSessionId == previous.Session.Id ? "" : " (not linked to this selected baseline)");
        if (_report?.Outcome is { } comparison)
            context += "\nTest verification: " + comparison.TestMatchSummary;
        if (before?.Tune is { } a && after?.Tune is { } b && a.SetupSha256 != b.SetupSha256)
            context += "\nThe setup fingerprint differs. The rows compare captured numeric values; file formatting or uncaptured fields may also differ.";
        RecordedSetupComparison.Show(SetupComparisonPresentation.Recorded(before?.Tune, after?.Tune), context);
    }
}
