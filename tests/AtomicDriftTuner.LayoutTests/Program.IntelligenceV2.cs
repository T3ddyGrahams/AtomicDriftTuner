using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckIntelligenceV2(string output)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Intelligence 2.0 recorder: " + why); checks++; }
        static object? Get(object o, string field) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
        static void Set(object o, string field, object? value) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        var f = new CarTestFixture(output); var choice = f.Build().Single();
        var testFile = f.Service.Save(f.Run, choice, Path.Combine(f.DirectoryPath, "Test.ini"));
        using var hub = new TelemetryHubService(); hub.Dispose();
        var window = new TelemetryWindow(f.Input, hub, () => new() { CarModel = f.Input.Car.SourceFolderName!, Track = "fixture" });
        try
        {
            Set(Get(window, "_sessionStore")!, "<RootDirectory>k__BackingField", Path.Combine(f.DirectoryPath, "sessions"));
            Set(window, "_history", new RunHistoryStore(Path.Combine(f.DirectoryPath, "history")));
            var calibration = Get(window, "_calibrationStore")!;
            Set(calibration, "_directory", f.DirectoryPath); Set(calibration, "_path", Path.Combine(f.DirectoryPath, "calibration.json"));
            Set(calibration, "_backupPath", Path.Combine(f.DirectoryPath, "calibration.backup.json"));
            Set(window, "_lastSavedForGuide", f.Run);
            var c = f.Run.Session.Context!;
            var plan = new RecordingPlan(c.DriverId, c.DriverName, f.Run.Session.Id, choice.Test.Description, testFile, c.Conditions,
                TuningFocus.CarSetupOnly, true) { Test = choice.Test };
            window.UseRecordingPlan(plan, force: true);
            ((CheckBox)window.FindName("UseAutomaticSetupCheck")).IsChecked = false;
            ((CheckBox)window.FindName("TuneInUseCheck")).IsChecked = true;
            window.UseRecordingPlan(RunHistoryStore.Clone(plan));
            Check(((CheckBox)window.FindName("TuneInUseCheck")).IsChecked == true, "Reopening the identical frozen plan lost confirmation.");
            RunContext Capture() => (RunContext)typeof(TelemetryWindow).GetMethod("CaptureRunContext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            var recorded = Capture();
            Check(recorded.Test?.Id == choice.Test.Id && recorded.Test.Changes.Count == 2 && recorded.Tune!.Settings["ACSetup.PRESSURE_LR"] == 26,
                "Recording did not carry exact plan and actual changed values.");
            Check(!ReferenceEquals(recorded.Test, choice.Test), "Recorder shares mutable plan objects.");
            ((TextBox)window.FindName("TestedChangeBox")).Text = "Trying my own settings";
            Check(Capture().Test is null, "An edited description retained the ADT plan.");
            ((CheckBox)window.FindName("DriverDefinedTestCheck")).IsChecked = true;
            recorded = Capture();
            Check(recorded.Test?.Origin == RecommendationTestService.Driver && recorded.Test.Changes.Count == 2,
                "Explicit custom test lost provenance or values.");
            window.UseRecordingPlan(plan, force: true);
            Check(((CheckBox)window.FindName("DriverDefinedTestCheck")).IsChecked == false, "Reloading an ADT plan retained custom-test mode.");
            var content = (FrameworkElement)window.Content;
            foreach (var size in new[] { new Size(430, 600), new Size(680, 900), new Size(1200, 600) })
            {
                Layout(content, size);
                AssertReachableByScrolling(content, "TelemetryBodyScroll", "DriverDefinedTestCheck", size);
                Render(content, size, Path.Combine(output, $"IntelligenceV2-Recorder-{size.Width}.png"));
            }
        }
        finally { window.Close(); }
        Progress($"PASS {checks} intelligence 2.0 recorder assertions plus responsive test-control reachability.");
    }
}
