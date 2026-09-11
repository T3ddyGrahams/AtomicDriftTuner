# Appearance and readability

Version 0.9.0-preview.3 adds 35 color controls, bringing the palette to 72 editable roles. Colors are shared by role across ADT's interface, rather than assigned separately to every individual label or button.

## Fix the dark headings in Core + Effects

1. Open **Appearance / Customize Appearance**.
2. Under **Headings + Control Details**, change **Section headings (Core Settings, Effects, etc.)**. You can also select this target in the color wheel's **Editing** list.
3. Valid edits preview immediately in open ADT screens. Choose **Save Theme** to keep them after restarting.

Closing Appearance restores the last saved theme. **Preview Default Theme** lets you try the default palette; save it only if you want to keep it. Invalid or unfinished hex values do not replace the active palette. Supported formats are `#RRGGBB` and `#AARRGGBB`.

## Which control changes which color?

| Element | Appearance control |
| --- | --- |
| Core Settings, Effects, Protection and other headings | Section headings |
| Body text and plain field labels | Field labels / body text |
| Secondary notes and dashboard field captions | Secondary Text / Muted Text |
| Expandable section header and arrow | Expandable section text / Expand-collapse arrow |
| Ordinary buttons and their states | Button background, text, border, hover, pressed, disabled |
| Primary action buttons | Accent / Accent Text; button state colors on hover or press |
| Scrollbar track and handle | Scrollbar controls, including hover and dragging |
| Sliders and progress indicators | Slider track / filled track / handle / border |
| Input selection and cursor | Selected input text background / color, Text cursor |
| Tooltips and right-click menus | Tooltip / Context menu controls |
| Tables, dropdowns, checkboxes and tab headers | Existing dedicated color groups |
| Remote connected / warning / error indicators | Remote status colors |

The selected tab's label color no longer colors the contents of the tab. Wrapped checkbox labels retain their checkbox text color. Table resize grips no longer draw system-white blocks.

Older saved themes retain their existing colors. Missing new roles derive from that theme's text, accent, surface and control colors; there is no theme reset. Newly customized roles are independent of their original fallback colors.

The Remote companion uses the saved palette when its page is refreshed. The desktop previews update immediately; Remote follows saved changes. The color wheel's spectrum and logo are artwork/data, not theme colors. Windows-managed title bars, file dialogs and message boxes remain controlled by Windows.

The readability check flags low contrast; it does not stop you choosing a color. Check the screens you use after making a custom palette, especially disabled controls and selected text.

Validation covers palette migration, all additional resources, live heading updates, separate selected-tab text, checkbox labels, expander operation, actual Appearance editing/save/reopen/cancel using isolated settings, invalid-input handling, and Remote CSS color validation. Layout checks cover narrow, portrait, short landscape and ultrawide sizes. Physical-monitor, keyboard and live-driving acceptance remain manual checks.
