using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckEvidenceNotifications(string output)
    {
        static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void RenderEvidence(TelemetryWindow window) => typeof(TelemetryWindow).GetMethod("RenderEvidence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        using var hub = new TelemetryHubService();
        var window = new TelemetryWindow(new TuneInput(), hub);
        var sounds = 0;
        Set(window, "_evidencePreferencesReady", false); // Never persist the fixture's checkbox choice.
        Set(window, "_playReadyChime", (Action)(() => sounds++));
        ((CheckBox)window.FindName("ReadyChimeCheck")).IsChecked = true;
        var ready = new RecordingEvidenceProgress { State = "ready", ReadyToReview = true, Message = "Enough evidence to review. Stop and save when ready." };
        try
        {
            Set(window, "_recording", true); Set(window, "_evidence", ready);
            RenderEvidence(window); RenderEvidence(window);
            if (sounds != 1 || ((TextBlock)window.FindName("EvidenceHeadingText")).Text != "READY TO REVIEW" || !(bool)Get(window, "_recording"))
                throw new Exception("Readiness must notify once and keep recording.");
            Set(window, "_telemetryUnavailableSince", 1d); RenderEvidence(window);
            if (((TextBlock)window.FindName("EvidenceHeadingText")).Text != "WAITING FOR TELEMETRY") throw new Exception("Stale readiness remained visible.");
            Set(window, "_telemetryUnavailableSince", null); RenderEvidence(window);
            if (sounds != 1) throw new Exception("Reconnection replayed readiness chime.");
            var session = (TelemetrySession)Get(window, "_session");
            session.Id = Guid.NewGuid().ToString("N"); RenderEvidence(window);
            if (sounds != 2) throw new Exception("New recording did not notify.");
            Set(window, "_evidence", ready with { State = "more-evidence", ReadyToReview = false, Message = "Record more clean entries.", NeededEvidence = new[] { "Record more clean entries.", "Include clean transitions.", "Hold the requested angle, then recover." } });
            RenderEvidence(window);
            if (((TextBlock)window.FindName("EvidenceNeededText")).Visibility != Visibility.Visible) throw new Exception("Missing-goal checklist is hidden.");
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            root.Resources.MergedDictionaries.Add(window.Resources);
            NameScope.SetNameScope(root, NameScope.GetNameScope(window));
            foreach (var size in new[] { new Size(430, 700), new Size(800, 480), new Size(1500, 800) })
            {
                Layout(root, size);
                foreach (var name in new[] { "EvidenceHeadingText", "EvidenceNeededText", "ReadyChimeCheck", "EvidenceBanner" })
                    AssertReachableByScrolling(root, "TelemetryBodyScroll", name, size);
                Render(root, size, Path.Combine(output, $"Evidence-{size.Width}-{size.Height}.png"));
            }
            window.Content = root;
            session.Samples.Add(new TelemetrySample());
            Set(window, "_recording", false); Set(window, "_sessionSaved", false); Set(window, "_evidence", ready); RenderEvidence(window);
            if (((TextBlock)window.FindName("EvidenceHeadingText")).Text != "SAVE YOUR RUN" || sounds != 2) throw new Exception("Stopped recording still claims live readiness.");
            session.Samples.Clear(); Set(window, "_sessionSaved", true);
        }
        finally { Set(window, "_recording", false); Set(window, "_sessionSaved", true); window.Close(); }
        Progress("PASS evidence banner, missing goals, optional one-shot sound, reconnect, recording isolation and responsive layout.");
    }
}
