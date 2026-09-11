namespace AtomicDriftTuner.Models;

public sealed partial class ThemeSettings
{
    private string? _SectionHeading;
    public string SectionHeading { get => _SectionHeading ?? PrimaryText; set => _SectionHeading = value; }
    private string? _FieldLabel;
    public string FieldLabel { get => _FieldLabel ?? PrimaryText; set => _FieldLabel = value; }
    private string? _ExpanderHeader;
    public string ExpanderHeader { get => _ExpanderHeader ?? PrimaryText; set => _ExpanderHeader = value; }
    private string? _ExpanderGlyph;
    public string ExpanderGlyph { get => _ExpanderGlyph ?? Accent; set => _ExpanderGlyph = value; }
    private string? _ButtonBackground;
    public string ButtonBackground { get => _ButtonBackground ?? PanelAlt; set => _ButtonBackground = value; }
    private string? _ButtonText;
    public string ButtonText { get => _ButtonText ?? PrimaryText; set => _ButtonText = value; }
    private string? _ButtonBorder;
    public string ButtonBorder { get => _ButtonBorder ?? Border; set => _ButtonBorder = value; }
    private string? _ControlHover;
    public string ControlHover { get => _ControlHover ?? PanelAlt; set => _ControlHover = value; }
    private string? _ButtonHoverText;
    public string ButtonHoverText { get => _ButtonHoverText ?? PrimaryText; set => _ButtonHoverText = value; }
    private string? _ButtonPressed;
    public string ButtonPressed { get => _ButtonPressed ?? Input; set => _ButtonPressed = value; }
    private string? _DisabledText;
    public string DisabledText { get => _DisabledText ?? MutedText; set => _DisabledText = value; }
    private string? _DisabledBackground;
    public string DisabledBackground { get => _DisabledBackground ?? PanelAlt; set => _DisabledBackground = value; }
    private string? _FocusBorder;
    public string FocusBorder { get => _FocusBorder ?? Accent; set => _FocusBorder = value; }
    private string? _ScrollTrack;
    public string ScrollTrack { get => _ScrollTrack ?? AppBackground; set => _ScrollTrack = value; }
    private string? _ScrollThumb;
    public string ScrollThumb { get => _ScrollThumb ?? Border; set => _ScrollThumb = value; }
    private string? _ScrollThumbHover;
    public string ScrollThumbHover { get => _ScrollThumbHover ?? SecondaryText; set => _ScrollThumbHover = value; }
    private string? _ScrollThumbActive;
    public string ScrollThumbActive { get => _ScrollThumbActive ?? Accent; set => _ScrollThumbActive = value; }
    private string? _SliderTrack;
    public string SliderTrack { get => _SliderTrack ?? Input; set => _SliderTrack = value; }
    private string? _SliderFill;
    public string SliderFill { get => _SliderFill ?? Accent; set => _SliderFill = value; }
    private string? _SliderThumb;
    public string SliderThumb { get => _SliderThumb ?? Accent; set => _SliderThumb = value; }
    private string? _SliderThumbBorder;
    public string SliderThumbBorder { get => _SliderThumbBorder ?? Surface; set => _SliderThumbBorder = value; }
    private string? _InputSelection;
    public string InputSelection { get => _InputSelection ?? Accent; set => _InputSelection = value; }
    private string? _InputSelectionText;
    public string InputSelectionText { get => _InputSelectionText ?? AccentText; set => _InputSelectionText = value; }
    private string? _InputCaret;
    public string InputCaret { get => _InputCaret ?? InputText; set => _InputCaret = value; }
    private string? _TooltipBackground;
    public string TooltipBackground { get => _TooltipBackground ?? Panel; set => _TooltipBackground = value; }
    private string? _TooltipText;
    public string TooltipText { get => _TooltipText ?? PrimaryText; set => _TooltipText = value; }
    private string? _TooltipBorder;
    public string TooltipBorder { get => _TooltipBorder ?? Border; set => _TooltipBorder = value; }
    private string? _MenuBackground;
    public string MenuBackground { get => _MenuBackground ?? Surface; set => _MenuBackground = value; }
    private string? _MenuText;
    public string MenuText { get => _MenuText ?? PrimaryText; set => _MenuText = value; }
    private string? _MenuHighlight;
    public string MenuHighlight { get => _MenuHighlight ?? Accent; set => _MenuHighlight = value; }
    private string? _MenuHighlightText;
    public string MenuHighlightText { get => _MenuHighlightText ?? AccentText; set => _MenuHighlightText = value; }
    private string? _StatusGood;
    public string StatusGood { get => _StatusGood ?? "#6BE585"; set => _StatusGood = value; }
    private string? _StatusWarning;
    public string StatusWarning { get => _StatusWarning ?? "#FFD36B"; set => _StatusWarning = value; }
    private string? _StatusError;
    public string StatusError { get => _StatusError ?? "#FF657A"; set => _StatusError = value; }
    private string? _ColorPickerIndicator;
    public string ColorPickerIndicator { get => _ColorPickerIndicator ?? PrimaryText; set => _ColorPickerIndicator = value; }
}

/// <summary>One catalog drives validation, resources, cloning and the additional color editors.</summary>
public static class AdditionalThemeColors
{
    public sealed record Option(string Name, string Label, Func<ThemeSettings, string> Get, Action<ThemeSettings, string> Set)
    {
        public string ResourceKey => Name + "Brush";
        public string EditorName => Name + "ColorBox";
    }
    public static IReadOnlyList<Option> All { get; } =
    [
        new(nameof(ThemeSettings.SectionHeading), "Section headings (Core Settings, Effects, etc.)", t => t.SectionHeading, (t, v) => t.SectionHeading = v),
        new(nameof(ThemeSettings.FieldLabel), "Field labels / body text", t => t.FieldLabel, (t, v) => t.FieldLabel = v),
        new(nameof(ThemeSettings.ExpanderHeader), "Expandable section text", t => t.ExpanderHeader, (t, v) => t.ExpanderHeader = v),
        new(nameof(ThemeSettings.ExpanderGlyph), "Expand / collapse arrow", t => t.ExpanderGlyph, (t, v) => t.ExpanderGlyph = v),
        new(nameof(ThemeSettings.ButtonBackground), "Button background", t => t.ButtonBackground, (t, v) => t.ButtonBackground = v),
        new(nameof(ThemeSettings.ButtonText), "Button text", t => t.ButtonText, (t, v) => t.ButtonText = v),
        new(nameof(ThemeSettings.ButtonBorder), "Button border", t => t.ButtonBorder, (t, v) => t.ButtonBorder = v),
        new(nameof(ThemeSettings.ControlHover), "Button hover background", t => t.ControlHover, (t, v) => t.ControlHover = v),
        new(nameof(ThemeSettings.ButtonHoverText), "Button hover text", t => t.ButtonHoverText, (t, v) => t.ButtonHoverText = v),
        new(nameof(ThemeSettings.ButtonPressed), "Button pressed background", t => t.ButtonPressed, (t, v) => t.ButtonPressed = v),
        new(nameof(ThemeSettings.DisabledText), "Disabled control text", t => t.DisabledText, (t, v) => t.DisabledText = v),
        new(nameof(ThemeSettings.DisabledBackground), "Disabled control background", t => t.DisabledBackground, (t, v) => t.DisabledBackground = v),
        new(nameof(ThemeSettings.FocusBorder), "Keyboard focus / hover outline", t => t.FocusBorder, (t, v) => t.FocusBorder = v),
        new(nameof(ThemeSettings.ScrollTrack), "Scrollbar track", t => t.ScrollTrack, (t, v) => t.ScrollTrack = v),
        new(nameof(ThemeSettings.ScrollThumb), "Scrollbar handle", t => t.ScrollThumb, (t, v) => t.ScrollThumb = v),
        new(nameof(ThemeSettings.ScrollThumbHover), "Scrollbar handle hover", t => t.ScrollThumbHover, (t, v) => t.ScrollThumbHover = v),
        new(nameof(ThemeSettings.ScrollThumbActive), "Scrollbar handle dragging", t => t.ScrollThumbActive, (t, v) => t.ScrollThumbActive = v),
        new(nameof(ThemeSettings.SliderTrack), "Slider track", t => t.SliderTrack, (t, v) => t.SliderTrack = v),
        new(nameof(ThemeSettings.SliderFill), "Slider filled track", t => t.SliderFill, (t, v) => t.SliderFill = v),
        new(nameof(ThemeSettings.SliderThumb), "Slider handle", t => t.SliderThumb, (t, v) => t.SliderThumb = v),
        new(nameof(ThemeSettings.SliderThumbBorder), "Slider handle border", t => t.SliderThumbBorder, (t, v) => t.SliderThumbBorder = v),
        new(nameof(ThemeSettings.InputSelection), "Selected input text background", t => t.InputSelection, (t, v) => t.InputSelection = v),
        new(nameof(ThemeSettings.InputSelectionText), "Selected input text color", t => t.InputSelectionText, (t, v) => t.InputSelectionText = v),
        new(nameof(ThemeSettings.InputCaret), "Text cursor", t => t.InputCaret, (t, v) => t.InputCaret = v),
        new(nameof(ThemeSettings.TooltipBackground), "Tooltip background", t => t.TooltipBackground, (t, v) => t.TooltipBackground = v),
        new(nameof(ThemeSettings.TooltipText), "Tooltip text", t => t.TooltipText, (t, v) => t.TooltipText = v),
        new(nameof(ThemeSettings.TooltipBorder), "Tooltip border", t => t.TooltipBorder, (t, v) => t.TooltipBorder = v),
        new(nameof(ThemeSettings.MenuBackground), "Context menu background", t => t.MenuBackground, (t, v) => t.MenuBackground = v),
        new(nameof(ThemeSettings.MenuText), "Context menu text", t => t.MenuText, (t, v) => t.MenuText = v),
        new(nameof(ThemeSettings.MenuHighlight), "Context menu highlight", t => t.MenuHighlight, (t, v) => t.MenuHighlight = v),
        new(nameof(ThemeSettings.MenuHighlightText), "Context menu selected text", t => t.MenuHighlightText, (t, v) => t.MenuHighlightText = v),
        new(nameof(ThemeSettings.StatusGood), "Remote success / connected status", t => t.StatusGood, (t, v) => t.StatusGood = v),
        new(nameof(ThemeSettings.StatusWarning), "Remote warning status", t => t.StatusWarning, (t, v) => t.StatusWarning = v),
        new(nameof(ThemeSettings.StatusError), "Remote error / stop status", t => t.StatusError, (t, v) => t.StatusError = v),
        new(nameof(ThemeSettings.ColorPickerIndicator), "Color wheel selection ring", t => t.ColorPickerIndicator, (t, v) => t.ColorPickerIndicator = v),
    ];
}
