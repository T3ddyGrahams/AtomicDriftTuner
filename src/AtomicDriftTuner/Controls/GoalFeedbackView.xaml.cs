using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Controls;

public partial class GoalFeedbackView : UserControl
{
    private readonly Dictionary<string, GoalFeedback> _drafts = [];
    private GoalFeedback? _feedback;
    private bool _binding;
    private readonly string _group = Guid.NewGuid().ToString("N");
    public event EventHandler? SaveRequested;
    public event EventHandler? NextTestRequested;
    public event EventHandler? AnswersChanged;
    public GoalFeedback? Snapshot => _feedback is null ? null : RunHistoryStore.Clone(_feedback);
    public bool AllowNextTest { get; set; }
    public GoalFeedbackView() => InitializeComponent();

    public void Bind(GoalFeedback? feedback, GoalFeedback? saved = null)
    {
        if (_feedback is not null) _drafts[_feedback.ContextFingerprint] = RunHistoryStore.Clone(_feedback);
        _feedback = feedback is null ? null : RunHistoryStore.Clone(feedback);
        if (_feedback is not null)
        {
            GoalFeedbackEngine.RestoreAnswers(_feedback, _drafts.GetValueOrDefault(_feedback.ContextFingerprint) ?? saved);
            if (_drafts.Count > 200) _drafts.Remove(_drafts.Keys.First());
        }
        Render();
    }
    private void Render()
    {
        _binding = true;
        try
        {
            QuestionsPanel.Children.Clear(); ProblemPanel.Children.Clear();
            Form.Visibility = _feedback is null ? Visibility.Collapsed : Visibility.Visible;
            ContextText.Text = _feedback?.ContextDescription ?? "Choose a saved run and a baseline with recorded driver and goal information. Section reviews also need two complete passes.";
            if (_feedback is null) return;
            foreach (var q in _feedback.Questions)
            {
                var label = new TextBlock { Text = q.Prompt + (q.IsTestGoal ? " · tested goal" : ""), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SectionHeadingBrush"); QuestionsPanel.Children.Add(label);
                var options = new WrapPanel(); QuestionsPanel.Children.Add(options);
                foreach (var rating in new[] { FeedbackRating.Better, FeedbackRating.Same, FeedbackRating.Worse, FeedbackRating.CouldNotJudge })
                    AddOption(options, _group + q.Key, GoalFeedbackEngine.Label(rating), q.Prompt, q.Answer == rating, () => { q.Answer = rating; Changed(); });
                var evidence = new TextBlock { Text = "Telemetry: " + q.Evidence, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) };
                evidence.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush"); QuestionsPanel.Children.Add(evidence);
            }
            foreach (var answer in new[] { ProblemRating.No, ProblemRating.Yes, ProblemRating.CouldNotJudge })
                AddOption(ProblemPanel, _group + "problem", answer == ProblemRating.CouldNotJudge ? "Couldn't judge" : answer.ToString(), "New problem", _feedback.NewProblem == answer, () => { _feedback.NewProblem = answer; Changed(); });
            NotesBox.Text = _feedback.ProblemNotes;
            SaveStatus.Text = _feedback.HasAnswers ? "Loaded answers for this exact comparison. Save to keep a new review; earlier reviews remain available." : "Answers remain a draft until you save. Saving a review does not apply or revert settings.";
        }
        finally { _binding = false; }
        UpdateGuidance();
    }
    private void AddOption(Panel panel, string group, string label, string question, bool selected, Action select)
    {
        var button = new RadioButton { GroupName = group, Content = label, IsChecked = selected, Margin = new Thickness(0, 2, 16, 5), Padding = new Thickness(3), MinHeight = 28 };
        button.Style = (Style)FindResource("FeedbackAnswerStyle"); AutomationProperties.SetName(button, question + ": " + label);
        button.Checked += (_, _) => { if (!_binding) select(); }; panel.Children.Add(button);
    }
    private void Changed()
    {
        if (_binding) return;
        SaveStatus.Text = "Unsaved answers. Save this review to keep it after closing ADT.";
        UpdateGuidance(); AnswersChanged?.Invoke(this, EventArgs.Empty);
    }
    private void NotesChanged(object sender, TextChangedEventArgs e) { if (!_binding && _feedback is not null) { _feedback.ProblemNotes = NotesBox.Text; Changed(); } }
    private void UpdateGuidance()
    {
        if (_feedback is null) return;
        var guidance = GoalFeedbackEngine.Evaluate(_feedback); ActionText.Text = guidance.Action; GuidanceText.Text = guidance.Message;
        SaveButton.IsEnabled = _feedback.HasAnswers;
        NextTestButton.Visibility = AllowNextTest && guidance.CanReviewNextTest ? Visibility.Visible : Visibility.Collapsed;
    }
    public void Saved(string message) => SaveStatus.Text = message;
    private void SaveClicked(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);
    private void NextClicked(object sender, RoutedEventArgs e) => NextTestRequested?.Invoke(this, EventArgs.Empty);
    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        if (_feedback is null) return;
        foreach (var q in _feedback.Questions) q.Answer = FeedbackRating.NotAnswered;
        _feedback.NewProblem = ProblemRating.NotAnswered; _feedback.ProblemNotes = ""; Render(); Changed();
    }
}
