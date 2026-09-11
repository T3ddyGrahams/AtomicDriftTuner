using System.Diagnostics;
using System.IO;
using System.Reflection;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckCompanionRecorder(string output)
    {
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Companion recorder: " + message); count++; }
        static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static CompanionCommand Command(CompanionRecorderState state, string action) => new() { Action = action, WindowId = state.WindowId, SessionId = state.SessionId, ControlVersion = state.ControlVersion };
        var root = Path.Combine(output, "companion-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var hub = new TelemetryHubService();
        hub.Dispose(); // No real shared memory, game or wheelbase is accessed by these fixtures.
        var window = new TelemetryWindow(new TuneInput(), hub);
        try
        {
            var store = Get(window, "_sessionStore");
            var sessionRoot = Path.Combine(root, "sessions");
            Set(store, "<RootDirectory>k__BackingField", sessionRoot);
            var initial = window.GetCompanionState(true);
            Check(!initial.CanStart && !initial.CanSave && !initial.CanStop, "offline recorder permits commands");
            Check(!window.ExecuteCompanionCommand(Command(initial, "start"), true).Ok, "offline start accepted");
            var session = (TelemetrySession)Get(window, "_session");
            session.CarName = "Companion fixture";
            for (var i = 0; i < 100; i++) session.Samples.Add(new TelemetrySample { TimeSeconds = i * 0.02, SpeedKmh = 55, PacketId = i });
            Set(window, "_recording", true); Set(window, "_sessionSaved", false);
            ((Stopwatch)Get(window, "_clock")).Start();
            var recording = window.GetCompanionState(false);
            Check(recording.CanStop && !recording.CanStart && !recording.CanSave, "recording flags");
            Check(window.ExecuteCompanionCommand(Command(recording, "stop"), false).Ok, "context change prevented stopping original run");
            var stopped = window.GetCompanionState(false);
            Check(stopped.CanSave && !stopped.CanStart && stopped.Samples == 100, "stop lost samples or allowed replacement");
            Check(stopped.ControlVersion != recording.ControlVersion, "stop did not invalidate earlier commands");
            Check(!window.ExecuteCompanionCommand(Command(recording, "stop"), false).Ok, "duplicate stop accepted");
            Check(!window.ExecuteCompanionCommand(Command(stopped, "start"), true).Ok, "unsaved run replaced");
            var failurePath = Path.Combine(root, "blocked-file"); File.WriteAllText(failurePath, "preserve");
            Set(store, "<RootDirectory>k__BackingField", failurePath);
            Check(!window.ExecuteCompanionCommand(Command(stopped, "save"), false).Ok, "failed disk save reported success");
            Check(window.GetCompanionState(false).CanSave && session.Samples.Count == 100, "failed save lost retryable session");
            Set(store, "<RootDirectory>k__BackingField", sessionRoot);
            var save = Command(window.GetCompanionState(false), "save");
            Check(window.ExecuteCompanionCommand(save, false).Ok, "save retry failed");
            Check(!window.GetCompanionState(false).CanSave && window.GetCompanionState(false).State == "saved", "saved state not reflected");
            Check(!window.ExecuteCompanionCommand(save, false).Ok, "duplicate save accepted");
            Check(Directory.GetFiles(sessionRoot, "session.json", SearchOption.AllDirectories).Length == 1 && Directory.GetFiles(sessionRoot, "telemetry.csv", SearchOption.AllDirectories).Length == 1, "save did not produce exactly one JSON/CSV pair");
            Check(File.ReadAllText(failurePath) == "preserve", "failed save changed unrelated file");
            var beforeEdit = window.GetCompanionState(true);
            ((System.Windows.Controls.TextBox)window.FindName("ConditionsBox")).Text = "different plan";
            Check(beforeEdit.ControlVersion != window.GetCompanionState(true).ControlVersion, "edited plan retained old concurrency token");
        }
        finally { window.Close(); }
        Progress($"PASS {count} companion recorder assertions; real stop/save, duplicate commands and disk-failure recovery.");
    }
}
