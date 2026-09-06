using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class ShareCodeWindow : Window
{
    private readonly ShareCodeService _service =
        new();

    private readonly Action<AtomicSharePayload, bool> _loadHandler;
    private readonly AtomicSharePayload? _createdPayload;

    private AtomicSharePayload? _decodedPayload;
    private string? _decodedSourceCode;
    private bool _suppressImportTextChanged;

    public ShareCodeWindow(
        AtomicSharePayload? currentPayload,
        Action<AtomicSharePayload, bool> loadHandler)
    {
        _createdPayload =
            currentPayload;

        _loadHandler =
            loadHandler ??
            throw new ArgumentNullException(
                nameof(loadHandler));

        InitializeComponent();
        RenderCurrentPayload();
        InvalidateDecodedImport(
            clearPreview: true);
    }

    private void RenderCurrentPayload()
    {
        if (_createdPayload is null)
        {
            CreateSummaryText.Text =
                "No generated tune was supplied. Generate a tune on the ADT Dashboard, close this workspace, and open Share Codes again.";

            GeneratedCodeBox.Text =
                string.Empty;

            GeneratedStatsText.Text =
                "Share code unavailable.";

            CopyCodeButton.IsEnabled =
                false;

            CopyPreviewButton.IsEnabled =
                false;

            return;
        }

        try
        {
            var code =
                _service.Encode(
                    _createdPayload);

            var preview =
                _service.BuildPreview(
                    _createdPayload);

            GeneratedCodeBox.Text =
                code;

            CreateSummaryText.Text =
                preview;

            GeneratedStatsText.Text =
                $"{code.Length:N0} characters • {ShareCodeService.Schema} • creating or copying this code performs no hardware write.";

            CopyCodeButton.IsEnabled =
                !string.IsNullOrWhiteSpace(
                    code);

            CopyPreviewButton.IsEnabled =
                true;
        }
        catch (Exception ex)
        {
            CreateSummaryText.Text =
                "Could not create a share code: " +
                ex.Message;

            GeneratedCodeBox.Text =
                string.Empty;

            GeneratedStatsText.Text =
                string.Empty;

            CopyCodeButton.IsEnabled =
                false;

            CopyPreviewButton.IsEnabled =
                false;
        }
    }

    private void CopyCode_Click(
        object sender,
        RoutedEventArgs e)
    {
        var code =
            GeneratedCodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                code))
        {
            return;
        }

        try
        {
            Clipboard.SetText(
                code);

            GeneratedStatsText.Text =
                $"Copied {code.Length:N0}-character AT1 share code to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Copy Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CopyPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_createdPayload is null)
        {
            return;
        }

        try
        {
            var preview =
                _service.BuildPreview(
                    _createdPayload);

            Clipboard.SetText(
                preview);

            GeneratedStatsText.Text =
                "Copied human-readable tune preview to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Copy Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Paste_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                PreviewBox.Text =
                    "The clipboard does not contain text.";

                return;
            }

            var clipboardText =
                Clipboard.GetText();

            _suppressImportTextChanged =
                true;

            try
            {
                ImportCodeBox.Text =
                    clipboardText;
            }
            finally
            {
                _suppressImportTextChanged =
                    false;
            }

            InvalidateDecodedImport(
                clearPreview: true);

            PreviewBox.Text =
                "Share code pasted. Choose DECODE + REVIEW before loading it.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Paste Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ImportCodeBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressImportTextChanged)
        {
            return;
        }

        // A decoded payload is authoritative only for the exact text that was
        // reviewed. Any edit, paste, deletion, or replacement invalidates it.
        InvalidateDecodedImport(
            clearPreview: true);
    }

    private void Decode_Click(
        object sender,
        RoutedEventArgs e)
    {
        var source =
            ImportCodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                source))
        {
            InvalidateDecodedImport(
                clearPreview: true);

            PreviewBox.Text =
                "Paste an ADT share code first.";

            return;
        }

        try
        {
            var decoded =
                _service.Decode(
                    source);

            var preview =
                _service.BuildPreview(
                    decoded);

            _decodedPayload =
                decoded;

            _decodedSourceCode =
                source;

            PreviewBox.Text =
                preview;

            LoadContextButton.IsEnabled =
                true;
        }
        catch (Exception ex)
        {
            InvalidateDecodedImport(
                clearPreview: true);

            PreviewBox.Text =
                "Invalid share code:\n\n" +
                ex.Message;
        }
    }

    private void LoadContext_Click(
        object sender,
        RoutedEventArgs e)
    {
        var payload =
            _decodedPayload;

        var reviewedSource =
            _decodedSourceCode;

        var currentSource =
            ImportCodeBox.Text.Trim();

        if (
            payload is null ||
            string.IsNullOrWhiteSpace(
                reviewedSource) ||
            !string.Equals(
                reviewedSource,
                currentSource,
                StringComparison.Ordinal))
        {
            InvalidateDecodedImport(
                clearPreview: false);

            MessageBox.Show(
                "The share-code text changed after it was reviewed. Decode it again before loading the context.",
                "ADT Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var saveBehavior =
            ImportBehaviorBox.IsChecked ==
            true;

        var behaviorWarning =
            saveBehavior
                ? "\n\nDesired Behavior: the shared per-car behavior target WILL be saved locally for the imported car."
                : "\n\nDesired Behavior: the shared per-car behavior target will NOT be saved.";

        var answer =
            MessageBox.Show(
                "ADT will load this reviewed hardware/wheel/pack/car/Drift Target context and regenerate recommendations locally.\n\n" +
                "The shared recommendation/AZOM snapshot is review-only and will NOT be applied directly.\n" +
                "Nothing will be written to the wheelbase by this import." +
                behaviorWarning +
                "\n\nContinue?",
                "Import ADT Share Code",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            LoadContextButton.IsEnabled =
                false;

            _loadHandler(
                payload,
                saveBehavior);

            MessageBox.Show(
                saveBehavior
                    ? "Shared context loaded and tune regenerated locally.\n\nThe shared Desired Behavior was saved for the imported car. No AZOM settings were applied."
                    : "Shared context loaded and tune regenerated locally.\n\nNo Desired Behavior profile was saved and no AZOM settings were applied.",
                "ADT Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Import intentionally changes the authoritative MainWindow
            // context. Close this now-stale share workspace so the user returns
            // to the regenerated Dashboard instead of continuing to view the
            // payload that belonged to the pre-import context.
            Close();
        }
        catch (Exception ex)
        {
            // The caller owns the actual import transaction. Keep this exact
            // decoded payload available for review/retry when the callback
            // reports a failure.
            LoadContextButton.IsEnabled =
                true;

            MessageBox.Show(
                ex.Message,
                "Import Share Code",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void InvalidateDecodedImport(
        bool clearPreview)
    {
        _decodedPayload =
            null;

        _decodedSourceCode =
            null;

        LoadContextButton.IsEnabled =
            false;

        if (clearPreview)
        {
            PreviewBox.Text =
                string.Empty;
        }
    }
}
