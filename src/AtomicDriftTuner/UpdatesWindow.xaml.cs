using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class UpdatesWindow : Window
{
    private enum DownloadKind
    {
        Installer,
        PortableZip
    }

    private readonly UpdateService _service =
        new();

    private readonly SemaphoreSlim _operationGate =
        new(
            1,
            1);

    private AtomicReleaseInfo? _release;
    private AtomicReleaseAsset? _installer;
    private AtomicReleaseAsset? _portable;

    private CancellationTokenSource? _downloadCancellation;

    private bool _busy;
    private bool _downloading;
    private bool _closed;

    public UpdatesWindow()
    {
        InitializeComponent();

        CurrentVersionText.Text =
            $"Installed: {DistributionInfo.DisplayVersion} • {DistributionInfo.Channel}";

        Closed +=
            UpdatesWindow_Closed;

        UpdateControls();
    }

    private async void Check_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryBeginOperation(
                downloading: false))
        {
            return;
        }

        try
        {
            CheckStatusText.Text =
                "Checking official GitHub Releases...";

            ReleaseTitleText.Text =
                "Checking...";

            ReleaseMetaText.Text =
                string.Empty;

            ReleaseNotesBox.Text =
                string.Empty;

            ClearAssets();

            var includePrerelease =
                IncludePrereleaseBox.IsChecked ==
                true;

            var result =
                await _service.CheckAsync(
                    includePrerelease);

            if (_closed)
            {
                return;
            }

            _release =
                result.LatestRelease;

            CheckStatusText.Text =
                result.Message;

            if (_release is null)
            {
                ReleaseTitleText.Text =
                    "No matching release found";

                ReleaseMetaText.Text =
                    "Try enabling public beta / pre-release versions or open the official GitHub Releases page.";

                return;
            }

            ReleaseTitleText.Text =
                string.IsNullOrWhiteSpace(
                    _release.Name)
                    ? _release.TagName
                    : _release.Name;

            ReleaseMetaText.Text =
                $"Tag: {_release.TagName} • " +
                $"Published: {(_release.PublishedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "unknown")} • " +
                (
                    _release.Prerelease
                        ? "PRE-RELEASE / BETA"
                        : "STABLE"
                );

            ReleaseNotesBox.Text =
                string.IsNullOrWhiteSpace(
                    _release.Body)
                    ? "This GitHub release does not contain release notes."
                    : _release.Body;

            _installer =
                UpdateService.FindInstaller(
                    _release);

            _portable =
                UpdateService.FindPortable(
                    _release);

            RenderAssets();
        }
        catch (Exception ex)
        {
            if (_closed)
            {
                return;
            }

            ClearAssets();

            CheckStatusText.Text =
                "Update check failed: " +
                ex.Message;

            ReleaseTitleText.Text =
                "Update check failed";

            ReleaseMetaText.Text =
                "No update download was started by this window.";
        }
        finally
        {
            EndOperation();
        }
    }

    private void ClearAssets()
    {
        _release =
            null;

        _installer =
            null;

        _portable =
            null;

        InstallerNameText.Text =
            "Installer: not loaded";

        InstallerSizeText.Text =
            string.Empty;

        PortableNameText.Text =
            "Portable ZIP: not loaded";

        PortableSizeText.Text =
            string.Empty;

        HashText.Text =
            string.Empty;

        DownloadProgress.Value =
            0;

        UpdateControls();
    }

    private void RenderAssets()
    {
        if (_installer is null)
        {
            InstallerNameText.Text =
                "Installer: not attached to this release";

            InstallerSizeText.Text =
                string.Empty;
        }
        else
        {
            InstallerNameText.Text =
                _installer.Name;

            InstallerSizeText.Text =
                FormatSize(
                    _installer.SizeBytes);
        }

        if (_portable is null)
        {
            PortableNameText.Text =
                "Portable ZIP: not attached to this release";

            PortableSizeText.Text =
                string.Empty;
        }
        else
        {
            PortableNameText.Text =
                _portable.Name;

            PortableSizeText.Text =
                FormatSize(
                    _portable.SizeBytes);
        }

        UpdateControls();
    }

    private async void DownloadInstaller_Click(
        object sender,
        RoutedEventArgs e)
    {
        var asset =
            _installer;

        if (asset is null)
        {
            return;
        }

        await DownloadAsync(
            asset,
            DownloadKind.Installer);
    }

    private async void DownloadPortable_Click(
        object sender,
        RoutedEventArgs e)
    {
        var asset =
            _portable;

        if (asset is null)
        {
            return;
        }

        await DownloadAsync(
            asset,
            DownloadKind.PortableZip);
    }

    private async Task DownloadAsync(
        AtomicReleaseAsset asset,
        DownloadKind kind)
    {
        if (!TryBeginOperation(
                downloading: true))
        {
            return;
        }

        CancellationTokenSource? cancellation =
            null;

        try
        {
            var expectedExtension =
                kind ==
                DownloadKind.Installer
                    ? ".exe"
                    : ".zip";

            var safeName =
                BuildSafeSuggestedFileName(
                    asset.Name,
                    expectedExtension);

            if (
                !safeName.EndsWith(
                    expectedExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    kind ==
                    DownloadKind.Installer
                        ? "ADT refused the selected installer asset because its filename is not an .exe file."
                        : "ADT refused the selected portable asset because its filename is not a .zip file.");
            }

            var downloads =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    "Downloads");

            var dialog =
                new SaveFileDialog
                {
                    Title =
                        "Save ADT Update",

                    FileName =
                        safeName,

                    InitialDirectory =
                        Directory.Exists(
                            downloads)
                            ? downloads
                            : string.Empty,

                    Filter =
                        kind ==
                        DownloadKind.Installer
                            ? "Windows installer (*.exe)|*.exe"
                            : "ZIP archive (*.zip)|*.zip",

                    DefaultExt =
                        expectedExtension,

                    AddExtension =
                        true,

                    OverwritePrompt =
                        true
                };

            var owner =
                ResolveVisibleOwner();

            var accepted =
                owner is null
                    ? dialog.ShowDialog()
                    : dialog.ShowDialog(
                        owner);

            if (
                accepted !=
                true)
            {
                return;
            }

            if (
                !string.Equals(
                    Path.GetExtension(
                        dialog.FileName),
                    expectedExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    owner ??
                    this,
                    $"ADT requires this download to be saved as {expectedExtension}.",
                    "ADT Update Download",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            DownloadProgress.Value =
                0;

            HashText.Text =
                string.Empty;

            DownloadStatusText.Text =
                $"Downloading {asset.Name} from the selected GitHub release...";

            cancellation =
                new CancellationTokenSource();

            _downloadCancellation =
                cancellation;

            UpdateControls();

            var progress =
                new Progress<double>(
                    value =>
                    {
                        if (_closed)
                        {
                            return;
                        }

                        var safeValue =
                            double.IsFinite(
                                value)
                                ? Math.Clamp(
                                    value,
                                    0,
                                    100)
                                : 0;

                        DownloadProgress.Value =
                            safeValue;
                    });

            var result =
                await _service.DownloadAsync(
                    asset,
                    dialog.FileName,
                    progress,
                    cancellation.Token);

            if (_closed)
            {
                return;
            }

            DownloadProgress.Value =
                100;

            DownloadStatusText.Text =
                $"Download complete: {result.FilePath}\n" +
                "ADT did not launch or install the file.";

            HashText.Text =
                $"Local SHA-256: {result.Sha256}";

            ShowMessage(
                "Update file downloaded successfully.\n\n" +
                result.FilePath +
                "\n\nADT did not run it. Review the release/version/hash, close ADT, and then launch an installer manually if you choose to update.",
                "ADT Update Download",
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            if (!_closed)
            {
                DownloadStatusText.Text =
                    "Download canceled.";
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                DownloadStatusText.Text =
                    "Download failed: " +
                    ex.Message;

                ShowMessage(
                    ex.Message,
                    "ADT Update Download",
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (
                ReferenceEquals(
                    _downloadCancellation,
                    cancellation))
            {
                _downloadCancellation =
                    null;
            }

            cancellation?.Dispose();

            EndOperation();
        }
    }

    private void CancelDownload_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            !_downloading ||
            _downloadCancellation is null)
        {
            return;
        }

        CancelDownloadButton.IsEnabled =
            false;

        DownloadStatusText.Text =
            "Canceling download...";

        try
        {
            _downloadCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download completed while the cancel click was being handled.
        }
    }

    private bool TryBeginOperation(
        bool downloading)
    {
        if (
            _closed ||
            !_operationGate.Wait(
                0))
        {
            return false;
        }

        _busy =
            true;

        _downloading =
            downloading;

        UpdateControls();

        return true;
    }

    private void EndOperation()
    {
        if (!_busy)
        {
            return;
        }

        _busy =
            false;

        _downloading =
            false;

        try
        {
            _operationGate.Release();
        }
        catch (SemaphoreFullException)
        {
            // Defensive only; UI cleanup must not crash ADT.
        }

        if (!_closed)
        {
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        if (_closed)
        {
            return;
        }

        CheckButton.IsEnabled =
            !_busy;

        IncludePrereleaseBox.IsEnabled =
            !_busy;

        OpenReleasesButton.IsEnabled =
            !_busy;

        DownloadInstallerButton.IsEnabled =
            !_busy &&
            _installer is not null;

        DownloadPortableButton.IsEnabled =
            !_busy &&
            _portable is not null;

        OpenReleaseButton.IsEnabled =
            !_busy &&
            _release is not null;

        CancelDownloadButton.IsEnabled =
            _downloading &&
            _downloadCancellation is not null;
    }

    private void OpenReleases_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenOfficialGitHubUrl(
            UpdateService.ReleasesPageUrl);
    }

    private void OpenRelease_Click(
        object sender,
        RoutedEventArgs e)
    {
        var release =
            _release;

        if (release is null)
        {
            return;
        }

        OpenOfficialGitHubUrl(
            release.HtmlUrl);
    }

    private void OpenOfficialGitHubUrl(
        string url)
    {
        if (
            !TryCreateOfficialGitHubUri(
                url,
                out var uri))
        {
            ShowMessage(
                "ADT refused to open an update URL outside the official T3ddyGrahams/AtomicDriftTuner GitHub repository.",
                "ADT Updates",
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo(
                    uri.AbsoluteUri)
                {
                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            ShowMessage(
                "Windows could not open the GitHub release page.\n\n" +
                ex.Message,
                "ADT Updates",
                MessageBoxImage.Warning);
        }
    }

    private static bool TryCreateOfficialGitHubUri(
        string? value,
        out Uri uri)
    {
        uri =
            null!;

        if (
            string.IsNullOrWhiteSpace(
                value) ||
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var parsed) ||
            !string.Equals(
                parsed.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                parsed.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(
                parsed.UserInfo))
        {
            return false;
        }

        const string repositoryPath =
            "/T3ddyGrahams/AtomicDriftTuner";

        if (
            !(
                string.Equals(
                    parsed.AbsolutePath.TrimEnd('/'),
                    repositoryPath,
                    StringComparison.OrdinalIgnoreCase) ||
                parsed.AbsolutePath.StartsWith(
                    repositoryPath + "/",
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            return false;
        }

        uri =
            parsed;

        return true;
    }

    private static string BuildSafeSuggestedFileName(
        string? name,
        string expectedExtension)
    {
        var candidate =
            Path.GetFileName(
                (name ?? string.Empty)
                    .Trim());

        if (string.IsNullOrWhiteSpace(
                candidate))
        {
            candidate =
                "ADT-Update" +
                expectedExtension;
        }

        var invalid =
            Path.GetInvalidFileNameChars();

        candidate =
            new string(
                candidate
                    .Select(
                        character =>
                            invalid.Contains(
                                character)
                                ? '_'
                                : character)
                    .ToArray());

        return candidate;
    }

    private static string FormatSize(
        long bytes)
    {
        if (bytes <= 0)
        {
            return "Unknown size";
        }

        double size =
            bytes;

        string[] units =
        [
            "B",
            "KB",
            "MB",
            "GB"
        ];

        var unit =
            0;

        while (
            size >=
            1024 &&
            unit <
            units.Length -
            1)
        {
            size /=
                1024;

            unit++;
        }

        return
            $"{size:0.##} {units[unit]}";
    }

    private Window? ResolveVisibleOwner()
    {
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

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void UpdatesWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed =
            true;

        Closed -=
            UpdatesWindow_Closed;

        var cancellation =
            _downloadCancellation;

        _downloadCancellation =
            null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Download completion won the race with the close path.
        }
    }
}
