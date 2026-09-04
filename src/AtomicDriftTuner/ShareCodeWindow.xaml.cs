using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class ShareCodeWindow : Window
{
    private readonly ShareCodeService _service = new();
    private readonly Action<AtomicSharePayload, bool> _loadHandler;
    private readonly AtomicSharePayload? _createdPayload;
    private AtomicSharePayload? _decodedPayload;

    public ShareCodeWindow(
        AtomicSharePayload? currentPayload,
        Action<AtomicSharePayload, bool> loadHandler)
    {
        _createdPayload = currentPayload;
        _loadHandler = loadHandler;

        InitializeComponent();
        RenderCurrentPayload();
    }

    private void RenderCurrentPayload()
    {
        if (_createdPayload is null)
        {
            CreateSummaryText.Text = "No generated tune was supplied. Generate a tune in the main Atomic window, close this window, and open Share Codes again.";
            GeneratedCodeBox.Text = "";
            GeneratedStatsText.Text = "Share code unavailable.";
            return;
        }

        try
        {
            var code = _service.Encode(_createdPayload);
            GeneratedCodeBox.Text = code;
            CreateSummaryText.Text = _service.BuildPreview(_createdPayload);
            GeneratedStatsText.Text =
                $"{code.Length:N0} characters • {ShareCodeService.Schema} • no hardware write occurs when this code is created.";
        }
        catch (Exception ex)
        {
            CreateSummaryText.Text = "Could not create a share code: " + ex.Message;
            GeneratedCodeBox.Text = "";
            GeneratedStatsText.Text = "";
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GeneratedCodeBox.Text))
            return;

        try
        {
            Clipboard.SetText(GeneratedCodeBox.Text.Trim());
            GeneratedStatsText.Text = $"Copied {GeneratedCodeBox.Text.Trim().Length:N0}-character AT1 share code to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy Share Code", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_createdPayload is null)
            return;

        try
        {
            Clipboard.SetText(_service.BuildPreview(_createdPayload));
            GeneratedStatsText.Text = "Copied human-readable tune preview to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
                ImportCodeBox.Text = Clipboard.GetText();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Paste Share Code", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Decode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _decodedPayload = _service.Decode(ImportCodeBox.Text);
            PreviewBox.Text = _service.BuildPreview(_decodedPayload);
            LoadContextButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _decodedPayload = null;
            LoadContextButton.IsEnabled = false;
            PreviewBox.Text = "Invalid share code:\n\n" + ex.Message;
        }
    }

    private void LoadContext_Click(object sender, RoutedEventArgs e)
    {
        if (_decodedPayload is null)
            return;

        var answer = MessageBox.Show(
            "Atomic will load this shared hardware/wheel/pack/car/target context and regenerate a tune locally.\n\n" +
            "The shared AZOM snapshot is review-only and will NOT be applied directly.\n" +
            "Nothing will be written to the wheelbase by this import.\n\nContinue?",
            "Import Atomic Share Code",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _loadHandler(_decodedPayload, ImportBehaviorBox.IsChecked == true);
            MessageBox.Show(
                "Shared context loaded and tune regenerated locally.\n\nNo AZOM settings were applied.",
                "Atomic Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import Share Code", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
