using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckPitSetup(string repo, string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Pit setup UI: " + why); checks++; }
        static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        static Dictionary<string, byte[]> Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);
        static bool SameFiles(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after) => before.Count == after.Count &&
            before.All(pair => after.TryGetValue(pair.Key, out var bytes) && pair.Value.SequenceEqual(bytes));

        var directory = Path.Combine(output, "pit-setup-" + Guid.NewGuid().ToString("N"));
        var carPath = Path.Combine(directory, "isolated_pit_setup_fixture");
        var data = Path.Combine(carPath, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[DIFF_POWER]\nMIN=0\nMAX=100\nSTEP=1\n[CAMBER_LF]\nMIN=-10\nMAX=-2\nSTEP=1\nSHOW_CLICKS=1\n");
        File.AppendAllText(Path.Combine(data, "setup.ini"), "[ARB_REAR]\nMIN=0\nMAX=30000\nSTEP=1000\nSHOW_CLICKS=0\n[FRONT_BIAS]\nMIN=55\nMAX=100\nSTEP=1\n[FRONT_BIAS]\nMIN=45\nMAX=85\nSTEP=1\n");
        var baseline = Path.Combine(directory, "baseline.ini");
        const string baselineText = "[CAR]\nMODEL=isolated_pit_setup_fixture\n[DIFF_POWER]\nVALUE=60\n[CAMBER_LF]\nVALUE=-55\n[ARB_REAR]\nVALUE=4000\n[FRONT_BIAS]\nVALUE=60\n";
        File.WriteAllText(baseline, baselineText);
        var originalFiles = Files(directory);
        var input = new TuneInput();
        input.Intent.Name = "Pit fixture drift";
        input.Intent.Kind = DriftStyleKind.Competition;
        input.Car.Id = "isolated_pit_setup_fixture";
        input.Car.SourceFolderPath = carPath;
        // Avoid saved-setup discovery and its user settings lookup during construction.
        input.Car.SourceFolderName = null;
        var window = new CarSetupWindow(input, assistantBehaviorOverride: new CarBehaviorTarget());
        try
        {
            input.Car.SourceFolderName = "isolated_pit_setup_fixture";
            var store = Get(window, "_behaviorStore")!;
            Set(store, "_directory", directory); Set(store, "_path", Path.Combine(directory, "behavior.json"));
            var choiceType = typeof(CarSetupWindow).GetNestedType("SetupChoice", BindingFlags.NonPublic)!;
            var choice = Activator.CreateInstance(choiceType, baseline)!;
            var baselineBox = (ComboBox)window.FindName("BaselineBox");
            baselineBox.ItemsSource = new[] { choice }; baselineBox.SelectedIndex = 0;
            var stage = (Button)window.FindName("StagePitSetupButton");
            var save = (Button)window.FindName("SaveGeneratedButton");
            var clear = (Button)window.FindName("ClearPendingPitSetupButton");
            var status = (TextBlock)window.FindName("SetupStatusText");
            var staged = new List<PitSetupPlan>();
            window.StagePitSetupHandler = (analysis, label) =>
            {
                var plan = new PitSetupPlanService().Create(analysis, input.Car.SourceFolderName!, label);
                staged.Add(plan);
                return "Fixture plan staged; no game or file write.";
            };
            void Generate() { Call(window, "GenerateSetup_Click", window, new RoutedEventArgs()); Flush(); }
            void Stage() { Call(window, "StagePitSetup_Click", window, new RoutedEventArgs()); Flush(); }

            Flush();
            Check(!stage.IsEnabled && !save.IsEnabled, "staging/export enabled before generation");
            Check(stage.Content.ToString() == "Stage for in-game pits" && clear.Content.ToString() == "Clear pending pit action",
                "pit actions have ambiguous or outdated labels");
            Stage();
            Check(staged.Count == 0 && status.Text.Contains("Generate"), "a direct stage call bypassed the generation guard");
            Generate();
            Check(save.IsEnabled && stage.IsEnabled, "valid generation did not enable both export and staging");
            Check(staged.Count == 0 && SameFiles(originalFiles, Files(directory)), "generation staged a plan or wrote a setup");
            Stage();
            Check(staged.Count == 1 && staged[0].CarId == input.Car.SourceFolderName && staged[0].Label == "ADT " + input.Intent.Name &&
                staged[0].Changes.Any(c => c.Section == "DIFF_POWER" && c.Before == 60 && c.After != c.Before),
                "stage did not dispatch the reviewed numeric plan with its car/label");
            Check(status.Text.Contains("Fixture plan staged") && SameFiles(originalFiles, Files(directory)),
                "staging did not show the result or wrote a setup file");
            Check(staged[0].Changes.Any(c => c.Section == "CAMBER_LF" && c.Before == -55 && c.After == -56),
                "the actual Stage button did not preserve camber saved-tenths units");
            Check(staged[0].Changes.All(c => c.Section != "FRONT_BIAS" && c.Section != "ARB_REAR") &&
                ((TextBlock)window.FindName("DecodeWarningsText")).Text.Contains("FRONT_BIAS"),
                "conflicting brake bias or sub-step ARB was changed, or the warning was hidden");
            var frozen = JsonSerializer.Serialize(staged[0]);

            ((Slider)window.FindName("FrontEndBiteSlider")).Value = 1; Flush();
            Check(!save.IsEnabled && !stage.IsEnabled, "behavior change left stale stage/export enabled");
            Stage();
            Check(staged.Count == 1 && JsonSerializer.Serialize(staged[0]) == frozen,
                "behavior edit applied/staged automatically or changed the prior immutable plan");
            Generate();
            Set(window, "_generatedSignature", "stale-signature"); Stage();
            Check(staged.Count == 1 && !stage.IsEnabled && !save.IsEnabled && status.Text.Contains("Generate again"),
                "a stale generation signature reached the staging callback");

            Generate();
            File.AppendAllText(baseline, "; external edit\n"); Stage();
            Check(staged.Count == 1 && !stage.IsEnabled && status.Text.Contains("Generate again"),
                "a baseline changed after generation was staged");
            File.WriteAllText(baseline, baselineText); Generate();
            var baselineTimestamp = File.GetLastWriteTimeUtc(baseline);
            File.WriteAllText(baseline, baselineText.Replace("VALUE=60", "VALUE=61"));
            File.SetLastWriteTimeUtc(baseline, baselineTimestamp); Stage();
            Check(staged.Count == 1 && status.Text.Contains("baseline", StringComparison.OrdinalIgnoreCase),
                "a same-size baseline edit with preserved timestamp bypassed final numeric validation");
            File.WriteAllText(baseline, baselineText); Generate();
            ((ComboBox)window.FindName("AggressivenessBox")).SelectedItem = SetupAggressiveness.Conservative; Flush(); Stage();
            Check(staged.Count == 1 && !stage.IsEnabled && !save.IsEnabled, "aggressiveness change allowed stale staging");
            Generate();
            save.IsEnabled = false; Flush(); Check(!stage.IsEnabled, "stage enablement is detached from the export validity gate");
            save.IsEnabled = true; Flush(); Check(stage.IsEnabled, "stage binding did not follow restored export availability");

            var cleared = 0;
            window.ClearPendingPitSetupHandler = () => { cleared++; throw new InvalidOperationException("Close Assetto Corsa first."); };
            Call(window, "ClearPendingPitSetup_Click", window, new RoutedEventArgs());
            Check(cleared == 1 && status.Text == "Close Assetto Corsa first." && staged.Count == 1,
                "a rejected clear action hid its reason or staged another plan");
            Check(SameFiles(originalFiles, Files(directory)) && JsonSerializer.Serialize(staged[0]) == frozen,
                "generation/staging/refusals changed fixture files or the previously staged plan");

            var content = (FrameworkElement)window.Content;
            foreach (var size in new[] { new Size(430, 300), new Size(680, 900), new Size(1200, 540) })
            {
                Layout(content, size);
                foreach (var name in new[] { "SaveGeneratedButton", "StagePitSetupButton", "ClearPendingPitSetupButton" })
                    AssertVisible(content, name, size);
            }
        }
        finally { window.Close(); }

        input.Car.SourceFolderName = null;
        var goals = new CarSetupWindow(input, assistantBehaviorOverride: new CarBehaviorTarget(), behaviorOnly: true);
        try
        {
            foreach (var name in new[] { "SaveGeneratedButton", "StagePitSetupButton", "ClearPendingPitSetupButton" })
                Check(((Button)goals.FindName(name)).Visibility == Visibility.Collapsed, "goals-only view exposes " + name);
        }
        finally { goals.Close(); }
        input.Car.SourceFolderName = "isolated_pit_setup_fixture";
        var labels = XElement.Load(Path.Combine(repo, "src", "AtomicDriftTuner", "MainWindow.xaml")).Descendants()
            .Where(e => e.Name.LocalName == "Button").Select(e => (string?)e.Attribute("Content")).ToArray();
        Check(labels.Contains("Update recommendations only") && !labels.Contains("Apply Feedback + Regenerate"),
            "feedback regeneration still implies that it applies a tune");

        using var hub = new TelemetryHubService();
        using var physics = MemoryMappedFile.CreateNew(null, 4096);
        Set(Get(hub, "_reader")!, "_physicsMap", physics);
        void FreshHub()
        {
            Set(hub, "_latest", new TelemetrySample { PacketId = 10, TimeSeconds = 100, SpeedKmh = 60, Gear = 3 });
            Set(hub, "_updatedUtc", DateTimeOffset.UtcNow);
        }
        FreshHub();
        var recorder = new TelemetryWindow(input, hub, () => new() { CarModel = input.Car.SourceFolderName!, Track = "isolated_track" });
        try
        {
            Set(recorder, "_history", new RunHistoryStore(Path.Combine(directory, "history")));
            Set(Get(recorder, "_sessionStore")!, "<RootDirectory>k__BackingField", Path.Combine(directory, "sessions"));
            var calibration = Get(recorder, "_calibrationStore")!;
            Set(calibration, "_directory", directory); Set(calibration, "_path", Path.Combine(directory, "calibration.json"));
            Set(calibration, "_backupPath", Path.Combine(directory, "calibration.backup.json"));
            ((ComboBox)recorder.FindName("DriverBox")).Text = "Pit fixture driver";
            ((TextBox)recorder.FindName("ConditionsBox")).Text = "isolated dry practice";
            ((CheckBox)recorder.FindName("UseAutomaticSetupCheck")).IsChecked = false;
            Call(recorder, "AutomaticSetup_Click", recorder, new RoutedEventArgs());
            string? block = null;
            recorder.RecordingBlockReason = () => block;
            FreshHub(); var ready = recorder.GetCompanionState(true);
            Check(ready.CanStart, "fake live recorder was not otherwise ready before the pit block");
            var initialSession = Get(recorder, "_session");
            var beforeRecording = Files(directory);
            block = "Pit setup result is unknown. Sync the result or close AC before clearing.";
            FreshHub(); var blocked = recorder.GetCompanionState(true);
            Check(!blocked.CanStart && blocked.Message == block && blocked.ControlVersion != ready.ControlVersion,
                "pending pit action did not remove advertised recording permission/invalidate stale controls");
            var oldCommand = new CompanionCommand { Action = "start", WindowId = ready.WindowId, SessionId = ready.SessionId, ControlVersion = ready.ControlVersion };
            Check(!recorder.ExecuteCompanionCommand(oldCommand, true).Ok, "pre-pit recording command remained valid");
            var refused = false;
            try { Call(recorder, "StartRecording", false); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalid && invalid.Message == block) { refused = true; }
            Check(refused && !(bool)Get(recorder, "_recording")! && ReferenceEquals(initialSession, Get(recorder, "_session")),
                "desktop recording start bypassed the pit block or replaced the session");
            Check(SameFiles(beforeRecording, Files(directory)), "blocked recording created history/session/calibration files");
            block = null; FreshHub();
            Check(recorder.GetCompanionState(true).CanStart, "acknowledged pit block left the recorder permanently unavailable");
            var confirmed = (CheckBox)recorder.FindName("TuneInUseCheck"); confirmed.IsChecked = true;
            Set(recorder, "_setupSnapshotPath", baseline);
            Set(recorder, "_setupNonce", "old-capture-challenge");
            Call(recorder, "InvalidateSetupAfterPitAction");
            Check(confirmed.IsChecked == false && (string)Get(recorder, "_setupNonce")! == "" && ReferenceEquals(initialSession, Get(recorder, "_session")),
                "completed pit action kept old confirmation/challenge or overwrote earlier session evidence");
            Check(string.IsNullOrEmpty((string?)Get(recorder, "_setupSnapshotPath")) &&
                ((TextBlock)recorder.FindName("SetupSnapshotText")).Text.Contains("Pit setup action") &&
                SameFiles(beforeRecording, Files(directory)),
                "manual attachment stayed selected, its replacement instruction was hidden, or earlier files were changed");
        }
        finally
        {
            ((DispatcherTimer)Get(recorder, "_timer")!).Stop(); Set(recorder, "_recording", false); recorder.Close();
        }
        Progress($"PASS {checks} pit setup UI assertions; explicit staging, stale-plan refusals, isolated files, goals-only controls and recorder blocking.");
    }
}
