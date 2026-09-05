using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class ThemeService
{
    private static readonly (
        string ResourceKey,
        string SettingName,
        Func<ThemeSettings, string> Value)[] Map =
    [
        ("AppBackgroundBrush", "AppBackground", t => t.AppBackground),
        ("SurfaceBrush", "Surface", t => t.Surface),
        ("PanelBrush", "Panel", t => t.Panel),
        ("PanelAltBrush", "PanelAlt", t => t.PanelAlt),
        ("InputBrush", "Input", t => t.Input),
        ("BorderBrush", "Border", t => t.Border),

        ("PrimaryTextBrush", "PrimaryText", t => t.PrimaryText),
        ("SecondaryTextBrush", "SecondaryText", t => t.SecondaryText),
        ("MutedTextBrush", "MutedText", t => t.MutedText),

        ("AccentBrush", "Accent", t => t.Accent),
        ("AccentTextBrush", "AccentText", t => t.AccentText),

        ("InputTextBrush", "InputText", t => t.InputText),
        ("InputBorderBrush", "InputBorder", t => t.InputBorder),

        ("DataGridBackgroundBrush", "DataGridBackground", t => t.DataGridBackground),
        ("DataGridAlternateBackgroundBrush", "DataGridAlternateBackground", t => t.DataGridAlternateBackground),
        ("DataGridTextBrush", "DataGridText", t => t.DataGridText),
        ("DataGridHeaderBackgroundBrush", "DataGridHeaderBackground", t => t.DataGridHeaderBackground),
        ("DataGridHeaderTextBrush", "DataGridHeaderText", t => t.DataGridHeaderText),
        ("DataGridSelectedBackgroundBrush", "DataGridSelectedBackground", t => t.DataGridSelectedBackground),
        ("DataGridSelectedTextBrush", "DataGridSelectedText", t => t.DataGridSelectedText),
        ("DataGridGridLineBrush", "DataGridGridLine", t => t.DataGridGridLine),

        ("TabHeaderBackgroundBrush", "TabHeaderBackground", t => t.TabHeaderBackground),
        ("TabHeaderTextBrush", "TabHeaderText", t => t.TabHeaderText),
        ("TabSelectedBackgroundBrush", "TabSelectedBackground", t => t.TabSelectedBackground),
        ("TabSelectedTextBrush", "TabSelectedText", t => t.TabSelectedText),
        ("TabBorderBrush", "TabBorder", t => t.TabBorder),

        ("CheckBoxTextBrush", "CheckBoxText", t => t.CheckBoxText),
        ("CheckBoxBackgroundBrush", "CheckBoxBackground", t => t.CheckBoxBackground),
        ("CheckBoxBorderBrush", "CheckBoxBorder", t => t.CheckBoxBorder),
        ("CheckBoxCheckMarkBrush", "CheckBoxCheckMark", t => t.CheckBoxCheckMark),

        ("ComboBoxBackgroundBrush", "ComboBoxBackground", t => t.ComboBoxBackground),
        ("ComboBoxTextBrush", "ComboBoxText", t => t.ComboBoxText),
        ("ComboBoxDropDownBackgroundBrush", "ComboBoxDropDownBackground", t => t.ComboBoxDropDownBackground),
        ("ComboBoxDropDownTextBrush", "ComboBoxDropDownText", t => t.ComboBoxDropDownText),
        ("ComboBoxHighlightBrush", "ComboBoxHighlight", t => t.ComboBoxHighlight),
        ("ComboBoxHighlightTextBrush", "ComboBoxHighlightText", t => t.ComboBoxHighlightText),
        ("ComboBoxBorderBrush", "ComboBoxBorder", t => t.ComboBoxBorder)
    ];

    public static void Apply(
        ThemeSettings theme)
    {
        ArgumentNullException.ThrowIfNull(
            theme);

        var application =
            Application.Current;

        if (application is null)
        {
            return;
        }

        // Build the complete brush set before mutating application resources.
        //
        // This prevents a malformed theme from leaving ADT in a half-applied
        // state where some controls use the new theme and others use the old
        // theme.
        var brushes =
            BuildBrushSet(
                theme);

        foreach (var item in brushes)
        {
            application.Resources[item.ResourceKey] =
                item.Brush;
        }
    }

    public static void Validate(
        ThemeSettings theme)
    {
        ArgumentNullException.ThrowIfNull(
            theme);

        foreach (var entry in Map)
        {
            var value =
                entry.Value(
                    theme);

            try
            {
                _ = ParseThemeColor(
                    value);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Theme setting '{entry.SettingName}' is invalid: {ex.Message}",
                    ex);
            }
        }
    }

    public static string NormalizeHex(
        string value)
    {
        return ToHex(
            ParseThemeColor(
                value));
    }

    public static string ToHex(
        Color color)
    {
        return color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static Color ParseThemeColor(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                "Theme colors cannot be blank.");
        }

        var normalized =
            value.Trim();

        if (!TryParseHexColor(
                normalized,
                out var color))
        {
            throw new InvalidOperationException(
                $"'{SanitizeColorValue(normalized)}' is not a valid theme color. Use #RRGGBB or #AARRGGBB, for example #00CFE8.");
        }

        return color;
    }

    public static double ContrastRatio(
        string foreground,
        string background)
    {
        var foregroundColor =
            ParseThemeColor(
                foreground);

        var backgroundColor =
            ParseThemeColor(
                background);

        // WCAG contrast is defined for displayed colors. If the foreground is
        // translucent, composite it over the supplied background first.
        var effectiveForeground =
            Composite(
                foregroundColor,
                backgroundColor);

        // There is no lower layer supplied for a translucent background.
        // Treat it as composited over opaque black so the calculation remains
        // deterministic rather than silently discarding alpha.
        var effectiveBackground =
            backgroundColor.A == 255
                ? backgroundColor
                : Composite(
                    backgroundColor,
                    Colors.Black);

        var foregroundLuminance =
            RelativeLuminance(
                effectiveForeground);

        var backgroundLuminance =
            RelativeLuminance(
                effectiveBackground);

        var lighter =
            Math.Max(
                foregroundLuminance,
                backgroundLuminance);

        var darker =
            Math.Min(
                foregroundLuminance,
                backgroundLuminance);

        return
            (lighter + 0.05) /
            (darker + 0.05);
    }

    private static List<ThemeBrushEntry> BuildBrushSet(
        ThemeSettings theme)
    {
        var brushes =
            new List<ThemeBrushEntry>(
                Map.Length);

        foreach (var entry in Map)
        {
            var value =
                entry.Value(
                    theme);

            Color color;

            try
            {
                color =
                    ParseThemeColor(
                        value);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Theme setting '{entry.SettingName}' is invalid: {ex.Message}",
                    ex);
            }

            var brush =
                new SolidColorBrush(
                    color);

            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            brushes.Add(
                new ThemeBrushEntry(
                    entry.ResourceKey,
                    brush));
        }

        return brushes;
    }

    private static bool TryParseHexColor(
        string value,
        out Color color)
    {
        color =
            default;

        if (
            value.Length != 7 &&
            value.Length != 9)
        {
            return false;
        }

        if (value[0] != '#')
        {
            return false;
        }

        try
        {
            if (value.Length == 7)
            {
                if (
                    !TryHexByte(
                        value.AsSpan(1, 2),
                        out var red) ||
                    !TryHexByte(
                        value.AsSpan(3, 2),
                        out var green) ||
                    !TryHexByte(
                        value.AsSpan(5, 2),
                        out var blue))
                {
                    return false;
                }

                color =
                    Color.FromArgb(
                        255,
                        red,
                        green,
                        blue);

                return true;
            }

            if (
                !TryHexByte(
                    value.AsSpan(1, 2),
                    out var alpha) ||
                !TryHexByte(
                    value.AsSpan(3, 2),
                    out var redArgb) ||
                !TryHexByte(
                    value.AsSpan(5, 2),
                    out var greenArgb) ||
                !TryHexByte(
                    value.AsSpan(7, 2),
                    out var blueArgb))
            {
                return false;
            }

            color =
                Color.FromArgb(
                    alpha,
                    redArgb,
                    greenArgb,
                    blueArgb);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryHexByte(
        ReadOnlySpan<char> value,
        out byte result)
    {
        return byte.TryParse(
            value,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static Color Composite(
        Color foreground,
        Color background)
    {
        var foregroundAlpha =
            foreground.A /
            255.0;

        var backgroundAlpha =
            background.A /
            255.0;

        var outputAlpha =
            foregroundAlpha +
            backgroundAlpha *
            (1.0 - foregroundAlpha);

        if (outputAlpha <=
            0.000001)
        {
            return Colors.Black;
        }

        static byte BlendChannel(
            byte foregroundChannel,
            byte backgroundChannel,
            double foregroundAlpha,
            double backgroundAlpha,
            double outputAlpha)
        {
            var value =
                (
                    foregroundChannel *
                    foregroundAlpha +
                    backgroundChannel *
                    backgroundAlpha *
                    (1.0 - foregroundAlpha)
                ) /
                outputAlpha;

            return
                (byte)Math.Clamp(
                    Math.Round(
                        value,
                        MidpointRounding.AwayFromZero),
                    0,
                    255);
        }

        return Color.FromArgb(
            (byte)Math.Clamp(
                Math.Round(
                    outputAlpha * 255.0,
                    MidpointRounding.AwayFromZero),
                0,
                255),

            BlendChannel(
                foreground.R,
                background.R,
                foregroundAlpha,
                backgroundAlpha,
                outputAlpha),

            BlendChannel(
                foreground.G,
                background.G,
                foregroundAlpha,
                backgroundAlpha,
                outputAlpha),

            BlendChannel(
                foreground.B,
                background.B,
                foregroundAlpha,
                backgroundAlpha,
                outputAlpha));
    }

    private static double RelativeLuminance(
        Color color)
    {
        static double Linear(
            byte component)
        {
            var value =
                component /
                255.0;

            return value <=
                   0.04045
                ? value /
                  12.92
                : Math.Pow(
                    (value + 0.055) /
                    1.055,
                    2.4);
        }

        return
            0.2126 *
            Linear(
                color.R) +
            0.7152 *
            Linear(
                color.G) +
            0.0722 *
            Linear(
                color.B);
    }

    private static string SanitizeColorValue(
        string value)
    {
        const int maximumLength =
            64;

        var cleaned =
            new string(
                value
                    .Where(
                        character =>
                            !char.IsControl(
                                character))
                    .Take(
                        maximumLength)
                    .ToArray());

        return cleaned;
    }

    private sealed record ThemeBrushEntry(
        string ResourceKey,
        SolidColorBrush Brush);
}
