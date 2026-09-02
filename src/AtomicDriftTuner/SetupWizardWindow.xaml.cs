using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class SetupWizardWindow : Window
{
    private readonly bool _firstRun;
    private readonly AppSettingsStore _store = new();
    private readonly MachineConfigurationService _machine = new();
    private readonly BridgeManagerService _bridge = new();
    private AppSettings _settings;

    public bool SettingsChanged { get; private set; }

    public SetupWizardWindow(bool firstRun)
    {
        InitializeComponent();
        _firstRun = firstRun;
        _settings = _store.Load();

        IntroText.Text = firstRun
            ? "Welcome. Atomic will detect common locations, but every path can be changed. These machine paths stay local and are never stored inside shared tuning profiles."
            : "Review or change this computer's integration paths. These machine paths stay local and are never stored inside shared tuning profiles.";

        LoadDetectedValues();
    }

    private void LoadDetectedValues()
    {
        var detected = _machine.Detect(_settings);

        SimHubPathBox.Text = detected.SimHubRoot ?? _settings.SimHubRoot ?? "";
        AcRootBox.Text = detected.AssettoCorsaRoot ?? _settings.AssettoCorsaRoot ?? "";
        AcDocumentsBox.Text = detected.AssettoCorsaDocumentsRoot ?? _settings.AssettoCorsaDocumentsRoot ?? "";

        RefreshStatuses();
    }

    private void DetectAll_Click(object sender, RoutedEventArgs e) =>
        LoadDetectedValues();

    private void DetectSimHub_Click(object sender, RoutedEventArgs e)
    {
        var found =
            SimHubLocator.FindSimHubRoot(
                SimHubPathBox.Text);

        if (!string.IsNullOrWhiteSpace(found))
            SimHubPathBox.Text = found;

        RefreshStatuses();
    }

    private void BrowseSimHub_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the SimHub folder containing SimHubWPF.exe"
        };

        if (Directory.Exists(SimHubPathBox.Text))
            dialog.InitialDirectory = SimHubPathBox.Text;

        if (dialog.ShowDialog() == true)
        {
            SimHubPathBox.Text = dialog.FolderName;
            RefreshStatuses();
        }
    }

    private void BrowseAc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the Assetto Corsa folder containing content\\cars"
        };

        if (Directory.Exists(AcRootBox.Text))
            dialog.InitialDirectory = AcRootBox.Text;

        if (dialog.ShowDialog() == true)
        {
            AcRootBox.Text = dialog.FolderName;
            RefreshStatuses();
        }
    }

    private void BrowseAcDocuments_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the Assetto Corsa user-data folder"
        };

        if (Directory.Exists(AcDocumentsBox.Text))
            dialog.InitialDirectory = AcDocumentsBox.Text;

        if (dialog.ShowDialog() == true)
        {
            AcDocumentsBox.Text = dialog.FolderName;
            RefreshStatuses();
        }
    }

    private async void TestEverything_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatuses();

        var lines = new List<string>();

        lines.Add(
            SimHubLocator.IsValidRoot(SimHubPathBox.Text)
                ? "✓ SimHub path is valid."
                : "✗ SimHub path is not valid.");

        lines.Add(
            _machine.ValidateAssettoCorsaRoot(AcRootBox.Text)
                ? "✓ Assetto Corsa install is valid."
                : "✗ Assetto Corsa install was not found.");

        lines.Add(
            _machine.ValidateAssettoCorsaDocumentsRoot(AcDocumentsBox.Text)
                ? "✓ Assetto Corsa user-data folder exists."
                : "• Assetto Corsa user-data folder is not present yet. It may be created after AC has been run.");

        try
        {
            var pipe =
                _settings.AzomLive?.PipeName ??
                "AtomicDriftTuner.AzomBridge.v1";

            var live =
                await new AzomBridgeClient(pipe)
                    .ReadSnapshotAsync(1400);

            lines.Add(
                $"✓ Live Atomic bridge connected (bridge {live.BridgeVersion}, AZOM readable={live.SettingsReadable}).");
        }
        catch (Exception ex)
        {
            lines.Add(
                "• Live bridge did not answer. This is expected if SimHub is closed or the bridge is not enabled. " +
                ex.Message);
        }

        OverallStatusText.Text =
            string.Join(
                Environment.NewLine,
                lines);
    }

    private void RefreshBridge_Click(object sender, RoutedEventArgs e) =>
        RefreshStatuses();

    private void InstallBridge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = SimHubPathBox.Text.Trim();
            var status = _bridge.GetStatus(root);

            if (!status.SimHubValid)
                throw new InvalidOperationException(
                    "Choose a valid SimHub folder first.");

            if (status.SimHubRunning)
                throw new InvalidOperationException(
                    "Fully exit SimHub, including the tray process, then click Install / Repair again.");

            if (!status.PackagedBridgeAvailable)
                throw new InvalidOperationException(
                    "This is a developer/source run and does not contain a packaged bridge payload. " +
                    "Beta installer/portable builds created by distribution\\build-beta-package.ps1 include the payload.");

            try
            {
                _bridge.InstallOrRepair(root);

                MessageBox.Show(
                    "Bridge installed/repaired. Start SimHub, enable the Atomic Drift Tuner Bridge plugin if needed, and restart SimHub once if prompted.",
                    "Atomic Bridge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                var answer =
                    MessageBox.Show(
                        "Windows requires administrator permission to write into this SimHub folder.\n\n" +
                        "Atomic can relaunch only the bridge-install operation with UAC elevation. Continue?",
                        "Administrator Permission Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                    _bridge.LaunchElevatedInstall(root);
            }

            RefreshStatuses();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Bridge Install / Repair",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _machine.ApplyToSettings(
            _settings,
            SimHubPathBox.Text,
            AcRootBox.Text,
            AcDocumentsBox.Text,
            markFirstRunComplete: true);

        _store.Save(_settings);
        SettingsChanged = true;
        DialogResult = true;
        Close();
    }

    private void RefreshStatuses()
    {
        bool simHub =
            SimHubLocator.IsValidRoot(
                SimHubPathBox.Text);

        bool ac =
            _machine.ValidateAssettoCorsaRoot(
                AcRootBox.Text);

        bool docs =
            _machine.ValidateAssettoCorsaDocumentsRoot(
                AcDocumentsBox.Text);

        SimHubStatusText.Text =
            simHub
                ? "✓ SimHubWPF.exe and SimHub.Plugins.dll found."
                : "✗ SimHub folder is not valid.";

        AcStatusText.Text =
            ac
                ? "✓ content\\cars found."
                : "✗ content\\cars was not found under this folder.";

        AcDocumentsStatusText.Text =
            docs
                ? "✓ Assetto Corsa user-data folder exists."
                : "• Folder does not currently exist. You can still save this expected location.";

        var bridge =
            _bridge.GetStatus(
                SimHubPathBox.Text);

        BridgeStatusText.Text =
            $"Installed bridge: {(bridge.BridgeInstalled ? bridge.InstalledVersion : "missing")} • " +
            $"Packaged payload: {(bridge.PackagedBridgeAvailable ? bridge.PackagedVersion : "not present in this run")} • " +
            $"SimHub running: {(bridge.SimHubRunning ? "yes" : "no")}.";
    }
}
