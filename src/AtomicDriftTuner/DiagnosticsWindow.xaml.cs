using System.Threading;
using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class DiagnosticsWindow : Window
{
    private readonly SystemDiagnosticsService _service =
        new();

    private readonly SemaphoreSlim _operationGate =
        new(
            1,
            1);

    private SystemDiagnosticsReport? _report;

    private bool _busy;
    private bool _closed;

    public DiagnosticsWindow()
    {
        InitializeComponent();

        Closed +=
            DiagnosticsWindow_Closed;

        // MainWindow embeds this Window's visual content without showing the
        // backing Window, so Window.Loaded is not a reliable initialization
        // hook for ADT workspaces. Start the first diagnostics pass directly.
        _ =
            RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (
            _closed ||
            !TryBeginOperation())
        {
            return;
        }

        try
        {
            SetBusy(
                true,
                "Checking this PC...");

            var report =
                await _service.CollectAsync();

            if (_closed)
            {
                return;
            }

            _report =
                report;

            DiagnosticsGrid.ItemsSource =
                null;

            DiagnosticsGrid.ItemsSource =
                report.Items;

            var issues =
                report.Items.Count(
                    item =>
                        item.Status is
                            "NOT FOUND" or
                            "MISSING" or
                            "NOT CONNECTED" or
                            "NOT READABLE");

            SummaryText.Text =
                $"{DistributionInfo.DisplayVersion} • {report.Items.Count} checks • " +
                (
                    issues == 0
                        ? "no blocking integration issues detected."
                        : $"{issues} item(s) may need attention."
                );
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                SummaryText.Text =
                    "Diagnostics refresh failed: " +
                    ex.Message;
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void Copy_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _closed ||
            _busy ||
            _report is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(
                _service.ToPlainText(
                    _report));

            SummaryText.Text =
                "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Copy Diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Export_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _closed ||
            _busy)
        {
            return;
        }

        var consent =
            MessageBox.Show(
                "Create an ADT support package on this PC?\n\n" +
                "The package may contain redacted diagnostics/settings and ADT log files. " +
                "It is not uploaded automatically. Review the ZIP before sharing it with anyone.\n\n" +
                "Continue?",
                "Export ADT Support Package",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

        if (
            consent !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var dialog =
            new SaveFileDialog
            {
                Filter =
                    "ADT support package (*.zip)|*.zip",

                FileName =
                    $"ADTSupport_{DateTime.Now:yyyyMMdd_HHmmss}.zip",

                AddExtension =
                    true,

                DefaultExt =
                    ".zip"
            };

        if (
            dialog.ShowDialog() !=
            true)
        {
            return;
        }

        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            SetBusy(
                true,
                "Creating local ADT support package...");

            var path =
                await _service.ExportSupportPackageAsync(
                    dialog.FileName);

            if (_closed)
            {
                return;
            }

            MessageBox.Show(
                "Support package created:\n\n" +
                path +
                "\n\nADT does not upload this file automatically. Review the ZIP contents before sharing it.",
                "ADT Support Package",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SummaryText.Text =
                "Support package created successfully.";
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                MessageBox.Show(
                    ex.Message,
                    "ADT Support Package",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private bool TryBeginOperation()
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

        UpdateButtons();

        return true;
    }

    private void SetBusy(
        bool busy,
        string? status = null)
    {
        _busy =
            busy;

        if (
            !_closed &&
            !string.IsNullOrWhiteSpace(
                status))
        {
            SummaryText.Text =
                status;
        }

        UpdateButtons();
    }

    private void EndOperation()
    {
        if (!_busy)
        {
            return;
        }

        _busy =
            false;

        try
        {
            _operationGate.Release();
        }
        catch (SemaphoreFullException)
        {
            // Defensive only; never allow UI cleanup to crash ADT.
        }

        if (!_closed)
        {
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        if (_closed)
        {
            return;
        }

        RefreshButton.IsEnabled =
            !_busy;

        CopyButton.IsEnabled =
            !_busy &&
            _report is not null;

        ExportButton.IsEnabled =
            !_busy;
    }

    private void DiagnosticsWindow_Closed(
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
            DiagnosticsWindow_Closed;
    }
}
