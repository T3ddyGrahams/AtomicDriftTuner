namespace AtomicDriftTuner.Models;

public sealed class ThemeSettings
{
    public string PresetName { get; set; } = "Atomic Cyan";
    public string AppBackground { get; set; } = "#0E1116";
    public string Surface { get; set; } = "#171B22";
    public string Panel { get; set; } = "#20252D";
    public string PanelAlt { get; set; } = "#242931";
    public string Input { get; set; } = "#272C34";
    public string Border { get; set; } = "#3A414D";
    public string PrimaryText { get; set; } = "#F5F7FA";
    public string SecondaryText { get; set; } = "#9FA8B8";
    public string MutedText { get; set; } = "#AEB6C3";
    public string Accent { get; set; } = "#00CFE8";
    public string AccentText { get; set; } = "#071014";

    // Input fields are intentionally separate from general panel/text colors.
    // This prevents Windows/WPF defaults or a custom theme from creating
    // unreadable light-on-light or dark-on-dark edit fields.
    public string InputText { get; set; } = "#F5F7FA";
    public string InputBorder { get; set; } = "#3A414D";

    // DataGrid/table colors are fully themeable because WPF's platform default
    // column headers/cells can otherwise ignore a dark application theme.
    public string DataGridBackground { get; set; } = "#20252D";
    public string DataGridAlternateBackground { get; set; } = "#242931";
    public string DataGridText { get; set; } = "#F5F7FA";
    public string DataGridHeaderBackground { get; set; } = "#171B22";
    public string DataGridHeaderText { get; set; } = "#F5F7FA";
    public string DataGridSelectedBackground { get; set; } = "#00CFE8";
    public string DataGridSelectedText { get; set; } = "#071014";
    public string DataGridGridLine { get; set; } = "#3A414D";

    // Tab headers are also explicitly themed so section navigation remains
    // readable regardless of Windows accent/high-contrast defaults.
    public string TabHeaderBackground { get; set; } = "#20252D";
    public string TabHeaderText { get; set; } = "#F5F7FA";
    public string TabSelectedBackground { get; set; } = "#00CFE8";
    public string TabSelectedText { get; set; } = "#071014";
    public string TabBorder { get; set; } = "#3A414D";

    // CheckBox text/glyph colors are explicit. WPF's platform CheckBox theme
    // can otherwise render content using a system color that clashes with a
    // custom Atomic theme.
    public string CheckBoxText { get; set; } = "#F5F7FA";
    public string CheckBoxBackground { get; set; } = "#272C34";
    public string CheckBoxBorder { get; set; } = "#3A414D";
    public string CheckBoxCheckMark { get; set; } = "#00CFE8";

    // ComboBox / dropdown colors are intentionally separate from the rest of
    // the theme. This prevents a good-looking panel theme from accidentally
    // producing unreadable white-on-white Windows dropdown popups.
    public string ComboBoxBackground { get; set; } = "#272C34";
    public string ComboBoxText { get; set; } = "#F5F7FA";
    public string ComboBoxDropDownBackground { get; set; } = "#20252D";
    public string ComboBoxDropDownText { get; set; } = "#F5F7FA";
    public string ComboBoxHighlight { get; set; } = "#00CFE8";
    public string ComboBoxHighlightText { get; set; } = "#071014";
    public string ComboBoxBorder { get; set; } = "#3A414D";
}

public static class ThemeCatalog
{
    private static ThemeSettings Preset(
        string name,
        string app,
        string surface,
        string panel,
        string panelAlt,
        string input,
        string border,
        string primary,
        string secondary,
        string muted,
        string accent,
        string accentText)
        => new()
        {
            PresetName = name,
            AppBackground = app,
            Surface = surface,
            Panel = panel,
            PanelAlt = panelAlt,
            Input = input,
            Border = border,
            PrimaryText = primary,
            SecondaryText = secondary,
            MutedText = muted,
            Accent = accent,
            AccentText = accentText,
            InputText = primary,
            InputBorder = border,
            DataGridBackground = panel,
            DataGridAlternateBackground = panelAlt,
            DataGridText = primary,
            DataGridHeaderBackground = surface,
            DataGridHeaderText = primary,
            DataGridSelectedBackground = accent,
            DataGridSelectedText = accentText,
            DataGridGridLine = border,
            TabHeaderBackground = panel,
            TabHeaderText = primary,
            TabSelectedBackground = accent,
            TabSelectedText = accentText,
            TabBorder = border,
            CheckBoxText = primary,
            CheckBoxBackground = input,
            CheckBoxBorder = border,
            CheckBoxCheckMark = accent,
            ComboBoxBackground = input,
            ComboBoxText = primary,
            ComboBoxDropDownBackground = panel,
            ComboBoxDropDownText = primary,
            ComboBoxHighlight = accent,
            ComboBoxHighlightText = accentText,
            ComboBoxBorder = border
        };

    public static IReadOnlyList<ThemeSettings> Presets { get; } = new List<ThemeSettings>
    {
        Preset("Atomic Cyan", "#0E1116", "#171B22", "#20252D", "#242931", "#272C34", "#3A414D", "#F5F7FA", "#9FA8B8", "#AEB6C3", "#00CFE8", "#071014"),
        Preset("Drift Orange", "#100E0C", "#1D1814", "#282019", "#33271E", "#30261E", "#59412D", "#FFF7EE", "#CDB9A7", "#BDA590", "#FF8A1F", "#180B00"),
        Preset("Neon Purple", "#0E0B14", "#191322", "#241A31", "#2E213D", "#2B2037", "#513B69", "#FBF5FF", "#C4B2D5", "#AF98C3", "#B66CFF", "#12051D"),
        Preset("Race Red", "#110D0E", "#1D1517", "#291C1F", "#332226", "#302023", "#5B343C", "#FFF5F6", "#D0B2B7", "#BE9AA1", "#FF4D67", "#190307"),
        Preset("Ice Blue", "#0B1116", "#121D25", "#192832", "#21333E", "#1D2D37", "#345467", "#F3FAFF", "#AFC8D7", "#94B3C5", "#61D4FF", "#041118"),
        Preset("Monochrome", "#101010", "#191919", "#222222", "#2A2A2A", "#262626", "#454545", "#F4F4F4", "#B8B8B8", "#A1A1A1", "#E8E8E8", "#111111")
    };

    public static ThemeSettings Clone(ThemeSettings t) => new()
    {
        PresetName = t.PresetName,
        AppBackground = t.AppBackground,
        Surface = t.Surface,
        Panel = t.Panel,
        PanelAlt = t.PanelAlt,
        Input = t.Input,
        Border = t.Border,
        PrimaryText = t.PrimaryText,
        SecondaryText = t.SecondaryText,
        MutedText = t.MutedText,
        Accent = t.Accent,
        AccentText = t.AccentText,
        InputText = t.InputText,
        InputBorder = t.InputBorder,
        DataGridBackground = t.DataGridBackground,
        DataGridAlternateBackground = t.DataGridAlternateBackground,
        DataGridText = t.DataGridText,
        DataGridHeaderBackground = t.DataGridHeaderBackground,
        DataGridHeaderText = t.DataGridHeaderText,
        DataGridSelectedBackground = t.DataGridSelectedBackground,
        DataGridSelectedText = t.DataGridSelectedText,
        DataGridGridLine = t.DataGridGridLine,
        TabHeaderBackground = t.TabHeaderBackground,
        TabHeaderText = t.TabHeaderText,
        TabSelectedBackground = t.TabSelectedBackground,
        TabSelectedText = t.TabSelectedText,
        TabBorder = t.TabBorder,
        CheckBoxText = t.CheckBoxText,
        CheckBoxBackground = t.CheckBoxBackground,
        CheckBoxBorder = t.CheckBoxBorder,
        CheckBoxCheckMark = t.CheckBoxCheckMark,
        ComboBoxBackground = t.ComboBoxBackground,
        ComboBoxText = t.ComboBoxText,
        ComboBoxDropDownBackground = t.ComboBoxDropDownBackground,
        ComboBoxDropDownText = t.ComboBoxDropDownText,
        ComboBoxHighlight = t.ComboBoxHighlight,
        ComboBoxHighlightText = t.ComboBoxHighlightText,
        ComboBoxBorder = t.ComboBoxBorder
    };
}
