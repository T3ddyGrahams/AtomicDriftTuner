using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class ThemeWindow : Window
{
    private readonly AppSettingsStore _store = new();
    private ThemeSettings _original;
    private bool _loading;

    private sealed record ColorTarget(string Label, string TextBoxName)
    {
        public override string ToString() => Label;
    }

    private IReadOnlyList<ColorTarget> Targets { get; } =
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

    public ThemeWindow()
    {
        InitializeComponent();
        _original = ThemeCatalog.Clone(_store.Load().Theme);
        PresetBox.ItemsSource = ThemeCatalog.Presets.Select(x => x.PresetName).Concat(new[] { "Custom" }).ToList();
        ColorTargetBox.ItemsSource = Targets;
        LoadBoxes(_original);

        PreviewGrid.ItemsSource = new[]
        {
            new { Group = "Core", Setting = "Base Torque Output", Live = "68%", Target = "68%" },
            new { Group = "Core", Setting = "Maximum Wheel Speed", Live = "135%", Target = "142%" },
            new { Group = "Effects", Setting = "Wheel Damper", Live = "8%", Target = "7%" }
        };

        _loading = true;
        PresetBox.SelectedItem = ThemeCatalog.Presets.Any(x => x.PresetName == _original.PresetName)
            ? _original.PresetName
            : "Custom";
        ColorTargetBox.SelectedItem = Targets.First(x => x.TextBoxName == "AccentBox");
        _loading = false;
        LoadWheelForSelectedTarget();
        UpdateContrastStatus();
    }

    private ThemeSettings ReadBoxes(string presetName = "Custom")
    {
        var t = new ThemeSettings
        {
            PresetName = presetName,
            AppBackground = BackgroundBox.Text,
            Surface = SurfaceBox.Text,
            Panel = PanelBox.Text,
            PanelAlt = PanelAltBox.Text,
            Input = InputBox.Text,
            Border = BorderBox.Text,
            PrimaryText = PrimaryTextBox.Text,
            SecondaryText = SecondaryTextBox.Text,
            MutedText = MutedTextBox.Text,
            Accent = AccentBox.Text,
            AccentText = AccentTextBox.Text,

            InputText = InputTextBox.Text,
            InputBorder = InputBorderBox.Text,

            DataGridBackground = GridBackgroundBox.Text,
            DataGridAlternateBackground = GridAlternateBox.Text,
            DataGridText = GridTextBox.Text,
            DataGridHeaderBackground = GridHeaderBackgroundBox.Text,
            DataGridHeaderText = GridHeaderTextBox.Text,
            DataGridSelectedBackground = GridSelectedBackgroundBox.Text,
            DataGridSelectedText = GridSelectedTextBox.Text,
            DataGridGridLine = GridLineBox.Text,

            TabHeaderBackground = TabBackgroundBox.Text,
            TabHeaderText = TabTextBox.Text,
            TabSelectedBackground = TabSelectedBackgroundBox.Text,
            TabSelectedText = TabSelectedTextBox.Text,
            TabBorder = TabBorderBox.Text,

            CheckBoxText = CheckBoxTextBox.Text,
            CheckBoxBackground = CheckBoxBackgroundBox.Text,
            CheckBoxBorder = CheckBoxBorderBox.Text,
            CheckBoxCheckMark = CheckBoxCheckMarkBox.Text,

            ComboBoxBackground = ComboBackgroundBox.Text,
            ComboBoxText = ComboTextBox.Text,
            ComboBoxDropDownBackground = ComboDropBackgroundBox.Text,
            ComboBoxDropDownText = ComboDropTextBox.Text,
            ComboBoxHighlight = ComboHighlightBox.Text,
            ComboBoxHighlightText = ComboHighlightTextBox.Text,
            ComboBoxBorder = ComboBorderBox.Text
        };

        ThemeService.Validate(t);

        t.AppBackground = ThemeService.NormalizeHex(t.AppBackground);
        t.Surface = ThemeService.NormalizeHex(t.Surface);
        t.Panel = ThemeService.NormalizeHex(t.Panel);
        t.PanelAlt = ThemeService.NormalizeHex(t.PanelAlt);
        t.Input = ThemeService.NormalizeHex(t.Input);
        t.Border = ThemeService.NormalizeHex(t.Border);
        t.PrimaryText = ThemeService.NormalizeHex(t.PrimaryText);
        t.SecondaryText = ThemeService.NormalizeHex(t.SecondaryText);
        t.MutedText = ThemeService.NormalizeHex(t.MutedText);
        t.Accent = ThemeService.NormalizeHex(t.Accent);
        t.AccentText = ThemeService.NormalizeHex(t.AccentText);

        t.InputText = ThemeService.NormalizeHex(t.InputText);
        t.InputBorder = ThemeService.NormalizeHex(t.InputBorder);

        t.DataGridBackground = ThemeService.NormalizeHex(t.DataGridBackground);
        t.DataGridAlternateBackground = ThemeService.NormalizeHex(t.DataGridAlternateBackground);
        t.DataGridText = ThemeService.NormalizeHex(t.DataGridText);
        t.DataGridHeaderBackground = ThemeService.NormalizeHex(t.DataGridHeaderBackground);
        t.DataGridHeaderText = ThemeService.NormalizeHex(t.DataGridHeaderText);
        t.DataGridSelectedBackground = ThemeService.NormalizeHex(t.DataGridSelectedBackground);
        t.DataGridSelectedText = ThemeService.NormalizeHex(t.DataGridSelectedText);
        t.DataGridGridLine = ThemeService.NormalizeHex(t.DataGridGridLine);

        t.TabHeaderBackground = ThemeService.NormalizeHex(t.TabHeaderBackground);
        t.TabHeaderText = ThemeService.NormalizeHex(t.TabHeaderText);
        t.TabSelectedBackground = ThemeService.NormalizeHex(t.TabSelectedBackground);
        t.TabSelectedText = ThemeService.NormalizeHex(t.TabSelectedText);
        t.TabBorder = ThemeService.NormalizeHex(t.TabBorder);

        t.CheckBoxText = ThemeService.NormalizeHex(t.CheckBoxText);
        t.CheckBoxBackground = ThemeService.NormalizeHex(t.CheckBoxBackground);
        t.CheckBoxBorder = ThemeService.NormalizeHex(t.CheckBoxBorder);
        t.CheckBoxCheckMark = ThemeService.NormalizeHex(t.CheckBoxCheckMark);

        t.ComboBoxBackground = ThemeService.NormalizeHex(t.ComboBoxBackground);
        t.ComboBoxText = ThemeService.NormalizeHex(t.ComboBoxText);
        t.ComboBoxDropDownBackground = ThemeService.NormalizeHex(t.ComboBoxDropDownBackground);
        t.ComboBoxDropDownText = ThemeService.NormalizeHex(t.ComboBoxDropDownText);
        t.ComboBoxHighlight = ThemeService.NormalizeHex(t.ComboBoxHighlight);
        t.ComboBoxHighlightText = ThemeService.NormalizeHex(t.ComboBoxHighlightText);
        t.ComboBoxBorder = ThemeService.NormalizeHex(t.ComboBoxBorder);

        return t;
    }

    private void LoadBoxes(ThemeSettings t)
    {
        _loading = true;

        BackgroundBox.Text = t.AppBackground;
        SurfaceBox.Text = t.Surface;
        PanelBox.Text = t.Panel;
        PanelAltBox.Text = t.PanelAlt;
        BorderBox.Text = t.Border;
        PrimaryTextBox.Text = t.PrimaryText;
        SecondaryTextBox.Text = t.SecondaryText;
        MutedTextBox.Text = t.MutedText;
        AccentBox.Text = t.Accent;
        AccentTextBox.Text = t.AccentText;

        InputBox.Text = t.Input;
        InputTextBox.Text = t.InputText;
        InputBorderBox.Text = t.InputBorder;

        GridBackgroundBox.Text = t.DataGridBackground;
        GridAlternateBox.Text = t.DataGridAlternateBackground;
        GridTextBox.Text = t.DataGridText;
        GridHeaderBackgroundBox.Text = t.DataGridHeaderBackground;
        GridHeaderTextBox.Text = t.DataGridHeaderText;
        GridSelectedBackgroundBox.Text = t.DataGridSelectedBackground;
        GridSelectedTextBox.Text = t.DataGridSelectedText;
        GridLineBox.Text = t.DataGridGridLine;

        TabBackgroundBox.Text = t.TabHeaderBackground;
        TabTextBox.Text = t.TabHeaderText;
        TabSelectedBackgroundBox.Text = t.TabSelectedBackground;
        TabSelectedTextBox.Text = t.TabSelectedText;
        TabBorderBox.Text = t.TabBorder;

        CheckBoxTextBox.Text = t.CheckBoxText;
        CheckBoxBackgroundBox.Text = t.CheckBoxBackground;
        CheckBoxBorderBox.Text = t.CheckBoxBorder;
        CheckBoxCheckMarkBox.Text = t.CheckBoxCheckMark;

        ComboBackgroundBox.Text = t.ComboBoxBackground;
        ComboTextBox.Text = t.ComboBoxText;
        ComboDropBackgroundBox.Text = t.ComboBoxDropDownBackground;
        ComboDropTextBox.Text = t.ComboBoxDropDownText;
        ComboHighlightBox.Text = t.ComboBoxHighlight;
        ComboHighlightTextBox.Text = t.ComboBoxHighlightText;
        ComboBorderBox.Text = t.ComboBoxBorder;

        _loading = false;
    }

    private TextBox? FindTargetBox(string name) => FindName(name) as TextBox;

    private void ColorBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not TextBox box || box.Tag is not string name) return;
        var target = Targets.FirstOrDefault(x => x.TextBoxName == name);
        if (target != null)
        {
            _loading = true;
            ColorTargetBox.SelectedItem = target;
            _loading = false;
            LoadWheelForSelectedTarget();
        }
    }

    private void ColorTargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadWheelForSelectedTarget();
    }

    private void LoadWheelForSelectedTarget()
    {
        if (ColorTargetBox.SelectedItem is not ColorTarget target) return;
        var box = FindTargetBox(target.TextBoxName);
        if (box == null) return;
        try
        {
            _loading = true;
            ColorWheel.SetColor(ThemeService.ParseThemeColor(box.Text), false);
        }
        catch { }
        finally { _loading = false; }
    }

    private void ColorWheel_ColorChanged(object? sender, ColorWheelChangedEventArgs e)
    {
        if (_loading || ColorTargetBox.SelectedItem is not ColorTarget target) return;
        var box = FindTargetBox(target.TextBoxName);
        if (box == null) return;
        box.Text = ThemeService.ToHex(e.Color);
        PresetBox.SelectedItem = "Custom";
        TryPreviewQuietly();
    }

    private void TryPreviewQuietly()
    {
        try
        {
            var t = ReadBoxes(PresetBox.SelectedItem as string ?? "Custom");
            ThemeService.Apply(t);
            UpdateContrastStatus(t);
        }
        catch { }
    }

    private void UpdateContrastStatus(ThemeSettings? theme = null)
    {
        try
        {
            theme ??= ReadBoxes(PresetBox.SelectedItem as string ?? "Custom");

            var checks = new[]
            {
                ("input", ThemeService.ContrastRatio(theme.InputText, theme.Input)),
                ("table rows", ThemeService.ContrastRatio(theme.DataGridText, theme.DataGridBackground)),
                ("table alt rows", ThemeService.ContrastRatio(theme.DataGridText, theme.DataGridAlternateBackground)),
                ("table headers", ThemeService.ContrastRatio(theme.DataGridHeaderText, theme.DataGridHeaderBackground)),
                ("table selection", ThemeService.ContrastRatio(theme.DataGridSelectedText, theme.DataGridSelectedBackground)),
                ("tabs", ThemeService.ContrastRatio(theme.TabHeaderText, theme.TabHeaderBackground)),
                ("active tab", ThemeService.ContrastRatio(theme.TabSelectedText, theme.TabSelectedBackground)),
                ("checkbox on panel", ThemeService.ContrastRatio(theme.CheckBoxText, theme.Panel)),
                ("checkbox on alt panel", ThemeService.ContrastRatio(theme.CheckBoxText, theme.PanelAlt)),
                ("checkbox on surface", ThemeService.ContrastRatio(theme.CheckBoxText, theme.Surface)),
                ("dropdown closed", ThemeService.ContrastRatio(theme.ComboBoxText, theme.ComboBoxBackground)),
                ("dropdown popup", ThemeService.ContrastRatio(theme.ComboBoxDropDownText, theme.ComboBoxDropDownBackground)),
                ("dropdown highlight", ThemeService.ContrastRatio(theme.ComboBoxHighlightText, theme.ComboBoxHighlight))
            };

            var weak = checks.Where(x => x.Item2 < 4.5).ToList();

            if (weak.Count == 0)
            {
                ContrastText.Text =
                    "All major text/background pairs are at least 4.5:1. " +
                    string.Join(" • ", checks.Select(x => $"{x.Item1} {x.Item2:0.0}:1"));
            }
            else
            {
                ContrastText.Text =
                    "LOW CONTRAST: " +
                    string.Join(", ", weak.Select(x => $"{x.Item1} {x.Item2:0.0}:1")) +
                    ". Aim for at least 4.5:1 for normal UI text. " +
                    "Other pairs: " +
                    string.Join(" • ", checks.Where(x => x.Item2 >= 4.5).Select(x => $"{x.Item1} {x.Item2:0.0}:1"));
            }
        }
        catch
        {
            ContrastText.Text = "Enter valid #RRGGBB values to calculate readability contrast.";
        }
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedItem is not string name || name == "Custom") return;
        var preset = ThemeCatalog.Presets.FirstOrDefault(x => x.PresetName == name);
        if (preset == null) return;
        LoadBoxes(ThemeCatalog.Clone(preset));
        ThemeService.Apply(ReadBoxes(name));
        LoadWheelForSelectedTarget();
        UpdateContrastStatus();
        StatusText.Text = $"Previewing {name}.";
    }

    private void ApplyPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var t = ReadBoxes(PresetBox.SelectedItem as string ?? "Custom");
            ThemeService.Apply(t);
            UpdateContrastStatus(t);
            StatusText.Text = "Preview applied. It is not saved yet.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SaveTheme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = PresetBox.SelectedItem as string ?? "Custom";
            var t = ReadBoxes(selected);
            ThemeService.Apply(t);
            var app = _store.Load();
            app.Theme = t;
            _store.Save(app);
            _original = ThemeCatalog.Clone(t);
            UpdateContrastStatus(t);
            StatusText.Text = "Theme saved.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ResetTheme_Click(object sender, RoutedEventArgs e)
    {
        var t = ThemeCatalog.Clone(ThemeCatalog.Presets[0]);
        LoadBoxes(t);
        PresetBox.SelectedItem = t.PresetName;
        ThemeService.Apply(t);
        LoadWheelForSelectedTarget();
        UpdateContrastStatus(t);
        StatusText.Text = "Atomic Cyan preview restored.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Apply(_store.Load().Theme);
        base.OnClosed(e);
    }
}
