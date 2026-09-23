using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class CarSetupTestWindow : Window
{
    private readonly TuneInput _input;
    private readonly SavedTelemetrySession _run;
    private readonly TuningAssistantReport _report;
    private readonly AssistantCarTestService _service = new();
    public Func<CarSetupAnalysis, string, string>? StageHandler { get; init; }
    public Action<string, string?>? TestPrepared { get; init; }
    private AssistantCarTestService.Choice? Selected => ChoiceBox.SelectedItem as AssistantCarTestService.Choice;

    public CarSetupTestWindow(TuneInput input, SavedTelemetrySession run, TuningAssistantReport report)
    {
        InitializeComponent();
        _input = RunHistoryStore.Clone(input); _run = RunHistoryStore.Clone(run); _report = RunHistoryStore.Clone(report);
        // Next-step recommendations are UI-only and intentionally excluded from JSON history.
        _report.NextStep.Recommendation = report.NextStep.Recommendation is { } recommendation ? RunHistoryStore.Clone(recommendation) : null;
        RunText.Text = $"{run.DisplayName}\n{AssistantCarTestGoal(report)}";
        FindingText.Text = AssistantCarTestService.FindingSummary(run, report);
        ExpectationText.Text = "Try one change, then compare how the car feels and what the next run shows. A recommendation is a test, not a confirmed fix.";
        FindingEvidenceText.Text = report.NextStep.Recommendation?.Why ?? report.NextStep.Why;
        BaselineHelpText.Text = $"Recorded setup: {run.Session.Context?.Tune?.SetupFileName}. Choose a saved file with the same numeric values. ADT checks it against the run before showing a change. For an unsaved CSP capture, save that same setup in AC first.";
        WindowBoundsService.Attach(this);
    }
    private static string AssistantCarTestGoal(TuningAssistantReport report) => "Recorded goal: " + report.NextStep.Goal;
    private void BrowseBaseline_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", Title = "Choose the setup used in the selected run", CheckFileExists = true };
        try
        {
            var root = new AssettoCorsaSetupService().GetDefaultSetupsRoot();
            var directory = Path.Combine(root, _input.Car.SourceFolderName ?? "");
            if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
            var recordedName = _run.Session.Context?.Tune?.SetupFileName ?? "";
            if (recordedName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) dialog.FileName = Path.GetFileName(recordedName);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { TestStatusText.Text = "Browse to Documents → Assetto Corsa → setups → your car, then choose the setup used in this run."; }
        if (dialog.ShowDialog(this) == true) LoadBaseline(dialog.FileName);
    }
    private void LoadBaseline(string path)
    {
        ChoiceBox.ItemsSource = null; SaveTestButton.IsEnabled = StageTestButton.IsEnabled = false;
        ChangesText.Text = "No verified setup change yet."; DetailsText.Text = ""; BaselinePathBox.Text = path;
        try
        {
            ChoiceBox.ItemsSource = _service.Build(_input, _run, _report, path);
            ChoiceBox.SelectedIndex = 0;
            TestStatusText.Text = "Baseline matches this run. Review the proposed values, then save or stage this one test.";
        }
        catch (Exception ex) { TestStatusText.Text = ex.Message; }
    }
    private void Choice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SaveTestButton is null) return;
        SaveTestButton.IsEnabled = Selected is not null;
        StageTestButton.IsEnabled = Selected is not null && StageHandler is not null;
        ChangesText.Text = Selected?.Changes ?? "No verified setup change yet.";
        DetailsText.Text = Selected?.Details ?? "";
        AdjustmentHelpText.Text = Selected?.Explanation ?? "";
        if (Selected is not null) TestStatusText.Text = "Review this option. A new selection does not replace any file or previously staged plan.";
    }
    private void SaveTest_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } choice) return;
        var dialog = new SaveFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", DefaultExt = ".ini", AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(BaselinePathBox.Text), FileName = "ADT test - " + choice.Name + ".ini", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) SaveTo(dialog.FileName);
    }
    private void SaveTo(string path)
    {
        if (Selected is not { } choice) return;
        try
        {
            var saved = _service.Save(_run, choice, path);
            TestStatusText.Text = $"Saved {Path.GetFileName(saved)}. Load it in AC's pit setup menu, confirm it in the recorder, then record the comparison. The baseline is unchanged.";
            RememberTest(choice, saved);
        }
        catch (Exception ex) { Invalidate("Test setup was not saved: " + ex.Message); }
    }
    private void StageTest_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } choice || StageHandler is null) return;
        try
        {
            var result = _service.Stage(_run, choice, StageHandler);
            TestStatusText.Text = result + " In ADT Companion, choose Pit setup → Save & Apply Tune. Confirm the loaded setup before recording.";
            RememberTest(choice, null);
        }
        catch (Exception ex) { Invalidate("Test setup was not staged: " + ex.Message); }
    }
    private void RememberTest(AssistantCarTestService.Choice choice, string? path)
    {
        try
        {
            if (TestPrepared is null) throw new InvalidOperationException("Open the assistant from the dashboard to link this test to run history.");
            TestPrepared(choice.TestDescription, path);
        }
        catch (Exception ex) { TestStatusText.Text += " The file/staged plan is ready, but test tracking was not saved: " + ex.Message; }
    }
    private void Invalidate(string message)
    {
        ChoiceBox.ItemsSource = null; SaveTestButton.IsEnabled = StageTestButton.IsEnabled = false;
        ChangesText.Text = "Choose the baseline again to rebuild the preview."; DetailsText.Text = ""; TestStatusText.Text = message;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
