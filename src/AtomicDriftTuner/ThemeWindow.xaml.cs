using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class ThemeWindow : Window
{
    private readonly AppSettingsStore _store;

    private ThemeSettings _original;
    private bool _loading;

    private sealed record ColorTarget(
        string Label,
        string TextBoxName)
    {
        public override string ToString() =>
            Label;
    }

    private List<ColorTarget> Targets { get; } =
    [
        new("Background", "BackgroundBox"),
        new("Sidebar / Surface", "SurfaceBox"),
        new("Panel", "PanelBox"),
        new("Alternate Panel", "PanelAltBox"),
        new("Border", "BorderBox"),
        new("Primary Text", "PrimaryTextBox"),
        new("Secondary Text", "SecondaryTextBox"),
        new("Muted Text", "MutedTextBox"),
        new("Accent", "AccentBox"),
        new("Accent Text", "AccentTextBox"),

        new("Input Field Background", "InputBox"),
        new("Input Field Text", "InputTextBox"),
        new("Input Field Border", "InputBorderBox"),

        new("Table Row Background", "GridBackgroundBox"),
        new("Table Alternate Row", "GridAlternateBox"),
        new("Table Cell Text", "GridTextBox"),
        new("Table Header Background", "GridHeaderBackgroundBox"),
        new("Table Header Text", "GridHeaderTextBox"),
        new("Table Selected Background", "GridSelectedBackgroundBox"),
        new("Table Selected Text", "GridSelectedTextBox"),
        new("Table Grid Lines", "GridLineBox"),

        new("Tab Background", "TabBackgroundBox"),
        new("Tab Text", "TabTextBox"),
        new("Active Tab Background", "TabSelectedBackgroundBox"),
        new("Active Tab Text", "TabSelectedTextBox"),
        new("Tab Border", "TabBorderBox"),

        new("Checkbox Label / Text", "CheckBoxTextBox"),
        new("Checkbox Box Background", "CheckBoxBackgroundBox"),
        new("Checkbox Box Border", "CheckBoxBorderBox"),
        new("Checkbox Check Mark", "CheckBoxCheckMarkBox"),

        new("Dropdown Closed Background", "ComboBackgroundBox"),
        new("Dropdown Closed Text", "ComboTextBox"),
        new("Dropdown Popup Background", "ComboDropBackgroundBox"),
        new("Dropdown Popup Text", "ComboDropTextBox"),
        new("Dropdown Highlight", "ComboHighlightBox"),
        new("Dropdown Highlight Text", "ComboHighlightTextBox"),
        new("Dropdown Border", "ComboBorderBox")
    ];

    public ThemeWindow() : this(new AppSettingsStore()) { }

    public ThemeWindow(AppSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
        BuildAdditionalColorEditors();

        var savedTheme =
            _store.Load().Theme;

        _original =
            ThemeCatalog.Clone(
                savedTheme);

        PresetBox.ItemsSource =
            ThemeCatalog.Presets
                .Select(
                    preset =>
                        preset.PresetName)
                .Concat(
                    new[]
                    {
                        "Custom"
                    })
                .ToList();

        ColorTargetBox.ItemsSource =
            Targets;

        LoadBoxes(
            _original);

        PreviewGrid.ItemsSource =
            new[]
            {
                new
                {
                    Group = "Core",
                    Setting = "Base Torque Output",
                    Live = "68%",
                    Target = "68%"
                },
                new
                {
                    Group = "Core",
                    Setting = "Maximum Wheel Speed",
                    Live = "135%",
                    Target = "142%"
                },
                new
                {
                    Group = "Effects",
                    Setting = "Wheel Damper",
                    Live = "8%",
                    Target = "7%"
                }
            };

        _loading =
            true;

        try
        {
            PresetBox.SelectedItem =
                ThemeCatalog.Presets.Any(
                    preset =>
                        string.Equals(
                            preset.PresetName,
                            _original.PresetName,
                            StringComparison.Ordinal))
                    ? _original.PresetName
                    : "Custom";

            ColorTargetBox.SelectedItem =
                Targets.First(
                    target =>
                        target.TextBoxName ==
                        "AccentBox");
        }
        finally
        {
            _loading =
                false;
        }

        LoadWheelForSelectedTarget();
        UpdateContrastStatus();
    }

    private readonly Dictionary<string, TextBox> _additionalBoxes = new();

    private void BuildAdditionalColorEditors()
    {
        foreach (var color in AdditionalThemeColors.All)
        {
            var label = new TextBlock { Text = color.Label, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FieldLabelBrush");
            var box = new TextBox { Tag = color.EditorName, MaxLength = 9 };
            System.Windows.Automation.AutomationProperties.SetName(box, color.Label);
            box.GotFocus += ColorBox_GotFocus;
            box.TextChanged += ColorBox_TextChanged;
            _additionalBoxes.Add(color.Name, box);
            Targets.Add(new ColorTarget(color.Label, color.EditorName));
            AdditionalColorsPanel.Children.Add(label);
            AdditionalColorsPanel.Children.Add(box);
        }
    }

    private ThemeSettings ReadBoxes(
        string presetName = "Custom")
    {
        var theme =
            new ThemeSettings
            {
                PresetName =
                    presetName,

                AppBackground =
                    BackgroundBox.Text,

                Surface =
                    SurfaceBox.Text,

                Panel =
                    PanelBox.Text,

                PanelAlt =
                    PanelAltBox.Text,

                Input =
                    InputBox.Text,

                Border =
                    BorderBox.Text,

                PrimaryText =
                    PrimaryTextBox.Text,

                SecondaryText =
                    SecondaryTextBox.Text,

                MutedText =
                    MutedTextBox.Text,

                Accent =
                    AccentBox.Text,

                AccentText =
                    AccentTextBox.Text,

                InputText =
                    InputTextBox.Text,

                InputBorder =
                    InputBorderBox.Text,

                DataGridBackground =
                    GridBackgroundBox.Text,

                DataGridAlternateBackground =
                    GridAlternateBox.Text,

                DataGridText =
                    GridTextBox.Text,

                DataGridHeaderBackground =
                    GridHeaderBackgroundBox.Text,

                DataGridHeaderText =
                    GridHeaderTextBox.Text,

                DataGridSelectedBackground =
                    GridSelectedBackgroundBox.Text,

                DataGridSelectedText =
                    GridSelectedTextBox.Text,

                DataGridGridLine =
                    GridLineBox.Text,

                TabHeaderBackground =
                    TabBackgroundBox.Text,

                TabHeaderText =
                    TabTextBox.Text,

                TabSelectedBackground =
                    TabSelectedBackgroundBox.Text,

                TabSelectedText =
                    TabSelectedTextBox.Text,

                TabBorder =
                    TabBorderBox.Text,

                CheckBoxText =
                    CheckBoxTextBox.Text,

                CheckBoxBackground =
                    CheckBoxBackgroundBox.Text,

                CheckBoxBorder =
                    CheckBoxBorderBox.Text,

                CheckBoxCheckMark =
                    CheckBoxCheckMarkBox.Text,

                ComboBoxBackground =
                    ComboBackgroundBox.Text,

                ComboBoxText =
                    ComboTextBox.Text,

                ComboBoxDropDownBackground =
                    ComboDropBackgroundBox.Text,

                ComboBoxDropDownText =
                    ComboDropTextBox.Text,

                ComboBoxHighlight =
                    ComboHighlightBox.Text,

                ComboBoxHighlightText =
                    ComboHighlightTextBox.Text,

                ComboBoxBorder =
                    ComboBorderBox.Text
            };

        foreach (var color in AdditionalThemeColors.All) color.Set(theme, _additionalBoxes[color.Name].Text);

        ThemeService.Validate(
            theme);

        NormalizeTheme(
            theme);

        return theme;
    }

    private static void NormalizeTheme(
        ThemeSettings theme)
    {
        foreach (var color in AdditionalThemeColors.All) color.Set(theme, ThemeService.NormalizeHex(color.Get(theme)));
        theme.AppBackground =
            ThemeService.NormalizeHex(
                theme.AppBackground);

        theme.Surface =
            ThemeService.NormalizeHex(
                theme.Surface);

        theme.Panel =
            ThemeService.NormalizeHex(
                theme.Panel);

        theme.PanelAlt =
            ThemeService.NormalizeHex(
                theme.PanelAlt);

        theme.Input =
            ThemeService.NormalizeHex(
                theme.Input);

        theme.Border =
            ThemeService.NormalizeHex(
                theme.Border);

        theme.PrimaryText =
            ThemeService.NormalizeHex(
                theme.PrimaryText);

        theme.SecondaryText =
            ThemeService.NormalizeHex(
                theme.SecondaryText);

        theme.MutedText =
            ThemeService.NormalizeHex(
                theme.MutedText);

        theme.Accent =
            ThemeService.NormalizeHex(
                theme.Accent);

        theme.AccentText =
            ThemeService.NormalizeHex(
                theme.AccentText);

        theme.InputText =
            ThemeService.NormalizeHex(
                theme.InputText);

        theme.InputBorder =
            ThemeService.NormalizeHex(
                theme.InputBorder);

        theme.DataGridBackground =
            ThemeService.NormalizeHex(
                theme.DataGridBackground);

        theme.DataGridAlternateBackground =
            ThemeService.NormalizeHex(
                theme.DataGridAlternateBackground);

        theme.DataGridText =
            ThemeService.NormalizeHex(
                theme.DataGridText);

        theme.DataGridHeaderBackground =
            ThemeService.NormalizeHex(
                theme.DataGridHeaderBackground);

        theme.DataGridHeaderText =
            ThemeService.NormalizeHex(
                theme.DataGridHeaderText);

        theme.DataGridSelectedBackground =
            ThemeService.NormalizeHex(
                theme.DataGridSelectedBackground);

        theme.DataGridSelectedText =
            ThemeService.NormalizeHex(
                theme.DataGridSelectedText);

        theme.DataGridGridLine =
            ThemeService.NormalizeHex(
                theme.DataGridGridLine);

        theme.TabHeaderBackground =
            ThemeService.NormalizeHex(
                theme.TabHeaderBackground);

        theme.TabHeaderText =
            ThemeService.NormalizeHex(
                theme.TabHeaderText);

        theme.TabSelectedBackground =
            ThemeService.NormalizeHex(
                theme.TabSelectedBackground);

        theme.TabSelectedText =
            ThemeService.NormalizeHex(
                theme.TabSelectedText);

        theme.TabBorder =
            ThemeService.NormalizeHex(
                theme.TabBorder);

        theme.CheckBoxText =
            ThemeService.NormalizeHex(
                theme.CheckBoxText);

        theme.CheckBoxBackground =
            ThemeService.NormalizeHex(
                theme.CheckBoxBackground);

        theme.CheckBoxBorder =
            ThemeService.NormalizeHex(
                theme.CheckBoxBorder);

        theme.CheckBoxCheckMark =
            ThemeService.NormalizeHex(
                theme.CheckBoxCheckMark);

        theme.ComboBoxBackground =
            ThemeService.NormalizeHex(
                theme.ComboBoxBackground);

        theme.ComboBoxText =
            ThemeService.NormalizeHex(
                theme.ComboBoxText);

        theme.ComboBoxDropDownBackground =
            ThemeService.NormalizeHex(
                theme.ComboBoxDropDownBackground);

        theme.ComboBoxDropDownText =
            ThemeService.NormalizeHex(
                theme.ComboBoxDropDownText);

        theme.ComboBoxHighlight =
            ThemeService.NormalizeHex(
                theme.ComboBoxHighlight);

        theme.ComboBoxHighlightText =
            ThemeService.NormalizeHex(
                theme.ComboBoxHighlightText);

        theme.ComboBoxBorder =
            ThemeService.NormalizeHex(
                theme.ComboBoxBorder);
    }

    private void LoadBoxes(
        ThemeSettings theme)
    {
        _loading =
            true;

        try
        {
            foreach (var color in AdditionalThemeColors.All) _additionalBoxes[color.Name].Text = color.Get(theme);

            BackgroundBox.Text =
                theme.AppBackground;

            SurfaceBox.Text =
                theme.Surface;

            PanelBox.Text =
                theme.Panel;

            PanelAltBox.Text =
                theme.PanelAlt;

            BorderBox.Text =
                theme.Border;

            PrimaryTextBox.Text =
                theme.PrimaryText;

            SecondaryTextBox.Text =
                theme.SecondaryText;

            MutedTextBox.Text =
                theme.MutedText;

            AccentBox.Text =
                theme.Accent;

            AccentTextBox.Text =
                theme.AccentText;

            InputBox.Text =
                theme.Input;

            InputTextBox.Text =
                theme.InputText;

            InputBorderBox.Text =
                theme.InputBorder;

            GridBackgroundBox.Text =
                theme.DataGridBackground;

            GridAlternateBox.Text =
                theme.DataGridAlternateBackground;

            GridTextBox.Text =
                theme.DataGridText;

            GridHeaderBackgroundBox.Text =
                theme.DataGridHeaderBackground;

            GridHeaderTextBox.Text =
                theme.DataGridHeaderText;

            GridSelectedBackgroundBox.Text =
                theme.DataGridSelectedBackground;

            GridSelectedTextBox.Text =
                theme.DataGridSelectedText;

            GridLineBox.Text =
                theme.DataGridGridLine;

            TabBackgroundBox.Text =
                theme.TabHeaderBackground;

            TabTextBox.Text =
                theme.TabHeaderText;

            TabSelectedBackgroundBox.Text =
                theme.TabSelectedBackground;

            TabSelectedTextBox.Text =
                theme.TabSelectedText;

            TabBorderBox.Text =
                theme.TabBorder;

            CheckBoxTextBox.Text =
                theme.CheckBoxText;

            CheckBoxBackgroundBox.Text =
                theme.CheckBoxBackground;

            CheckBoxBorderBox.Text =
                theme.CheckBoxBorder;

            CheckBoxCheckMarkBox.Text =
                theme.CheckBoxCheckMark;

            ComboBackgroundBox.Text =
                theme.ComboBoxBackground;

            ComboTextBox.Text =
                theme.ComboBoxText;

            ComboDropBackgroundBox.Text =
                theme.ComboBoxDropDownBackground;

            ComboDropTextBox.Text =
                theme.ComboBoxDropDownText;

            ComboHighlightBox.Text =
                theme.ComboBoxHighlight;

            ComboHighlightTextBox.Text =
                theme.ComboBoxHighlightText;

            ComboBorderBox.Text =
                theme.ComboBoxBorder;
        }
        finally
        {
            _loading =
                false;
        }
    }

    private TextBox? FindTargetBox(
        string name) =>
        FindName(name) as TextBox ?? _additionalBoxes.Values.FirstOrDefault(box => (string?)box.Tag == name);

    private ColorTarget? FindTarget(
        string textBoxName) =>
        Targets.FirstOrDefault(
            target =>
                string.Equals(
                    target.TextBoxName,
                    textBoxName,
                    StringComparison.Ordinal));

    private void ColorBox_GotFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _loading ||
            sender is not TextBox box ||
            box.Tag is not string name)
        {
            return;
        }

        var target =
            FindTarget(
                name);

        if (target is null)
        {
            return;
        }

        _loading =
            true;

        try
        {
            ColorTargetBox.SelectedItem =
                target;
        }
        finally
        {
            _loading =
                false;
        }

        LoadWheelForSelectedTarget();
    }

    private void ColorBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (
            _loading ||
            sender is not TextBox box ||
            box.Tag is not string textBoxName)
        {
            return;
        }

        var target =
            FindTarget(
                textBoxName);

        if (target is not null &&
            ReferenceEquals(
                ColorTargetBox.SelectedItem,
                target))
        {
            TryLoadWheelFromBox(
                box);
        }

        if (!string.Equals(
                PresetBox.SelectedItem as string,
                "Custom",
                StringComparison.Ordinal))
        {
            PresetBox.SelectedItem =
                "Custom";
        }

        TryPreviewQuietly();
    }

    private void ColorTargetBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        LoadWheelForSelectedTarget();
    }

    private void LoadWheelForSelectedTarget()
    {
        if (
            ColorTargetBox.SelectedItem is not ColorTarget target)
        {
            return;
        }

        var box =
            FindTargetBox(
                target.TextBoxName);

        if (box is null)
        {
            return;
        }

        if (!TryLoadWheelFromBox(
                box))
        {
            StatusText.Text =
                $"The {target.Label} value is not a valid theme color yet. " +
                "Enter a valid hex color before using the wheel for this field.";
        }
    }

    private bool TryLoadWheelFromBox(
        TextBox box)
    {
        try
        {
            var color =
                ThemeService.ParseThemeColor(
                    box.Text);

            _loading =
                true;

            try
            {
                ColorWheel.SetColor(
                    color,
                    false);
            }
            finally
            {
                _loading =
                    false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ColorWheel_ColorChanged(
        object? sender,
        ColorWheelChangedEventArgs e)
    {
        if (
            _loading ||
            ColorTargetBox.SelectedItem is not ColorTarget target)
        {
            return;
        }

        var box =
            FindTargetBox(
                target.TextBoxName);

        if (box is null)
        {
            return;
        }

        // TextChanged marks the theme Custom, updates readability and performs
        // the actual preview after the wheel writes the new valid color.
        box.Text =
            ThemeService.ToHex(
                e.Color);
    }

    private bool TryPreviewQuietly()
    {
        try
        {
            var theme =
                ReadBoxes(
                    PresetBox.SelectedItem as string ??
                    "Custom");

            ThemeService.Apply(
                theme);

            UpdateContrastStatus(
                theme);

            StatusText.Text =
                "Previewing custom appearance. Save Theme to persist it; closing restores the last saved theme.";

            return true;
        }
        catch
        {
            UpdateContrastStatus();
            return false;
        }
    }

    private void UpdateContrastStatus(
        ThemeSettings? theme = null)
    {
        try
        {
            theme ??=
                ReadBoxes(
                    PresetBox.SelectedItem as string ??
                    "Custom");

            var checks =
                new[]
                {
                    (
                        "primary on background",
                        ThemeService.ContrastRatio(
                            theme.PrimaryText,
                            theme.AppBackground)
                    ),
                    (
                        "primary on panel",
                        ThemeService.ContrastRatio(
                            theme.PrimaryText,
                            theme.Panel)
                    ),
                    (
                        "secondary on background",
                        ThemeService.ContrastRatio(
                            theme.SecondaryText,
                            theme.AppBackground)
                    ),
                    (
                        "muted on panel",
                        ThemeService.ContrastRatio(
                            theme.MutedText,
                            theme.Panel)
                    ),
                    (
                        "accent text",
                        ThemeService.ContrastRatio(
                            theme.AccentText,
                            theme.Accent)
                    ),
                    (
                        "input",
                        ThemeService.ContrastRatio(
                            theme.InputText,
                            theme.Input)
                    ),
                    (
                        "table rows",
                        ThemeService.ContrastRatio(
                            theme.DataGridText,
                            theme.DataGridBackground)
                    ),
                    (
                        "table alt rows",
                        ThemeService.ContrastRatio(
                            theme.DataGridText,
                            theme.DataGridAlternateBackground)
                    ),
                    (
                        "table headers",
                        ThemeService.ContrastRatio(
                            theme.DataGridHeaderText,
                            theme.DataGridHeaderBackground)
                    ),
                    (
                        "table selection",
                        ThemeService.ContrastRatio(
                            theme.DataGridSelectedText,
                            theme.DataGridSelectedBackground)
                    ),
                    (
                        "tabs",
                        ThemeService.ContrastRatio(
                            theme.TabHeaderText,
                            theme.TabHeaderBackground)
                    ),
                    (
                        "active tab",
                        ThemeService.ContrastRatio(
                            theme.TabSelectedText,
                            theme.TabSelectedBackground)
                    ),
                    (
                        "checkbox on panel",
                        ThemeService.ContrastRatio(
                            theme.CheckBoxText,
                            theme.Panel)
                    ),
                    (
                        "checkbox on alt panel",
                        ThemeService.ContrastRatio(
                            theme.CheckBoxText,
                            theme.PanelAlt)
                    ),
                    (
                        "checkbox on surface",
                        ThemeService.ContrastRatio(
                            theme.CheckBoxText,
                            theme.Surface)
                    ),
                    (
                        "dropdown closed",
                        ThemeService.ContrastRatio(
                            theme.ComboBoxText,
                            theme.ComboBoxBackground)
                    ),
                    (
                        "dropdown popup",
                        ThemeService.ContrastRatio(
                            theme.ComboBoxDropDownText,
                            theme.ComboBoxDropDownBackground)
                    ),
                    (
                        "dropdown highlight",
                        ThemeService.ContrastRatio(
                            theme.ComboBoxHighlightText,
                            theme.ComboBoxHighlight)
                    )
                };

            checks = checks.Concat(new[]
            {
                ("section headings on surface", ThemeService.ContrastRatio(theme.SectionHeading, theme.Surface)),
                ("section headings on panel", ThemeService.ContrastRatio(theme.SectionHeading, theme.Panel)),
                ("field labels on surface", ThemeService.ContrastRatio(theme.FieldLabel, theme.Surface)),
                ("expander text", ThemeService.ContrastRatio(theme.ExpanderHeader, theme.AppBackground)),
                ("buttons", ThemeService.ContrastRatio(theme.ButtonText, theme.ButtonBackground)),
                ("hover buttons", ThemeService.ContrastRatio(theme.ButtonHoverText, theme.ControlHover)),
                ("tooltips", ThemeService.ContrastRatio(theme.TooltipText, theme.TooltipBackground)),
                ("context menu", ThemeService.ContrastRatio(theme.MenuText, theme.MenuBackground))
            }).ToArray();

            var weak =
                checks
                    .Where(
                        check =>
                            check.Item2 <
                            4.5)
                    .ToList();

            if (weak.Count == 0)
            {
                ContrastText.Text =
                    "All checked text/background pairs are at least 4.5:1. " +
                    string.Join(
                        " • ",
                        checks.Select(
                            check =>
                                $"{check.Item1} {check.Item2:0.0}:1"));

                return;
            }

            ContrastText.Text =
                "LOW CONTRAST: " +
                string.Join(
                    ", ",
                    weak.Select(
                        check =>
                            $"{check.Item1} {check.Item2:0.0}:1")) +
                ". Aim for at least 4.5:1 for normal UI text. " +
                "Other pairs: " +
                string.Join(
                    " • ",
                    checks
                        .Where(
                            check =>
                                check.Item2 >=
                                4.5)
                        .Select(
                            check =>
                                $"{check.Item1} {check.Item2:0.0}:1"));
        }
        catch
        {
            ContrastText.Text =
                "Enter valid theme hex colors (for example #RRGGBB or #AARRGGBB) to calculate readability contrast.";
        }
    }

    private void PresetBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (
            _loading ||
            PresetBox.SelectedItem is not string name ||
            string.Equals(
                name,
                "Custom",
                StringComparison.Ordinal))
        {
            return;
        }

        var preset =
            ThemeCatalog.Presets.FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.PresetName,
                        name,
                        StringComparison.Ordinal));

        if (preset is null)
        {
            return;
        }

        try
        {
            var preview =
                ThemeCatalog.Clone(
                    preset);

            LoadBoxes(
                preview);

            ThemeService.Apply(
                ReadBoxes(
                    name));

            LoadWheelForSelectedTarget();
            UpdateContrastStatus();

            StatusText.Text =
                $"Previewing {name}. Save Theme to persist it; closing restores the last saved theme.";
        }
        catch (Exception ex)
        {
            RestoreSavedPreview();

            MessageBox.Show(
                ex.Message,
                "Appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyPreview_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var theme =
                ReadBoxes(
                    PresetBox.SelectedItem as string ??
                    "Custom");

            ThemeService.Apply(
                theme);

            UpdateContrastStatus(
                theme);

            StatusText.Text =
                "Preview applied. It is not saved; closing restores the last saved theme.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveTheme_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var selected =
                PresetBox.SelectedItem as string ??
                "Custom";

            var theme =
                ReadBoxes(
                    selected);

            // Persist first. If disk/settings persistence fails, ADT must not
            // report the preview as the new saved theme.
            var app =
                _store.Load();

            app.Theme =
                theme;

            _store.Save(
                app);

            _original =
                ThemeCatalog.Clone(
                    theme);

            ThemeService.Apply(
                theme);

            LoadBoxes(
                theme);

            LoadWheelForSelectedTarget();
            UpdateContrastStatus(
                theme);

            StatusText.Text =
                "Theme saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ResetTheme_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (ThemeCatalog.Presets.Count == 0)
            {
                throw new InvalidOperationException(
                    "ADT does not have a default appearance preset available.");
            }

            var theme =
                ThemeCatalog.Clone(
                    ThemeCatalog.Presets[0]);

            LoadBoxes(
                theme);

            _loading =
                true;

            try
            {
                PresetBox.SelectedItem =
                    theme.PresetName;
            }
            finally
            {
                _loading =
                    false;
            }

            ThemeService.Apply(
                theme);

            LoadWheelForSelectedTarget();
            UpdateContrastStatus(
                theme);

            StatusText.Text =
                $"Previewing default preset: {theme.PresetName}. Save Theme to persist it; closing restores the last saved theme.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RestoreSavedPreview()
    {
        try
        {
            ThemeService.Apply(
                _original);

            LoadBoxes(
                _original);

            _loading =
                true;

            try
            {
                PresetBox.SelectedItem =
                    ThemeCatalog.Presets.Any(
                        preset =>
                            string.Equals(
                                preset.PresetName,
                                _original.PresetName,
                                StringComparison.Ordinal))
                        ? _original.PresetName
                        : "Custom";
            }
            finally
            {
                _loading =
                    false;
            }

            LoadWheelForSelectedTarget();
            UpdateContrastStatus(
                _original);
        }
        catch
        {
            // Appearance recovery is best-effort. Never turn a preview failure
            // into a window-close/application-shutdown failure.
        }
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(
        EventArgs e)
    {
        // Never re-read settings during the close path. _original always
        // represents the last theme that was successfully loaded or saved in
        // this window, so it is the safest source for reverting unsaved preview.
        try
        {
            ThemeService.Apply(
                _original);
        }
        catch
        {
            // Closing the Appearance window must not crash ADT.
        }

        base.OnClosed(
            e);
    }
}
