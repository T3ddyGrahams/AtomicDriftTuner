using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class DiagnosticsWindow : Window
{
    private readonly SystemDiagnosticsService _service = new();
    private SystemDiagnosticsReport? _report;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            SummaryText.Text = "Checking this PC...";
            _report = await _service.CollectAsync();

            DiagnosticsGrid.ItemsSource = null;
            DiagnosticsGrid.ItemsSource = _report.Items;

            int issues =
                _report.Items.Count(
                    x => x.Status is
                        "NOT FOUND" or
                        "MISSING" or
                        "NOT CONNECTED" or
                        "NOT READABLE");

            SummaryText.Text =
                $"{DistributionInfo.DisplayVersion} • {_report.Items.Count} checks • " +
                (issues == 0
                    ? "no blocking integration issues detected."
                    : $"{issues} item(s) may need attention.");
        }
        catch (Exception ex)
        {
            SummaryText.Text = ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null)
            return;

        Clipboard.SetText(
            _service.ToPlainText(
                _report));

        SummaryText.Text =
            "Diagnostics copied to the clipboard.";
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Atomic support package (*.zip)|*.zip",
                FileName =
                    $"AtomicSupport_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };

            if (dialog.ShowDialog() != true)
                return;

            var path =
                await _service.ExportSupportPackageAsync(
                    dialog.FileName);

            MessageBox.Show(
                "Support package created:\n\n" +
                path +
                "\n\nThe package contains redacted diagnostics/settings and Atomic log files. It does not include telemetry sessions, saved tunes, AC setup files, or per-car behavior profiles.",
                "Support Package",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Support Package",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
