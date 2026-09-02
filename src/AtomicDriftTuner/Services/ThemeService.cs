using System.Windows;
using System.Windows.Media;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class ThemeService
{
    private static readonly (string Key, Func<ThemeSettings, string> Value)[] Map =
    [
        ("AppBackgroundBrush", t => t.AppBackground),
        ("SurfaceBrush", t => t.Surface),
        ("PanelBrush", t => t.Panel),
        ("PanelAltBrush", t => t.PanelAlt),
        ("InputBrush", t => t.Input),
        ("BorderBrush", t => t.Border),
        ("PrimaryTextBrush", t => t.PrimaryText),
        ("SecondaryTextBrush", t => t.SecondaryText),
        ("MutedTextBrush", t => t.MutedText),
        ("AccentBrush", t => t.Accent),
        ("AccentTextBrush", t => t.AccentText),
        ("InputTextBrush", t => t.InputText),
        ("InputBorderBrush", t => t.InputBorder),
        ("DataGridBackgroundBrush", t => t.DataGridBackground),
        ("DataGridAlternateBackgroundBrush", t => t.DataGridAlternateBackground),
        ("DataGridTextBrush", t => t.DataGridText),
        ("DataGridHeaderBackgroundBrush", t => t.DataGridHeaderBackground),
        ("DataGridHeaderTextBrush", t => t.DataGridHeaderText),
        ("DataGridSelectedBackgroundBrush", t => t.DataGridSelectedBackground),
        ("DataGridSelectedTextBrush", t => t.DataGridSelectedText),
        ("DataGridGridLineBrush", t => t.DataGridGridLine),
        ("TabHeaderBackgroundBrush", t => t.TabHeaderBackground),
        ("TabHeaderTextBrush", t => t.TabHeaderText),
        ("TabSelectedBackgroundBrush", t => t.TabSelectedBackground),
        ("TabSelectedTextBrush", t => t.TabSelectedText),
        ("TabBorderBrush", t => t.TabBorder),
        ("CheckBoxTextBrush", t => t.CheckBoxText),
        ("CheckBoxBackgroundBrush", t => t.CheckBoxBackground),
        ("CheckBoxBorderBrush", t => t.CheckBoxBorder),
        ("CheckBoxCheckMarkBrush", t => t.CheckBoxCheckMark),
        ("ComboBoxBackgroundBrush", t => t.ComboBoxBackground),
        ("ComboBoxTextBrush", t => t.ComboBoxText),
        ("ComboBoxDropDownBackgroundBrush", t => t.ComboBoxDropDownBackground),
        ("ComboBoxDropDownTextBrush", t => t.ComboBoxDropDownText),
        ("ComboBoxHighlightBrush", t => t.ComboBoxHighlight),
        ("ComboBoxHighlightTextBrush", t => t.ComboBoxHighlightText),
        ("ComboBoxBorderBrush", t => t.ComboBoxBorder)
    ];

    public static void Apply(ThemeSettings theme)
    {
        if (Application.Current == null) return;
        Validate(theme);
        foreach (var (key, get) in Map)
            Application.Current.Resources[key] = MakeBrush(get(theme));
    }

    public static void Validate(ThemeSettings theme)
    {
        foreach (var (_, get) in Map)
            _ = ParseThemeColor(get(theme));
    }

    public static string NormalizeHex(string value) => ToHex(ParseThemeColor(value));

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color ParseThemeColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Theme colors cannot be blank.");
        try
        {
            var obj = ColorConverter.ConvertFromString(value.Trim());
            if (obj is Color color) return color;
        }
        catch { }
        throw new InvalidOperationException($"'{value}' is not a valid color. Use #RRGGBB, for example #00CFE8.");
    }

    public static double ContrastRatio(string foreground, string background)
    {
        static double Linear(byte b)
        {
            var x = b / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) =>
            0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        var a = Luminance(ParseThemeColor(foreground));
        var b = Luminance(ParseThemeColor(background));
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static SolidColorBrush MakeBrush(string value)
    {
        var brush = new SolidColorBrush(ParseThemeColor(value));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
