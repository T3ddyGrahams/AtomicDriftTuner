using System.Diagnostics;
using System.Windows;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class RemoteControlWindow
{
    private void UpdateCompanionUi()
    {
        InstallCompanionButton.IsEnabled = !_busy && !_closed;
        OpenCompanionPackageButton.IsEnabled = !_busy && !_closed;
    }

    private void InstallCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        try
        {
            var result = new CompanionInstallationService().Install(new AppSettingsStore().Load().AssettoCorsaRoot);
            CompanionInstallText.Text = (result.ChangedFiles == 0 ? "ADT Companion is already up to date." : "ADT Companion installed/updated.") +
                "\n" + result.Folder +
                "\nStart a new AC driving session with Custom Shaders Patch enabled, then open ADT Companion from the in-game apps sidebar. Start ADT Remote below and pair with its code. Keep desktop ADT running." +
                (result.BackupFolder is null ? "" : "\nPrevious ADT files were backed up to: " + result.BackupFolder);
        }
        catch (Exception ex)
        {
            CompanionInstallText.Text = "Companion installation did not finish: " + ex.Message +
                "\nIf Windows denies access, use Open Companion Package to find the ZIP and install it through Content Manager. Other mods and car files are preserved.";
        }
    }

    private void OpenCompanionPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        try
        {
            var archive = Path.Combine(AppContext.BaseDirectory, CompanionInstallationService.ImportArchiveName);
            var folder = Path.Combine(AppContext.BaseDirectory, CompanionInstallationService.PackageDirectory);
            if (File.Exists(archive))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + archive + "\"") { UseShellExecute = true });
                CompanionInstallText.Text = "Drag ADTCompanion-ContentManager.zip into Content Manager and review its ADT Companion installation entry. Exit the current driving session first, then start a new session after installation. If CM cannot import it, follow CompanionPayload\\README.md for manual installation.";
            }
            else if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                CompanionInstallText.Text = "The companion package folder is open. See README.md for Content Manager and manual installation instructions.";
            }
            else throw new InvalidOperationException("Re-extract the full ADT portable ZIP or reinstall this ADT build to restore its companion files.");
        }
        catch (Exception ex) { CompanionInstallText.Text = "Could not open the companion package: " + ex.Message; }
    }
}
