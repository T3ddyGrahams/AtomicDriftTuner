using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class UpdatesWindow : Window
{
    private readonly UpdateService _service = new();
    private AtomicReleaseInfo? _release;
    private AtomicReleaseAsset? _installer;
    private AtomicReleaseAsset? _portable;
    private CancellationTokenSource? _downloadCancellation;

    public UpdatesWindow()
    {
        InitializeComponent();
        CurrentVersionText.Text =
            $"Installed: {DistributionInfo.DisplayVersion} • {DistributionInfo.Channel}";
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetChecking(true);
            CheckStatusText.Text = "Checking official GitHub Releases...";
            ReleaseTitleText.Text = "Checking...";
            ReleaseMetaText.Text = "";
            ReleaseNotesBox.Text = "";
            ClearAssets();

            var result = await _service.CheckAsync(IncludePrereleaseBox.IsChecked == true);
            _release = result.LatestRelease;
            CheckStatusText.Text = result.Message;

            if (_release is null)
            {
                ReleaseTitleText.Text = "No matching release found";
                ReleaseMetaText.Text = "Try enabling public beta / pre-release versions or open GitHub Releases.";
                return;
            }

            ReleaseTitleText.Text = string.IsNullOrWhiteSpace(_release.Name)
                ? _release.TagName
                : _release.Name;

            ReleaseMetaText.Text =
                $"Tag: {_release.TagName} • " +
                $"Published: {(_release.PublishedAt?.ToLocalTime().ToString("g") ?? "unknown")} • " +
                (_release.Prerelease ? "PRE-RELEASE / BETA" : "STABLE");

            ReleaseNotesBox.Text = string.IsNullOrWhiteSpace(_release.Body)
                ? "This GitHub release does not contain release notes."
                : _release.Body;

            _installer = UpdateService.FindInstaller(_release);
            _portable = UpdateService.FindPortable(_release);
            RenderAssets();
            OpenReleaseButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            CheckStatusText.Text = ex.Message;
            ReleaseTitleText.Text = "Update check failed";
            ReleaseMetaText.Text = "Atomic did not download or change anything.";
        }
        finally
        {
            SetChecking(false);
        }
    }

    private void SetChecking(bool checking)
    {
        CheckButton.IsEnabled = !checking;
        IncludePrereleaseBox.IsEnabled = !checking;
    }

    private void ClearAssets()
    {
        _release = null;
        _installer = null;
        _portable = null;
        InstallerNameText.Text = "Installer: not loaded";
        InstallerSizeText.Text = "";
        PortableNameText.Text = "Portable ZIP: not loaded";
        PortableSizeText.Text = "";
        DownloadInstallerButton.IsEnabled = false;
        DownloadPortableButton.IsEnabled = false;
        OpenReleaseButton.IsEnabled = false;
    }

    private void RenderAssets()
    {
        if (_installer is null)
        {
            InstallerNameText.Text = "Installer: not attached to this release";
            InstallerSizeText.Text = "";
            DownloadInstallerButton.IsEnabled = false;
        }
        else
        {
            InstallerNameText.Text = _installer.Name;
            InstallerSizeText.Text = FormatSize(_installer.SizeBytes);
            DownloadInstallerButton.IsEnabled = true;
        }

        if (_portable is null)
        {
            PortableNameText.Text = "Portable ZIP: not attached to this release";
            PortableSizeText.Text = "";
            DownloadPortableButton.IsEnabled = false;
        }
        else
        {
            PortableNameText.Text = _portable.Name;
            PortableSizeText.Text = FormatSize(_portable.SizeBytes);
            DownloadPortableButton.IsEnabled = true;
        }
    }

    private async void DownloadInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (_installer is not null)
            await DownloadAsync(_installer);
    }

    private async void DownloadPortable_Click(object sender, RoutedEventArgs e)
    {
        if (_portable is not null)
            await DownloadAsync(_portable);
    }

    private async Task DownloadAsync(AtomicReleaseAsset asset)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        var dialog = new SaveFileDialog
        {
            Title = "Save Atomic Drift Tuner Update",
            FileName = asset.Name,
            InitialDirectory = Directory.Exists(downloads) ? downloads : "",
            Filter = asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? "Windows installer (*.exe)|*.exe|All files (*.*)|*.*"
                : "ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetDownloadControls(false);
            DownloadProgress.Value = 0;
            HashText.Text = "";
            DownloadStatusText.Text = $"Downloading {asset.Name} from official GitHub Releases...";

            _downloadCancellation?.Dispose();
            _downloadCancellation = new CancellationTokenSource();

            var progress = new Progress<double>(value => DownloadProgress.Value = value);
            var result = await _service.DownloadAsync(
                asset,
                dialog.FileName,
                progress,
                _downloadCancellation.Token);

            DownloadStatusText.Text =
                $"Download complete: {result.FilePath}\n" +
                "Atomic did not launch or install the file.";
            HashText.Text = $"SHA-256: {result.Sha256}";

            MessageBox.Show(
                "Update file downloaded successfully.\n\n" +
                result.FilePath +
                "\n\nAtomic did not run it. Close Atomic before launching an installer.",
                "Atomic Update Download",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Download canceled. Partial file removed.";
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Atomic Update Download",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetDownloadControls(true);
        }
    }

    private void SetDownloadControls(bool enabled)
    {
        CheckButton.IsEnabled = enabled;
        IncludePrereleaseBox.IsEnabled = enabled;
        DownloadInstallerButton.IsEnabled = enabled && _installer is not null;
        DownloadPortableButton.IsEnabled = enabled && _portable is not null;
        OpenReleaseButton.IsEnabled = enabled && _release is not null;
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(UpdateService.ReleasesPageUrl);

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_release is not null)
            OpenUrl(_release.HtmlUrl);
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Atomic refused to open a non-GitHub update URL.",
                "Updates",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "Unknown size";
        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        base.OnClosed(e);
    }
}
