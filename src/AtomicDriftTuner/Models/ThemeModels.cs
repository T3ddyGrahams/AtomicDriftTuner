namespace AtomicDriftTuner.Models;

public sealed class ThemeSettings
{
    private string _presetName = "Atomic Cyan";
    private string _appBackground = "#0E1116";
    private string _surface = "#171B22";
    private string _panel = "#20252D";
    private string _panelAlt = "#242931";
    private string _input = "#272C34";
    private string _border = "#3A414D";
    private string _primaryText = "#F5F7FA";
    private string _secondaryText = "#9FA8B8";
    private string _mutedText = "#AEB6C3";
    private string _accent = "#00CFE8";
    private string _accentText = "#071014";
    private string _inputText = "#F5F7FA";
    private string _inputBorder = "#3A414D";
    private string _dataGridBackground = "#20252D";
    private string _dataGridAlternateBackground = "#242931";
    private string _dataGridText = "#F5F7FA";
    private string _dataGridHeaderBackground = "#171B22";
    private string _dataGridHeaderText = "#F5F7FA";
    private string _dataGridSelectedBackground = "#00CFE8";
    private string _dataGridSelectedText = "#071014";
    private string _dataGridGridLine = "#3A414D";
    private string _tabHeaderBackground = "#20252D";
    private string _tabHeaderText = "#F5F7FA";
    private string _tabSelectedBackground = "#00CFE8";
    private string _tabSelectedText = "#071014";
    private string _tabBorder = "#3A414D";
    private string _checkBoxText = "#F5F7FA";
    private string _checkBoxBackground = "#272C34";
    private string _checkBoxBorder = "#3A414D";
    private string _checkBoxCheckMark = "#00CFE8";
    private string _comboBoxBackground = "#272C34";
    private string _comboBoxText = "#F5F7FA";
    private string _comboBoxDropDownBackground = "#20252D";
    private string _comboBoxDropDownText = "#F5F7FA";
    private string _comboBoxHighlight = "#00CFE8";
    private string _comboBoxHighlightText = "#071014";
    private string _comboBoxBorder = "#3A414D";

    public string PresetName
    {
        get => _presetName;
        set => _presetName = value ?? "Atomic Cyan";
    }

    public string AppBackground
    {
        get => _appBackground;
        set => _appBackground = value ?? "#0E1116";
    }

    public string Surface
    {
        get => _surface;
        set => _surface = value ?? "#171B22";
    }

    public string Panel
    {
        get => _panel;
        set => _panel = value ?? "#20252D";
    }

    public string PanelAlt
    {
        get => _panelAlt;
        set => _panelAlt = value ?? "#242931";
    }

    public string Input
    {
        get => _input;
        set => _input = value ?? "#272C34";
    }

    public string Border
    {
        get => _border;
        set => _border = value ?? "#3A414D";
    }

    public string PrimaryText
    {
        get => _primaryText;
        set => _primaryText = value ?? "#F5F7FA";
    }

    public string SecondaryText
    {
        get => _secondaryText;
        set => _secondaryText = value ?? "#9FA8B8";
    }

    public string MutedText
    {
        get => _mutedText;
        set => _mutedText = value ?? "#AEB6C3";
    }

    public string Accent
    {
        get => _accent;
        set => _accent = value ?? "#00CFE8";
    }

    public string AccentText
    {
        get => _accentText;
        set => _accentText = value ?? "#071014";
    }

    // Input fields are intentionally separate from general panel/text colors.
    // This prevents Windows/WPF defaults or a custom theme from creating
    // unreadable light-on-light or dark-on-dark edit fields.
    public string InputText
    {
        get => _inputText;
        set => _inputText = value ?? "#F5F7FA";
    }

    public string InputBorder
    {
        get => _inputBorder;
        set => _inputBorder = value ?? "#3A414D";
    }

    // DataGrid/table colors are fully themeable because WPF's platform default
    // column headers/cells can otherwise ignore a dark application theme.
    public string DataGridBackground
    {
        get => _dataGridBackground;
        set => _dataGridBackground = value ?? "#20252D";
    }

    public string DataGridAlternateBackground
    {
        get => _dataGridAlternateBackground;
        set => _dataGridAlternateBackground = value ?? "#242931";
    }

    public string DataGridText
    {
        get => _dataGridText;
        set => _dataGridText = value ?? "#F5F7FA";
    }

    public string DataGridHeaderBackground
    {
        get => _dataGridHeaderBackground;
        set => _dataGridHeaderBackground = value ?? "#171B22";
    }

    public string DataGridHeaderText
    {
        get => _dataGridHeaderText;
        set => _dataGridHeaderText = value ?? "#F5F7FA";
    }

    public string DataGridSelectedBackground
    {
        get => _dataGridSelectedBackground;
        set => _dataGridSelectedBackground = value ?? "#00CFE8";
    }

    public string DataGridSelectedText
    {
        get => _dataGridSelectedText;
        set => _dataGridSelectedText = value ?? "#071014";
    }

    public string DataGridGridLine
    {
        get => _dataGridGridLine;
        set => _dataGridGridLine = value ?? "#3A414D";
    }

    // Tab headers are explicitly themed so section navigation remains readable
    // regardless of Windows accent or high-contrast defaults.
    public string TabHeaderBackground
    {
        get => _tabHeaderBackground;
        set => _tabHeaderBackground = value ?? "#20252D";
    }

    public string TabHeaderText
    {
        get => _tabHeaderText;
        set => _tabHeaderText = value ?? "#F5F7FA";
    }

    public string TabSelectedBackground
    {
        get => _tabSelectedBackground;
        set => _tabSelectedBackground = value ?? "#00CFE8";
    }

    public string TabSelectedText
    {
        get => _tabSelectedText;
        set => _tabSelectedText = value ?? "#071014";
    }

    public string TabBorder
    {
        get => _tabBorder;
        set => _tabBorder = value ?? "#3A414D";
    }

    // CheckBox text/glyph colors are explicit. WPF's platform CheckBox theme
    // can otherwise render content using a system color that clashes with a
    // custom ADT theme.
    public string CheckBoxText
    {
        get => _checkBoxText;
        set => _checkBoxText = value ?? "#F5F7FA";
    }

    public string CheckBoxBackground
    {
        get => _checkBoxBackground;
        set => _checkBoxBackground = value ?? "#272C34";
    }

    public string CheckBoxBorder
    {
        get => _checkBoxBorder;
        set => _checkBoxBorder = value ?? "#3A414D";
    }

    public string CheckBoxCheckMark
    {
        get => _checkBoxCheckMark;
        set => _checkBoxCheckMark = value ?? "#00CFE8";
    }

    // ComboBox/dropdown colors are intentionally separate from the rest of
    // the theme. This prevents a good-looking panel theme from accidentally
    // producing unreadable Windows dropdown popups.
    public string ComboBoxBackground
    {
        get => _comboBoxBackground;
        set => _comboBoxBackground = value ?? "#272C34";
    }

    public string ComboBoxText
    {
        get => _comboBoxText;
        set => _comboBoxText = value ?? "#F5F7FA";
    }

    public string ComboBoxDropDownBackground
    {
        get => _comboBoxDropDownBackground;
        set => _comboBoxDropDownBackground = value ?? "#20252D";
    }

    public string ComboBoxDropDownText
    {
        get => _comboBoxDropDownText;
        set => _comboBoxDropDownText = value ?? "#F5F7FA";
    }

    public string ComboBoxHighlight
    {
        get => _comboBoxHighlight;
        set => _comboBoxHighlight = value ?? "#00CFE8";
    }

    public string ComboBoxHighlightText
    {
        get => _comboBoxHighlightText;
        set => _comboBoxHighlightText = value ?? "#071014";
    }

    public string ComboBoxBorder
    {
        get => _comboBoxBorder;
        set => _comboBoxBorder = value ?? "#3A414D";
    }
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

    public static IReadOnlyList<ThemeSettings> Presets { get; } =
        new List<ThemeSettings>
        {
            Preset(
                "Atomic Cyan",
                "#0E1116",
                "#171B22",
                "#20252D",
                "#242931",
                "#272C34",
                "#3A414D",
                "#F5F7FA",
                "#9FA8B8",
                "#AEB6C3",
                "#00CFE8",
                "#071014"),

            Preset(
                "Drift Orange",
                "#100E0C",
                "#1D1814",
                "#282019",
                "#33271E",
                "#30261E",
                "#59412D",
                "#FFF7EE",
                "#CDB9A7",
                "#BDA590",
                "#FF8A1F",
                "#180B00"),

            Preset(
                "Neon Purple",
                "#0E0B14",
                "#191322",
                "#241A31",
                "#2E213D",
                "#2B2037",
                "#513B69",
                "#FBF5FF",
                "#C4B2D5",
                "#AF98C3",
                "#B66CFF",
                "#12051D"),

            Preset(
                "Race Red",
                "#110D0E",
                "#1D1517",
                "#291C1F",
                "#332226",
                "#302023",
                "#5B343C",
                "#FFF5F6",
                "#D0B2B7",
                "#BE9AA1",
                "#FF4D67",
                "#190307"),

            Preset(
                "Ice Blue",
                "#0B1116",
                "#121D25",
                "#192832",
                "#21333E",
                "#1D2D37",
                "#345467",
                "#F3FAFF",
                "#AFC8D7",
                "#94B3C5",
                "#61D4FF",
                "#041118"),

            Preset(
                "Monochrome",
                "#101010",
                "#191919",
                "#222222",
                "#2A2A2A",
                "#262626",
                "#454545",
                "#F4F4F4",
                "#B8B8B8",
                "#A1A1A1",
                "#E8E8E8",
                "#111111")
        };

    public static ThemeSettings Clone(ThemeSettings theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return new ThemeSettings
        {
            PresetName = theme.PresetName,
            AppBackground = theme.AppBackground,
            Surface = theme.Surface,
            Panel = theme.Panel,
            PanelAlt = theme.PanelAlt,
            Input = theme.Input,
            Border = theme.Border,
            PrimaryText = theme.PrimaryText,
            SecondaryText = theme.SecondaryText,
            MutedText = theme.MutedText,
            Accent = theme.Accent,
            AccentText = theme.AccentText,

            InputText = theme.InputText,
            InputBorder = theme.InputBorder,

            DataGridBackground = theme.DataGridBackground,
            DataGridAlternateBackground = theme.DataGridAlternateBackground,
            DataGridText = theme.DataGridText,
            DataGridHeaderBackground = theme.DataGridHeaderBackground,
            DataGridHeaderText = theme.DataGridHeaderText,
            DataGridSelectedBackground = theme.DataGridSelectedBackground,
            DataGridSelectedText = theme.DataGridSelectedText,
            DataGridGridLine = theme.DataGridGridLine,

            TabHeaderBackground = theme.TabHeaderBackground,
            TabHeaderText = theme.TabHeaderText,
            TabSelectedBackground = theme.TabSelectedBackground,
            TabSelectedText = theme.TabSelectedText,
            TabBorder = theme.TabBorder,

            CheckBoxText = theme.CheckBoxText,
            CheckBoxBackground = theme.CheckBoxBackground,
            CheckBoxBorder = theme.CheckBoxBorder,
            CheckBoxCheckMark = theme.CheckBoxCheckMark,

            ComboBoxBackground = theme.ComboBoxBackground,
            ComboBoxText = theme.ComboBoxText,
            ComboBoxDropDownBackground = theme.ComboBoxDropDownBackground,
            ComboBoxDropDownText = theme.ComboBoxDropDownText,
            ComboBoxHighlight = theme.ComboBoxHighlight,
            ComboBoxHighlightText = theme.ComboBoxHighlightText,
            ComboBoxBorder = theme.ComboBoxBorder
        };
    }
}