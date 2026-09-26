using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner;
using AtomicDriftTuner.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckFeedback(string output)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Feedback UI: " + why); checks++; }
        static T C<T>(FrameworkElement view, string name) where T : FrameworkElement => (T)view.FindName(name);
        static void Click(FrameworkElement v, string name) => C<Button>(v, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static RadioButton Option(GoalFeedbackView view, int question, string label) =>
            C<StackPanel>(view, "QuestionsPanel").Children.OfType<WrapPanel>().ElementAt(question).Children.OfType<RadioButton>().Single(b => (string)b.Content == label);
        static void Problem(GoalFeedbackView view, string label) => C<WrapPanel>(view, "ProblemPanel").Children.OfType<RadioButton>().Single(b => (string)b.Content == label).IsChecked = true;
        var (a, b) = FeedbackFixture.Pair(); var fresh = GoalFeedbackEngine.ForRun(a, b)!;
        var view = new GoalFeedbackView { AllowNextTest = true };
        view.Bind(fresh);
        Check(view.Snapshot is { HasAnswers: false } && !C<Button>(view, "SaveButton").IsEnabled, "A default answer was inferred");
        Option(view, 0, "Better").IsChecked = true; Option(view, 0, "Worse").IsChecked = true;
        Check(view.Snapshot!.Questions[0].Answer == FeedbackRating.Worse && Option(view, 0, "Better").IsChecked == false, "Multiple answers selected");
        Option(view, 0, "Better").IsChecked = true;
        for (int i = 1; i < fresh.Questions.Count; i++) Option(view, i, "Same").IsChecked = true;
        Problem(view, "No"); C<TextBox>(view, "NotesBox").Text = "Easier to catch the transition.";
        Check(C<TextBlock>(view, "ActionText").Text == "Keep and verify", "Guidance did not update");
        int saves = 0; view.SaveRequested += (_, _) => saves++;
        Check(saves == 0, "Answering saved automatically"); Click(view, "SaveButton"); Check(saves == 1, "Save not wired");
        var snapshot = view.Snapshot!;
        view.Bind(null); Check(C<StackPanel>(view, "Form").Visibility == Visibility.Collapsed, "Clear left old form");
        view.Bind(fresh); Check(view.Snapshot!.Questions[0].Answer == FeedbackRating.Better && C<TextBox>(view, "NotesBox").Text.Length > 0, "Run switch lost draft");
        var reopened = new GoalFeedbackView(); reopened.Bind(fresh, snapshot);
        Check(reopened.Snapshot!.Questions[0].Answer == FeedbackRating.Better, "Saved answers not restored");
        var (_, other) = FeedbackFixture.Pair(); reopened.Bind(GoalFeedbackEngine.ForRun(a, other), snapshot);
        Check(!reopened.Snapshot!.HasAnswers, "Other run inherited saved feedback");
        Problem(view, "Yes"); Check(C<TextBlock>(view, "ActionText").Text == "Review tradeoff", "New problem ignored");
        Click(view, "ClearButton"); Check(!view.Snapshot!.HasAnswers, "Clear did not clear only draft");
        Check(snapshot.Questions[0].Answer == FeedbackRating.Better, "Clearing modified saved snapshot");
        var noChange = RunHistoryStore.Clone(b); FeedbackFixture.Set(noChange, "transition", 1.6);
        var same = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, noChange)!, FeedbackRating.Same);
        view.Bind(same);
        Check(C<Button>(view, "NextTestButton").Visibility == Visibility.Visible, "Supported next-review entry hidden");
        int next = 0; view.NextTestRequested += (_, _) => next++; Click(view, "NextTestButton"); Check(next == 1 && saves == 1, "Next review changed/save state");
        var scroll = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        foreach (var size in new[] { new Size(430, 620), new Size(680, 900), new Size(1800, 750) })
        {
            Layout(scroll, size); Check(scroll.ScrollableWidth == 0, "Horizontal clipping");
            scroll.ScrollToBottom(); Layout(scroll, size);
            Check(C<Button>(view, "SaveButton").ActualWidth < size.Width && C<TextBlock>(view, "GuidanceText").ActualWidth < size.Width, "Actions or guidance clipped");
            Render(scroll, size, Path.Combine(output, $"Feedback-{size.Width}.png"));
        }

        var sectionStore = new TrackSectionStore(Path.Combine(output, "feedback-sections-" + Guid.NewGuid().ToString("N")));
        var section = TrackSectionEngine.Mark(a.Session, 1, 7, "Test corner", 30, 45, 2, 0, "Follow reference"); sectionStore.Save(section);
        var track = new TrackSectionView { Store = sectionStore };
        track.Bind(a, null); var sectionView = C<GoalFeedbackView>(track, "SectionFeedback");
        Check(sectionView.Snapshot is { Scope: "Section", Comparable: true }, "Section form missing");
        Option(sectionView, 0, "Better").IsChecked = true; Problem(sectionView, "No"); Click(sectionView, "SaveButton");
        Check(C<ComboBox>(track, "SectionReviewHistory").Items.Count == 1 && C<TextBlock>(sectionView, "SaveStatus").Text.StartsWith("Saved"), "Section save failed");
        C<ComboBox>(track, "AfterBox").SelectedIndex = 2;
        Check(!sectionView.Snapshot!.HasAnswers && C<ComboBox>(track, "SectionReviewHistory").Items.Count == 0, "Pass switch reused feedback");
        C<ComboBox>(track, "AfterBox").SelectedIndex = 1; Check(sectionView.Snapshot!.HasAnswers, "Pass draft disappeared");
        var reopenedTrack = new TrackSectionView { Store = sectionStore }; reopenedTrack.Bind(a, null);
        Check(C<GoalFeedbackView>(reopenedTrack, "SectionFeedback").Snapshot!.HasAnswers && C<ComboBox>(reopenedTrack, "SectionReviewHistory").Items.Count == 1, "Section reopen lost answers");
        C<ComboBox>(track, "SectionBox").SelectedIndex = -1; Check(sectionView.Snapshot is null, "Stale feedback survived deselection");

        // Exercise the real assistant save path and navigation without production startup or installed user data.
        var fixture = new CarTestFixture(output); var assistant = new TuningAssistantWindow(fixture.Input);
        static void Set(object o, string field, object? value) => o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, value);
        static object? Call(object o, string method, params object?[] args) => o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);
        try
        {
            var history = new RunHistoryStore(Path.Combine(fixture.DirectoryPath, "feedback-history")); Set(assistant, "_history", history);
            var pair = FeedbackFixture.Pair(RunHistoryStore.ContextKey(fixture.Input));
            Set(assistant, "_sessions", new List<SavedTelemetrySession> { pair.Before, pair.After });
            C<ComboBox>(assistant, "SessionBox").ItemsSource = new[] { pair.Before, pair.After };
            C<ComboBox>(assistant, "SessionBox").SelectedItem = pair.After;
            var quick = C<GoalFeedbackView>(assistant, "QuickFeedback");
            Check(quick.Snapshot?.BaselineSessionId == pair.Before.Session.Id, "Assistant baseline binding failed");
            for (int i = 0; i < quick.Snapshot!.Questions.Count; i++) Option(quick, i, i == 0 ? "Better" : "Same").IsChecked = true;
            Problem(quick, "No"); Click(quick, "SaveButton");
            Check(history.ListReviews(fixture.Input).Single().GoalFeedback!.HasAnswers, "Assistant did not persist quick review");
            Call(assistant, "RenderNextStep", new AssistantNextStep { Action = "Review" }); Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(((TabItem)C<TabControl>(assistant, "AssistantTabs").SelectedItem).Header.ToString() == "Before / After", "Next-step review missed quick feedback");
            C<ComboBox>(assistant, "SessionBox").SelectedItem = pair.Before;
            Check(quick.Snapshot is null, "Baseline run retained after-run answers");
            C<ComboBox>(assistant, "SessionBox").SelectedItem = pair.After;
            Check(quick.Snapshot!.HasAnswers, "Assistant run switch lost feedback");
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} feedback UI assertions; exclusive answers, history, pass/run switching, responsive renders, and explicit-save navigation.");
    }
}
