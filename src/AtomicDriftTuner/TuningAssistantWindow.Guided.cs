using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;
public partial class TuningAssistantWindow
{
    public event Action<SavedTelemetrySession, string>? RecommendationTestRequested;
    public event Action<RunReview>? RunReviewSaved;
    public event Action<SavedTelemetrySession, string>? GuidedSetupSaved;
    public void FocusGuidedSession(string driverId, string sessionId)
    {
        if (_guidedSetupWindow is not null) return;
        var drivers = HistoryDriverBox.ItemsSource as IEnumerable<DriverIdentity>;
        HistoryDriverBox.SelectedItem = drivers?.FirstOrDefault(d => d.Id == driverId) ?? drivers?.FirstOrDefault();
        RefreshSessions();
        if (sessionId.Length == 0) return;
        SessionBox.SelectedItem = _sessions.FirstOrDefault(s => s.Session.Id == sessionId && s.Session.Context?.DriverId == driverId);
        if (SessionBox.SelectedItem is null) StatusText.Text = "The planned run is not in the recent list. Choose the correct run explicitly; no replacement was selected.";
        else AssistantTabs.SelectedIndex = ((SavedTelemetrySession)SessionBox.SelectedItem).Session.Context?.RecommendationSessionId.Length > 0 ? 3 : 1;
    }
    private void TestRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (_guidedSetupWindow is not null) { StatusText.Text = "Close AC Setup with Guidance before choosing a new test."; return; }
        if (_reportSession is null || RecommendationGrid.SelectedItem is not AssistantRecommendation recommendation)
        { StatusText.Text = "Select one row in Recommendations, then choose Test This Recommendation."; return; }
        if (!RunHistoryStore.ValidContext(_reportSession.Session.Context))
        { StatusText.Text = "This older run has no complete driver/tune context. Record a new baseline before starting a guided test."; return; }
        try
        {
            RecommendationTestRequested?.Invoke(_reportSession, recommendation.Domain + ": " + recommendation.Change);
            StatusText.Text = "Test plan saved. The dashboard now explains how to prepare and record the comparison. Your recorded Desired Behavior remains fixed.";
        }
        catch (Exception ex) { StatusText.Text = "Test plan could not be created: " + ex.Message; }
    }
}
