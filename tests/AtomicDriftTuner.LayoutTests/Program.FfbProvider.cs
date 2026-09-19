using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckFfbProvider(string output)
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("FFB provider UI: " + message); checks++; }
        var root = Path.Combine(output, "ffb-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new GuidedWorkflowStore(Path.Combine(root, "workflow"));
        store.SavePreferences(new() { Completed = true, FocusChoiceConfirmed = true, SimHub = "Yes", Azom = "Yes", WantLiveConnection = true });
        var settings = new AppSettingsStore();
        foreach (var (field, value) in new[] { ("_directory", root), ("_path", Path.Combine(root, "settings.json")), ("_backupPath", Path.Combine(root, "settings.backup.json")) })
            typeof(AppSettingsStore).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, value);
        settings.Save(settings.Load());
        var wizard = new SetupWizardWindow(false, store, settings);
        try
        {
            var provider = (ComboBox)wizard.FindName("FfbProviderBox");
            Check(provider.Items.Count == 3 && ((FfbProviderOptions.Option)provider.SelectedItem).Provider == FfbProvider.SimHubAzom, "Existing AZOM choice changed");
            provider.SelectedItem = FfbProviderOptions.All.Single(x => x.Provider == FfbProvider.MozaPitHouse);
            Check(((StackPanel)wizard.FindName("PitHouseInterviewPanel")).Visibility == Visibility.Visible, "Pit House panel hidden");
            Check(((StackPanel)wizard.FindName("SimHubInterviewPanel")).Visibility == Visibility.Collapsed && ((Border)wizard.FindName("OptionalSimHubCard")).Visibility == Visibility.Collapsed, "Pit House still requires AZOM setup");
            Check(((Button)wizard.FindName("TestEverythingButton")).Content.Equals("Check AC Paths"), "Pit House check calls AZOM");
            var p = (GuidedPreferences)wizard.GetType().GetMethod("InterviewPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, null)!;
            Check(p.FfbProvider == FfbProvider.MozaPitHouse && p.SimHub == "Yes" && p.WantLiveConnection, "Switch erased previous settings");
            provider.SelectedItem = FfbProviderOptions.All.Single(x => x.Provider == FfbProvider.Manual);
            Check(((StackPanel)wizard.FindName("PitHouseInterviewPanel")).Visibility == Visibility.Collapsed, "Manual exposes SDK");
            Check(((TextBlock)wizard.FindName("InterviewInstructionsText")).Text.Contains("Manual FFB"), "Manual instructions wrong");
            provider.SelectedItem = FfbProviderOptions.All.Single(x => x.Provider == FfbProvider.SimHubAzom);
            Check(((Border)wizard.FindName("OptionalSimHubCard")).Visibility == Visibility.Visible, "Switch back lost AZOM help");
        }
        finally
        {
            wizard.GetType().GetField("_closingAfterSave", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wizard, true);
            wizard.Close();
        }
        foreach (var provider in new[] { FfbProvider.MozaPitHouse, FfbProvider.Manual })
        {
            var p = store.Preferences(); p.FfbProvider = provider; store.SavePreferences(p);
            var window = new PitHouseSettingsWindow(new TuneInput(), new TuneResult(), store);
            try
            {
                Check(!((Button)window.FindName("ApplyButton")).IsEnabled, "Opening view enables writes before a read");
                Check(((StackPanel)window.FindName("SdkPanel")).Visibility == (provider == FfbProvider.MozaPitHouse ? Visibility.Visible : Visibility.Collapsed), "Manual SDK visibility wrong");
                Check(((ItemsControl)window.FindName("SettingsRows")).Items.Count == 10, "Missing supported control rows");
                Check(((ItemsControl)window.FindName("SettingsRows")).Items.Cast<PitHouseSettingsWindow.SettingRow>().All(x => !x.CanSelect && !x.Selected), "Unread values can be applied");
                Check(((TextBlock)window.FindName("UnsupportedText")).Text.Contains("not mapped"), "Unsupported controls not explained");
                var content = (FrameworkElement)window.Content;
                Layout(content, new Size(900, 740));
                Render(content, new Size(900, 740), Path.Combine(output, provider + "-Settings.png"));
            }
            finally { window.Close(); }
        }
        Progress($"PASS FFB provider UI: {checks} assertions; native SDK never loaded");
    }
}
