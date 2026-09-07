# v0.8.3-beta.1 UI and icon preview

Local preview; not yet a published GitHub release.

## Changes

- Dashboard cards form fewer columns as the window narrows. Main window minimum is now 560×480 logical units.
- Navigation collapses below 900 logical units, with a persistent menu button. On narrow screens it overlays content; on wider screens its divider can be dragged to resize it.
- Dashboard Save Profile remains above the scrolling page. Embedded tool views hide the dashboard actions from keyboard navigation.
- Car setup keeps Save for This Car and Save ADT Setup in a separate footer. Baseline controls and behavior sliders stack on narrow screens.
- Setup, telemetry, assistant, share, AZOM, appearance, and update views use bounded scrolling and wrapping action groups. Wide result tables scroll horizontally.
- Embedded pages retain their styles and named-control bindings when moved into the main workspace.
- Per-monitor DPI awareness and monitor work-area sizing support portrait screens and moving the window between differently scaled monitors.
- New ADT application, title-bar, shortcut, and installer icon based on the supplied artwork.

Saving and hardware write rules are retained. Save ADT Setup remains disabled until a valid generated setup is available; changing its inputs still requires regeneration.

## Validation

App Release build succeeded with zero warnings/errors. Installer and self-contained portable packages were built. Network dependency audits were rerun successfully after the restricted build could not initially reach NuGet.

Layout suite: 156 geometry assertions across ten tool pages, including a 430×300 embedded workspace, portrait and ultrawide layouts, all tab selections, card reflow, and dashboard Save visibility after scrolling. It loads production XAML and styles without business event handlers; this is not a claim that every live interaction or physical monitor transition has been tested.

Existing regression suite: 18 scenarios passed, including 8,000 built-in tuning combinations.

## Try on both monitors

1. Open the preview and move it between your portrait and ultrawide displays; resize and maximize it on each.
2. Use the menu button to hide/show navigation. On the ultrawide, drag the navigation divider.
3. Open AC Setup and expand Desired Car Behavior. Confirm Save for This Car stays reachable while the controls scroll.
4. Load a baseline, generate recommendations, and confirm Save ADT Setup remains visible. The original baseline is still preserved.
5. Check Dashboard Save Profile, recording/session save, and the other tools at your usual Windows scaling.

Report the page, window size, monitor scaling, and any obscured control before publishing this preview to testers. No real wheelbase Apply/Revert or installed SimHub bridge replacement was performed during this UI pass.
