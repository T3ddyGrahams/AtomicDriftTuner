using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckStartupSelection(string output)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Startup selection UI: " + message); checks++; }
        static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        static T Control<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
        static ComboBox Box(MainWindow window, string name) => Control<ComboBox>(window, name);
        static TextBox Number(MainWindow window, string name) => Control<TextBox>(window, name);
        static MainWindow Open(AppSettingsStore settings) => (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [settings, false], null)!;
        var root = Path.Combine(output, "startup-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppSettingsStore Store(string name)
        {
            var directory = Path.Combine(root, name);
            var store = new AppSettingsStore();
            Set(store, "_directory", directory);
            Set(store, "_path", Path.Combine(directory, "settings.json"));
            Set(store, "_backupPath", Path.Combine(directory, "settings.backup.json"));
            return store;
        }
        static void Close(MainWindow window)
        {
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        var selectors = new[] { "HardwareBox", "WheelBox", "PackBox", "CarBox", "IntentBox" };
        var numbers = SessionSelection.HardwareFields.Concat(SessionSelection.WheelFields).Concat(SessionSelection.CarFields).ToArray();
        void CheckBlank(MainWindow window, string label)
        {
            Check(selectors.All(name => Box(window, name).SelectedIndex == -1), label + " inherited a wheelbase, rim, pack, car or target");
            Check(numbers.All(name => Number(window, name).Text.Length == 0), label + " inherited editable hardware/car values");
            Check(!Control<Button>(window, "DashboardGenerateButton").IsEnabled && !Box(window, "CarBox").IsEnabled, label + " enabled generation or a car choice before a pack");
            Check(Get(window, "_lastResult") is null, label + " generated a tune automatically");
        }

        var freshStore = Store("fresh");
        var fresh = Open(freshStore);
        try
        {
            CheckBlank(fresh, "Fresh account");
            Check(Control<TextBlock>(fresh, "CarSourceText").Text.Contains("Choose your car"), "fresh car instructions are missing");
            Check(!File.Exists((string)Get(freshStore, "_path")!), "opening a blank dashboard wrote settings");
            var workflow = new GuidedWorkflowStore(Path.Combine(root, "fresh-workflow"));
            Set(fresh, "_workflow", workflow); Set(fresh, "_guidedReady", true);
            Call(fresh, "RefreshGuidedWorkflow");
            Check(Control<Button>(fresh, "GuidedNextButton").IsEnabled && Equals(Control<Button>(fresh, "GuidedNextButton").Content, "Choose What to Tune"), "blank first launch cannot open the scope question");
            Check(Control<TextBlock>(fresh, "GuidedDetailsText").Visibility == Visibility.Collapsed, "fresh guidance defaulted to expanded detailed help");
            var content = (FrameworkElement)fresh.Content;
            Layout(content, new Size(680, 850));
            Render(content, new Size(680, 850), Path.Combine(output, "Startup-Blank-680.png"));
            workflow.SavePreferences(new GuidedPreferences { Completed = true, FocusChoiceConfirmed = true, Focus = TuningFocus.CarSetupOnly });
            Call(fresh, "RefreshGuidedWorkflow");
            Check(Equals(Control<Button>(fresh, "GuidedNextButton").Content, "Choose My Car & Hardware") && Control<Button>(fresh, "GuidedNextButton").IsEnabled, "confirmed car-only mode did not guide incomplete selectors");
            Check(Control<Border>(fresh, "GeneratedTuneCard").Visibility == Visibility.Collapsed && Control<Border>(fresh, "CalibrationCard").Visibility == Visibility.Collapsed, "car-only startup shows FFB-generation cards");
            Check(Control<TextBlock>(fresh, "GuidedComingNextText").Text.Length > 0 && Control<TextBlock>(fresh, "GuidedInstructionsText").Text.Contains("1."), "simple starting guidance lacks order or the following step");
            Layout(content, new Size(680, 850));
            Render(content, new Size(680, 850), Path.Combine(output, "Startup-CarOnly-Choose-680.png"));
            Set(fresh, "_guidedReady", false);
        }
        finally { Close(fresh); }
        Check(!File.Exists((string)Get(freshStore, "_path")!), "closing untouched blank startup persisted default selections");

        // Preview.3 settings have no LastSessionSelection. Preserve other settings and any
        // future fields while replacing the old automatic owner-specific UI defaults.
        var legacyStore = Store("legacy");
        Directory.CreateDirectory((string)Get(legacyStore, "_directory")!);
        File.WriteAllText((string)Get(legacyStore, "_path")!, """
            { "FirstRunCompleted": true, "AssettoCorsaRoot": "C:\\Isolated-AC", "AutoScanInstalledCars": false,
              "AutoSelectActiveCar": false, "Theme": { "Accent": "#DDAA33" }, "FuturePreference": { "Keep": 42 } }
            """);
        SessionSelection selected;
        var legacy = Open(legacyStore);
        try
        {
            CheckBlank(legacy, "Upgraded legacy account");
            Box(legacy, "HardwareBox").SelectedIndex = 0;
            Box(legacy, "WheelBox").SelectedIndex = 0;
            Box(legacy, "PackBox").SelectedIndex = 0;
            Check(Box(legacy, "CarBox").Items.Count > 0 && Box(legacy, "CarBox").SelectedIndex == -1, "choosing a pack selected its first car");
            Check(!Control<Button>(legacy, "DashboardGenerateButton").IsEnabled && Number(legacy, "CarMassBox").Text.Length == 0, "partial rig choice enabled generation or populated an unchosen car");
            Box(legacy, "CarBox").SelectedIndex = 0;
            Box(legacy, "IntentBox").SelectedIndex = 1;
            Check(Control<Button>(legacy, "DashboardGenerateButton").IsEnabled, "complete explicit choices did not enable generation");
            Number(legacy, "PeakTorqueBox").Text = "11.25";
            Number(legacy, "WheelDiameterBox").Text = "357";
            Number(legacy, "WheelInertiaBox").Text = "1.23";
            Number(legacy, "CarMassBox").Text = "1337";
            Number(legacy, "CarPowerBox").Text = "543.25";
            Box(legacy, "GripBox").SelectedItem = GripLevel.High;
            Call(legacy, "RememberSessionSelection");
            selected = legacyStore.Load().LastSessionSelection!;
            Check(selected.HardwareId == ((HardwareProfile)Box(legacy, "HardwareBox").SelectedItem).Id && selected.CarId == ((CarProfile)Box(legacy, "CarBox").SelectedItem).Id, "explicit rig/car choices were not saved");
            Check(selected.Numbers["WheelDiameterBox"] == 357 && selected.Numbers["CarPowerBox"] == 543.25 && selected.Grip == GripLevel.High, "custom values or grip were not saved");
            Check(Get(legacy, "_lastResult") is null, "saving selections generated a tune");
            var persisted = legacyStore.Load();
            Check(persisted.FirstRunCompleted && persisted.AssettoCorsaRoot == @"C:\Isolated-AC" && !persisted.AutoScanInstalledCars && !persisted.AutoSelectActiveCar && persisted.Theme.Accent == "#DDAA33", "remembering selections overwrote unrelated preferences");
            using var json = JsonDocument.Parse(File.ReadAllText((string)Get(legacyStore, "_path")!));
            Check(json.RootElement.GetProperty("FuturePreference").GetProperty("Keep").GetInt32() == 42, "remembering selections removed unknown settings");

            // A temporarily unfinished number must not replace its last valid saved value.
            Number(legacy, "WheelDiameterBox").Text = "-";
            Call(legacy, "SessionValue_LostFocus", Number(legacy, "WheelDiameterBox"), new RoutedEventArgs());
            Call(legacy, "RememberSessionSelection");
            Check(legacyStore.Load().LastSessionSelection!.Numbers["WheelDiameterBox"] == 357, "unfinished numeric edit destroyed the remembered value");
            Number(legacy, "WheelDiameterBox").Text = "357";
        }
        finally { Close(legacy); }
        var reopened = Open(legacyStore);
        try
        {
            Check(selectors.All(name => Box(reopened, name).SelectedItem is not null) && Control<Button>(reopened, "DashboardGenerateButton").IsEnabled, "restart did not restore the user's complete choices");
            Check(Number(reopened, "PeakTorqueBox").Text == "11.25" && Number(reopened, "WheelDiameterBox").Text == "357" && Number(reopened, "WheelInertiaBox").Text == "1.23" && Number(reopened, "CarMassBox").Text == "1337" && Number(reopened, "CarPowerBox").Text == "543.25", "restart lost custom hardware/car numbers");
            Check(Get(reopened, "_lastResult") is null && !((RemoteTuneContext)Get(Get(reopened, "_remoteServer")!, "_tune")!).HasGeneratedTune, "restoring selections claimed a generated tune");
            var remote = (RemoteServerService)Get(reopened, "_remoteServer")!;
            remote.UpdateTuneContext((TuneInput)Call(reopened, "BuildInput")!, new TuneResult());
            Set(reopened, "_lastResult", new TuneResult());
            Check(((RemoteTuneContext)Get(remote, "_tune")!).HasGeneratedTune, "remote clearing fixture did not contain a tune");
            Box(reopened, "PackBox").SelectedIndex = Box(reopened, "PackBox").SelectedIndex == 0 ? 1 : 0;
            Check(Box(reopened, "CarBox").SelectedIndex == -1 && Number(reopened, "CarMassBox").Text.Length == 0 && !Control<Button>(reopened, "DashboardGenerateButton").IsEnabled, "changing pack retained the former car");
            var remoteTune = (RemoteTuneContext)Get(remote, "_tune")!;
            Check(Get(remote, "_currentInput") is null && !remoteTune.HasGeneratedTune && remoteTune.Car.Length == 0 && remoteTune.RecommendedAzom is null && Get(reopened, "_lastResult") is null, "incomplete choice left the previous tune or car available remotely");
        }
        finally { Close(reopened); }

        var partialStore = Store("partial");
        var partial = Open(partialStore);
        try { Box(partial, "HardwareBox").SelectedIndex = 1; Number(partial, "PeakTorqueBox").Text = "8.75"; }
        finally { Close(partial); }
        var partialAgain = Open(partialStore);
        try
        {
            Check(Box(partialAgain, "HardwareBox").SelectedIndex == 1 && Number(partialAgain, "PeakTorqueBox").Text == "8.75" && selectors.Skip(1).All(name => Box(partialAgain, name).SelectedIndex == -1), "partial session did not survive closing without generation");
            Check(!Control<Button>(partialAgain, "DashboardGenerateButton").IsEnabled, "partial restart enabled generation");
        }
        finally { Close(partialAgain); }

        var missingStore = Store("missing");
        var missingSettings = missingStore.Load();
        missingSettings.LastSessionSelection = selected.Clean();
        missingSettings.LastSessionSelection.HardwareId = "removed-wheelbase";
        missingSettings.LastSessionSelection.CarId = "removed-car";
        missingStore.Save(missingSettings);
        var missing = Open(missingStore);
        try
        {
            Check(Box(missing, "HardwareBox").SelectedIndex == -1 && Box(missing, "CarBox").SelectedIndex == -1 && Number(missing, "PeakTorqueBox").Text.Length == 0 && Number(missing, "CarPowerBox").Text.Length == 0, "unavailable IDs fell back to somebody else's hardware or car");
            Check(Box(missing, "WheelBox").SelectedItem is not null && Box(missing, "IntentBox").SelectedItem is not null && !Control<Button>(missing, "DashboardGenerateButton").IsEnabled, "missing IDs discarded valid independent choices or enabled generation");
        }
        finally { Close(missing); }

        var installedStore = Store("installed");
        var installedSettings = installedStore.Load();
        installedSettings.LastSessionSelection = selected.Clean();
        installedSettings.LastSessionSelection.CarIsInstalled = true;
        installedSettings.LastSessionSelection.CarId = "isolated-installed-car";
        installedSettings.LastSessionSelection.CarFolder = "isolated_exact_car_folder";
        installedStore.Save(installedSettings);
        var installed = Open(installedStore);
        try
        {
            Check(Box(installed, "CarBox").SelectedIndex == -1 && Get(installed, "_pendingCarSelection") is SessionSelection && Control<TextBlock>(installed, "SessionSelectionStatusText").Text.Contains("waiting"), "installed car restored without verifying the scan");
            var scanned = new CarProfile { Id = "isolated-installed-car", PackId = selected.PackId!, DisplayName = "Isolated scanned car", IsInstalled = true, SourceFolderName = "wrong_folder", PowerHp = 400 };
            Set(installed, "_installedCars", new List<CarProfile> { scanned });
            Call(installed, "RestorePendingCarSelection");
            Check(Box(installed, "CarBox").SelectedIndex == -1 && Get(installed, "_pendingCarSelection") is not null, "same ID with a different folder was treated as the saved car");
            Number(installed, "PeakTorqueBox").Text = "10.5";
            Call(installed, "RememberSessionSelection");
            var waiting = installedStore.Load().LastSessionSelection!;
            Check(waiting.CarIsInstalled && waiting.CarId == scanned.Id && waiting.CarFolder == "isolated_exact_car_folder" && waiting.Numbers["CarPowerBox"] == 543.25 && waiting.Numbers["PeakTorqueBox"] == 10.5, "saving another field erased the pending installed car");
            scanned.SourceFolderName = "ISOLATED_EXACT_CAR_FOLDER";
            Call(installed, "RestorePendingCarSelection");
            Call(installed, "UpdateRemoteContextSafely");
            Check(ReferenceEquals(Box(installed, "CarBox").SelectedItem, scanned) && Get(installed, "_pendingCarSelection") is null && Control<Button>(installed, "DashboardGenerateButton").IsEnabled, "exact scanned ID and folder did not restore the saved car");
            Check(Number(installed, "CarPowerBox").Text == "543.25" && Number(installed, "CarMassBox").Text == "1337" && Equals(Box(installed, "GripBox").SelectedItem, GripLevel.High), "scanning restored base car numbers instead of saved custom values");
            Call(installed, "RememberSessionSelection");
            Check(installedStore.Load().LastSessionSelection!.CarIsInstalled && Get(installed, "_lastResult") is null, "scan lost installed identity or automatically generated a tune");
        }
        finally { Close(installed); }

        var other = Open(Store("another-account"));
        try { CheckBlank(other, "Independent settings directory"); }
        finally { Close(other); }
        var dirty = new SessionSelection
        {
            HardwareId = "invalid\nidentifier", WheelId = new string('x', 257), Intent = (DriftStyleKind)999, Grip = (GripLevel)999,
            Numbers = new() { ["CarPowerBox"] = double.NaN, ["WheelDiameterBox"] = 357, ["UnrelatedSetting"] = 77, ["CarMassBox"] = 100001 }
        };
        var clean = dirty.Clean();
        Check(clean.HardwareId is null && clean.WheelId is null && clean.Intent is null && clean.Grip is null && clean.Numbers.Count == 1 && clean.Numbers["WheelDiameterBox"] == 357, "invalid selection data passed sanitization");
        Check(dirty.Numbers.Count == 4 && dirty.HardwareId is not null, "cleaning selections mutated the saved source");
        dirty.Schema = "adt/future-selection/99";
        Check(dirty.Clean().Numbers.Count == 0 && dirty.Clean().HardwareId is null, "unknown selection schema inherited values");
        Progress($"PASS {checks} startup selection assertions with isolated settings, actual dashboard controls and scanned-car restoration.");
    }
}
