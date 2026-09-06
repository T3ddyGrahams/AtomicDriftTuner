using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class SetupWizardWindow : Window
{
    private const string DefaultBridgePipeName =
        "AtomicDriftTuner.AzomBridge.v1";

    private readonly bool _firstRun;

    private readonly AppSettingsStore _store =
        new();

    private readonly MachineConfigurationService _machine =
        new();

    private readonly BridgeManagerService _bridge =
        new();

    private readonly CancellationTokenSource _lifetimeCancellation =
        new();

    private AppSettings _settings;

    private bool _suppressPathEditTracking =
        true;

    private bool _pathsEdited;
    private bool _busy;
    private bool _closingAfterSave;
    private bool _closed;
    private bool _canInstallBridge;

    public bool SettingsChanged { get; private set; }

    public SetupWizardWindow(
        bool firstRun)
    {
        InitializeComponent();

        _firstRun =
            firstRun;

        _settings =
            _store.Load();

        ModeText.Text =
            firstRun
                ? "FIRST-RUN / MACHINE SETUP"
                : "MACHINE SETUP & PATHS";

        IntroText.Text =
            firstRun
                ? "Welcome. ADT will detect common locations, but every path can be changed. These machine-specific paths stay local and are not stored inside shared tuning profiles."
                : "Review or change this computer's integration paths. These machine-specific paths stay local and are not stored inside shared tuning profiles.";

        SaveButton.Content =
            firstRun
                ? "Save & Continue"
                : "Save Paths";

        CloseWithoutSavingButton.Content =
            firstRun
                ? "Not Now"
                : "Close Without Saving";

        Closing +=
            SetupWizardWindow_Closing;

        Closed +=
            SetupWizardWindow_Closed;

        LoadDetectedValues(
            initialLoad: true);

        _suppressPathEditTracking =
            false;

        _pathsEdited =
            false;

        UpdateControls();
    }

    private void LoadDetectedValues(
        bool initialLoad)
    {
        if (
            !initialLoad &&
            _pathsEdited)
        {
            var answer =
                Ask(
                    "Replace the current path fields with ADT's detected/saved locations?\n\n" +
                    "Any unsaved manual edits in these three path fields will be replaced.",
                    "Detect Machine Paths",
                    MessageBoxImage.Question);

            if (
                answer !=
                MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            var latest =
                _store.Load();

            var detected =
                _machine.Detect(
                    latest);

            _settings =
                latest;

            _suppressPathEditTracking =
                true;

            try
            {
                SimHubPathBox.Text =
                    FirstNonBlank(
                        detected.SimHubRoot,
                        latest.SimHubRoot);

                AcRootBox.Text =
                    FirstNonBlank(
                        detected.AssettoCorsaRoot,
                        latest.AssettoCorsaRoot);

                AcDocumentsBox.Text =
                    FirstNonBlank(
                        detected.AssettoCorsaDocumentsRoot,
                        latest.AssettoCorsaDocumentsRoot);
            }
            finally
            {
                _suppressPathEditTracking =
                    false;
            }

            _pathsEdited =
                false;

            RefreshStatuses();

            if (!initialLoad)
            {
                OverallStatusText.Text =
                    "Detection complete. Review the paths and use Test Everything if you want to verify live integrations before saving.";
            }
        }
        catch (Exception ex)
        {
            if (initialLoad)
            {
                _suppressPathEditTracking =
                    true;

                try
                {
                    SimHubPathBox.Text =
                        _settings.SimHubRoot ??
                        string.Empty;

                    AcRootBox.Text =
                        _settings.AssettoCorsaRoot ??
                        string.Empty;

                    AcDocumentsBox.Text =
                        _settings.AssettoCorsaDocumentsRoot ??
                        string.Empty;
                }
                finally
                {
                    _suppressPathEditTracking =
                        false;
                }

                RefreshStatusesBestEffort();

                OverallStatusText.Text =
                    "ADT could not complete automatic path detection. Saved paths were loaded where available. " +
                    ex.Message;

                return;
            }

            ShowMessage(
                "ADT could not auto-detect the machine paths.\n\n" +
                ex.Message,
                "Detect Machine Paths",
                MessageBoxImage.Warning);
        }
        finally
        {
            UpdateControls();
        }
    }

    private void DetectAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        LoadDetectedValues(
            initialLoad: false);
    }

    private void DetectSimHub_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            var found =
                SimHubLocator.FindSimHubRoot(
                    SimHubPathBox.Text);

            if (string.IsNullOrWhiteSpace(
                    found))
            {
                SimHubStatusText.Text =
                    "• ADT did not find a valid SimHub installation automatically. The current field was left unchanged.";

                RefreshBridgeStatusBestEffort();
                return;
            }

            SimHubPathBox.Text =
                found;

            RefreshStatuses();

            OverallStatusText.Text =
                "SimHub detection complete. Review the selected folder before saving.";
        }
        catch (Exception ex)
        {
            ShowMessage(
                "ADT could not auto-detect SimHub.\n\n" +
                ex.Message,
                "Detect SimHub",
                MessageBoxImage.Warning);
        }
    }

    private void BrowseSimHub_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var dialog =
            new OpenFolderDialog
            {
                Title =
                    "Choose the SimHub folder containing SimHubWPF.exe"
            };

        SetInitialDirectoryIfValid(
            dialog,
            SimHubPathBox.Text);

        if (
            ShowFolderDialog(
                dialog) ==
            true)
        {
            SimHubPathBox.Text =
                dialog.FolderName;

            RefreshStatuses();
        }
    }

    private void BrowseAc_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var dialog =
            new OpenFolderDialog
            {
                Title =
                    "Choose the Assetto Corsa folder containing content\\cars"
            };

        SetInitialDirectoryIfValid(
            dialog,
            AcRootBox.Text);

        if (
            ShowFolderDialog(
                dialog) ==
            true)
        {
            AcRootBox.Text =
                dialog.FolderName;

            RefreshStatuses();
        }
    }

    private void BrowseAcDocuments_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var dialog =
            new OpenFolderDialog
            {
                Title =
                    "Choose the Assetto Corsa user-data folder"
            };

        SetInitialDirectoryIfValid(
            dialog,
            AcDocumentsBox.Text);

        if (
            ShowFolderDialog(
                dialog) ==
            true)
        {
            AcDocumentsBox.Text =
                dialog.FolderName;

            RefreshStatuses();
        }
    }

    private async void TestEverything_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _busy ||
            _closed)
        {
            return;
        }

        SetBusy(
            true);

        var simHubPath =
            SimHubPathBox.Text;

        var acRoot =
            AcRootBox.Text;

        var acDocuments =
            AcDocumentsBox.Text;

        try
        {
            RefreshStatuses();

            OverallStatusText.Text =
                "Testing configured integrations...";

            var lines =
                new List<string>
                {
                    SimHubLocator.IsValidRoot(
                        simHubPath)
                        ? "✓ SimHub path is valid."
                        : "✗ SimHub path is not valid.",

                    _machine.ValidateAssettoCorsaRoot(
                        acRoot)
                        ? "✓ Assetto Corsa install is valid."
                        : "✗ Assetto Corsa install was not found.",

                    _machine.ValidateAssettoCorsaDocumentsRoot(
                        acDocuments)
                        ? "✓ Assetto Corsa user-data folder exists."
                        : "• Assetto Corsa user-data folder is not present yet. It may be created after AC has been run."
                };

            try
            {
                var latest =
                    _store.Load();

                var pipe =
                    latest.AzomLive?.PipeName;

                if (string.IsNullOrWhiteSpace(
                        pipe))
                {
                    pipe =
                        DefaultBridgePipeName;
                }

                var live =
                    await new AzomBridgeClient(
                            pipe)
                        .ReadSnapshotAsync(
                            1400,
                            _lifetimeCancellation.Token);

                if (_closed)
                {
                    return;
                }

                lines.Add(
                    live.SettingsReadable
                        ? $"✓ Live ADT bridge connected (bridge {live.BridgeVersion}); AZOM base settings are readable."
                        : $"• Live ADT bridge connected (bridge {live.BridgeVersion}), but AZOM base settings are not currently readable.");
            }
            catch (OperationCanceledException)
                when (
                    _closed ||
                    _lifetimeCancellation
                        .IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lines.Add(
                    "• Live bridge did not answer. This is expected if SimHub is closed, the ADT bridge is not enabled, or AZOM is unavailable. " +
                    ex.Message);
            }

            if (_closed)
            {
                return;
            }

            OverallStatusText.Text =
                string.Join(
                    Environment.NewLine,
                    lines);
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                OverallStatusText.Text =
                    "Integration test failed: " +
                    ex.Message;
            }
        }
        finally
        {
            if (!_closed)
            {
                SetBusy(
                    false);
            }
        }
    }

    private void RefreshBridge_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        RefreshStatuses();
    }

    private void InstallBridge_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _busy ||
            _closed)
        {
            return;
        }

        SetBusy(
            true);

        try
        {
            var root =
                SimHubPathBox.Text
                    .Trim()
                    .Trim('"');

            var status =
                _bridge.GetStatus(
                    root);

            if (!status.SimHubValid)
            {
                throw new InvalidOperationException(
                    "Choose a valid SimHub folder first.");
            }

            if (status.SimHubRunning)
            {
                throw new InvalidOperationException(
                    "Fully exit SimHub, including its tray process, before installing or repairing the ADT bridge.");
            }

            if (!status.PackagedBridgeAvailable)
            {
                throw new InvalidOperationException(
                    "This run does not contain a packaged ADT bridge payload. Distribution builds created by the beta packaging pipeline include the bridge payload.");
            }

            var installedText =
                status.BridgeInstalled
                    ? status.InstalledVersion
                    : "not currently installed";

            var packagedText =
                string.IsNullOrWhiteSpace(
                    status.PackagedVersion)
                    ? "unknown"
                    : status.PackagedVersion;

            var answer =
                Ask(
                    "Install / repair the packaged ADT SimHub Bridge?\n\n" +
                    $"SimHub folder:\n{root}\n\n" +
                    $"Installed bridge: {installedText}\n" +
                    $"Packaged bridge: {packagedText}\n\n" +
                    "This operation writes the packaged ADT bridge files into the selected SimHub installation. SimHub must remain fully closed while those files are changed.\n\n" +
                    "This setup operation does not apply AZOM wheelbase settings.\n\n" +
                    "Continue?",
                    "Install / Repair ADT Bridge",
                    MessageBoxImage.Warning);

            if (
                answer !=
                MessageBoxResult.Yes)
            {
                OverallStatusText.Text =
                    "Bridge install / repair canceled. No bridge operation was started.";

                return;
            }

            try
            {
                _bridge.InstallOrRepair(
                    root);

                var after =
                    _bridge.GetStatus(
                        root);

                if (!after.BridgeInstalled)
                {
                    throw new InvalidOperationException(
                        "The bridge install operation returned, but ADT could not verify an installed bridge afterward.");
                }

                ShowMessage(
                    "ADT detected the SimHub Bridge after the install/repair operation.\n\n" +
                    $"Installed bridge: {after.InstalledVersion}\n\n" +
                    "Start SimHub, enable the ADT bridge plugin if needed, and restart SimHub once if prompted.",
                    "ADT Bridge",
                    MessageBoxImage.Information);

                OverallStatusText.Text =
                    "ADT bridge install / repair returned successfully and an installed bridge was detected.";
            }
            catch (UnauthorizedAccessException)
            {
                var elevate =
                    Ask(
                        "Windows requires administrator permission to write into this SimHub folder.\n\n" +
                        "ADT can launch only the bridge-install operation with UAC elevation. The main ADT process itself is not being relaunched as administrator.\n\n" +
                        "Continue?",
                        "Administrator Permission Required",
                        MessageBoxImage.Question);

                if (
                    elevate !=
                    MessageBoxResult.Yes)
                {
                    OverallStatusText.Text =
                        "Bridge install / repair was not elevated and no elevated operation was started.";

                    return;
                }

                var elevatedLaunched =
                    _bridge.LaunchElevatedInstall(
                        root);

                if (!elevatedLaunched)
                {
                    OverallStatusText.Text =
                        "The administrator prompt was canceled or the elevated bridge-install process did not start. No elevated bridge operation was launched.";

                    return;
                }

                OverallStatusText.Text =
                    "The elevated bridge-install operation was launched. Complete the UAC/elevated operation, then click Refresh Bridge Status to verify the result.";
            }
        }
        catch (Exception ex)
        {
            ShowMessage(
                ex.Message,
                "Bridge Install / Repair",
                MessageBoxImage.Warning);
        }
        finally
        {
            RefreshStatusesBestEffort();

            if (!_closed)
            {
                SetBusy(
                    false);
            }
        }
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _busy ||
            _closed)
        {
            return;
        }

        try
        {
            var unresolved =
                BuildUnresolvedPathMessages();

            if (unresolved.Count > 0)
            {
                var answer =
                    Ask(
                        "Some integrations are not currently verified:\n\n" +
                        string.Join(
                            Environment.NewLine,
                            unresolved.Select(
                                item =>
                                    "• " +
                                    item)) +
                        "\n\nADT can still continue, and these paths can be changed later. Save these values anyway?",
                        "Save Unverified Paths?",
                        MessageBoxImage.Question);

                if (
                    answer !=
                    MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // Always start from the latest persisted AppSettings. The Setup
            // workspace may stay cached for a long time, so saving paths must not
            // overwrite unrelated settings changed elsewhere while it was open.
            var candidate =
                _store.Load();

            _machine.ApplyToSettings(
                candidate,
                SimHubPathBox.Text,
                AcRootBox.Text,
                AcDocumentsBox.Text,
                markFirstRunComplete:
                    true);

            _store.Save(
                candidate);

            _settings =
                candidate;

            SettingsChanged =
                true;

            _pathsEdited =
                false;

            _closingAfterSave =
                true;

            if (_firstRun)
            {
                DialogResult =
                    true;

                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            _closingAfterSave =
                false;

            ShowMessage(
                "ADT could not save the machine paths.\n\n" +
                ex.Message,
                "Save Setup & Paths",
                MessageBoxImage.Warning);
        }
    }

    private void CloseWithoutSaving_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        Close();
    }

    private void PathBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (
            _suppressPathEditTracking ||
            _closed)
        {
            return;
        }

        _pathsEdited =
            true;

        RefreshPathStatusesOnly();

        OverallStatusText.Text =
            "Path fields have unsaved changes. Test Everything is optional; save when the values look correct.";
    }

    private void RefreshStatuses()
    {
        RefreshPathStatusesOnly();
        RefreshBridgeStatusBestEffort();
        UpdateControls();
    }

    private void RefreshStatusesBestEffort()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            RefreshStatuses();
        }
        catch
        {
            // Setup must stay usable even if one status probe fails.
        }
    }

    private void RefreshPathStatusesOnly()
    {
        if (_closed)
        {
            return;
        }

        bool simHub;
        bool ac;
        bool docs;

        try
        {
            simHub =
                SimHubLocator.IsValidRoot(
                    SimHubPathBox.Text);
        }
        catch
        {
            simHub =
                false;
        }

        try
        {
            ac =
                _machine.ValidateAssettoCorsaRoot(
                    AcRootBox.Text);
        }
        catch
        {
            ac =
                false;
        }

        try
        {
            docs =
                _machine.ValidateAssettoCorsaDocumentsRoot(
                    AcDocumentsBox.Text);
        }
        catch
        {
            docs =
                false;
        }

        SimHubStatusText.Text =
            simHub
                ? "✓ SimHubWPF.exe and SimHub.Plugins.dll found."
                : string.IsNullOrWhiteSpace(
                    SimHubPathBox.Text)
                    ? "• SimHub is not configured."
                    : "✗ SimHub folder is not valid.";

        AcStatusText.Text =
            ac
                ? "✓ content\\cars found."
                : string.IsNullOrWhiteSpace(
                    AcRootBox.Text)
                    ? "• Assetto Corsa install is not configured."
                    : "✗ content\\cars was not found under this folder.";

        AcDocumentsStatusText.Text =
            docs
                ? "✓ Assetto Corsa user-data folder exists."
                : string.IsNullOrWhiteSpace(
                    AcDocumentsBox.Text)
                    ? "• Assetto Corsa user-data folder is not configured."
                    : "• Folder does not currently exist. You can still save an expected location if AC has not created it yet.";
    }

    private void RefreshBridgeStatusBestEffort()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            var bridge =
                _bridge.GetStatus(
                    SimHubPathBox.Text);

            BridgeStatusText.Text =
                $"Installed bridge: {(bridge.BridgeInstalled ? bridge.InstalledVersion : "missing")} • " +
                $"Packaged payload: {(bridge.PackagedBridgeAvailable ? bridge.PackagedVersion : "not present in this run")} • " +
                $"SimHub running: {(bridge.SimHubRunning ? "yes" : "no")}.";

            _canInstallBridge =
                bridge.SimHubValid &&
                bridge.PackagedBridgeAvailable &&
                !bridge.SimHubRunning;
        }
        catch (Exception ex)
        {
            BridgeStatusText.Text =
                "Bridge status unavailable: " +
                ex.Message;

            _canInstallBridge =
                false;
        }

        UpdateControls();
    }

    private List<string> BuildUnresolvedPathMessages()
    {
        var messages =
            new List<string>();

        if (
            !string.IsNullOrWhiteSpace(
                SimHubPathBox.Text) &&
            !SafeIsValidSimHubRoot(
                SimHubPathBox.Text))
        {
            messages.Add(
                "The SimHub folder is not currently valid.");
        }
        else if (
            string.IsNullOrWhiteSpace(
                SimHubPathBox.Text))
        {
            messages.Add(
                "SimHub is not configured.");
        }

        if (
            !string.IsNullOrWhiteSpace(
                AcRootBox.Text) &&
            !SafeValidateAcRoot(
                AcRootBox.Text))
        {
            messages.Add(
                "The Assetto Corsa install folder is not currently valid.");
        }
        else if (
            string.IsNullOrWhiteSpace(
                AcRootBox.Text))
        {
            messages.Add(
                "Assetto Corsa is not configured.");
        }

        if (
            !string.IsNullOrWhiteSpace(
                AcDocumentsBox.Text) &&
            !SafeValidateAcDocumentsRoot(
                AcDocumentsBox.Text))
        {
            messages.Add(
                "The Assetto Corsa user-data folder does not currently exist; this can be normal before AC creates it.");
        }
        else if (
            string.IsNullOrWhiteSpace(
                AcDocumentsBox.Text))
        {
            messages.Add(
                "The Assetto Corsa user-data folder is not configured.");
        }

        return messages;
    }

    private bool SafeIsValidSimHubRoot(
        string path)
    {
        try
        {
            return
                SimHubLocator.IsValidRoot(
                    path);
        }
        catch
        {
            return false;
        }
    }

    private bool SafeValidateAcRoot(
        string path)
    {
        try
        {
            return
                _machine.ValidateAssettoCorsaRoot(
                    path);
        }
        catch
        {
            return false;
        }
    }

    private bool SafeValidateAcDocumentsRoot(
        string path)
    {
        try
        {
            return
                _machine.ValidateAssettoCorsaDocumentsRoot(
                    path);
        }
        catch
        {
            return false;
        }
    }

    private void SetBusy(
        bool busy)
    {
        _busy =
            busy;

        UpdateControls();
    }

    private void UpdateControls()
    {
        if (_closed)
        {
            return;
        }

        SimHubPathBox.IsEnabled =
            !_busy;

        AcRootBox.IsEnabled =
            !_busy;

        AcDocumentsBox.IsEnabled =
            !_busy;

        BrowseSimHubButton.IsEnabled =
            !_busy;

        BrowseAcButton.IsEnabled =
            !_busy;

        BrowseAcDocumentsButton.IsEnabled =
            !_busy;

        DetectSimHubButton.IsEnabled =
            !_busy;

        DetectAllButton.IsEnabled =
            !_busy;

        TestEverythingButton.IsEnabled =
            !_busy;

        RefreshBridgeButton.IsEnabled =
            !_busy;

        InstallBridgeButton.IsEnabled =
            !_busy &&
            _canInstallBridge;

        SaveButton.IsEnabled =
            !_busy;

        CloseWithoutSavingButton.IsEnabled =
            !_busy;
    }

    private static string FirstNonBlank(
        string? preferred,
        string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(
                preferred))
        {
            return
                preferred.Trim();
        }

        return
            fallback?.Trim() ??
            string.Empty;
    }

    private static void SetInitialDirectoryIfValid(
        OpenFolderDialog dialog,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return;
        }

        try
        {
            var path =
                value
                    .Trim()
                    .Trim('"');

            if (Directory.Exists(
                    path))
            {
                dialog.InitialDirectory =
                    path;
            }
        }
        catch
        {
            // A malformed current field should not prevent browsing.
        }
    }

    private bool? ShowFolderDialog(
        OpenFolderDialog dialog)
    {
        var owner =
            ResolveVisibleOwner();

        return
            owner is null
                ? dialog.ShowDialog()
                : dialog.ShowDialog(
                    owner);
    }

    private Window? ResolveVisibleOwner()
    {
        if (IsVisible)
        {
            return this;
        }

        if (
            Owner is Window owner &&
            owner.IsVisible)
        {
            return owner;
        }

        var main =
            Application.Current?.MainWindow;

        return
            main is not null &&
            main.IsVisible
                ? main
                : null;
    }

    private MessageBoxResult Ask(
        string message,
        string title,
        MessageBoxImage image)
    {
        var owner =
            ResolveVisibleOwner();

        return
            owner is null
                ? MessageBox.Show(
                    message,
                    title,
                    MessageBoxButton.YesNo,
                    image)
                : MessageBox.Show(
                    owner,
                    message,
                    title,
                    MessageBoxButton.YesNo,
                    image);
    }

    private void ShowMessage(
        string message,
        string title,
        MessageBoxImage image)
    {
        var owner =
            ResolveVisibleOwner();

        if (owner is null)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                image);

            return;
        }

        MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButton.OK,
            image);
    }

    private void SetupWizardWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (
            _closingAfterSave ||
            _closed)
        {
            return;
        }

        if (_busy)
        {
            e.Cancel =
                true;

            return;
        }

        if (_firstRun)
        {
            var answer =
                Ask(
                    "Leave first-run setup without saving?\n\n" +
                    "ADT will leave first-run setup incomplete and ask again the next time it starts.",
                    "Leave Setup?",
                    MessageBoxImage.Question);

            if (
                answer !=
                MessageBoxResult.Yes)
            {
                e.Cancel =
                    true;
            }

            return;
        }

        if (!_pathsEdited)
        {
            return;
        }

        var discard =
            Ask(
                "Close Setup & Paths without saving the path changes made in this workspace?",
                "Discard Unsaved Path Changes?",
                MessageBoxImage.Question);

        if (
            discard !=
            MessageBoxResult.Yes)
        {
            e.Cancel =
                true;
        }
    }

    private void SetupWizardWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed =
            true;

        Closing -=
            SetupWizardWindow_Closing;

        Closed -=
            SetupWizardWindow_Closed;

        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Defensive only.
        }

        _lifetimeCancellation.Dispose();
    }
}
