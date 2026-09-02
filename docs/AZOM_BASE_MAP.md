# AZOM Base Page Map — v0.5.0

This map was created from two sets of three screenshots: one set with sliders at their lowest positions and one set with sliders at their highest positions.

## Confirmed slider ranges

| Section | Setting | Min shown | Max shown |
|---|---|---:|---:|
| Core | Wheel Rotation Angle | 60° | 2700° |
| Core | Game FFB Strength | 0% | 100% |
| Core | Base Torque Output | 50% | 100% |
| Core | Maximum Wheel Speed | 0% | 200% |
| Core | Interpolation | 0 | 10 |
| Gearshift Vibration | Shift Intensity | 0 | 5 |
| Gearshift Vibration | Shift Debounce | 0 ms | 1000 ms |
| Wheelbase Effects | Wheel Damper | 0% | 100% |
| Wheelbase Effects | Wheel Friction | 0% | 100% |
| Wheelbase Effects | Natural Inertia | 100 | 500 |
| Wheelbase Effects | Wheel Spring | 0% | 100% |
| Game Effects | Game Damper | 0% | 100% |
| Game Effects | Game Friction | 0% | 100% |
| Game Effects | Game Inertia | 0% | 100% |
| Game Effects | Game Spring | 0% | 100% |
| Protection | Steering Wheel Inertia | 100 | 4000 |
| Soft Limit | Stiffness | 1 | 10 |
| High Speed Damping | Damping Level | 0% | 100% |
| High Speed Damping | Trigger Speed | 0 kph | 400 kph |

## Boolean controls observed

- Vibrate on Neutral
- Hands-Off Protection
- Retain Game FFB
- Force Feedback Reversal
- Standby Mode
- Base Status LED
- Bluetooth

The screenshots establish that these controls exist. They are not treated as per-car tuning sliders; Atomic stores them as user preferences.

## FFB Equalizer

Bands observed:
- 10 Hz
- 15 Hz
- 25 Hz
- 40 Hz
- 60 Hz
- 100 Hz

The graph visibly spans 0 to 400, and AZOM labels `100% = neutral` and `400% = max boost`. Atomic therefore models each equalizer band as 0–400%.

Sensitivity choices observed: 0 through 10.

## FFB Output Curve

Five nodes are shown at input positions 20, 40, 60, 80 and 100. The screenshot's linear curve is 20→20, 40→40, 60→60, 80→80, 100→100.

Preset buttons observed:
- Linear
- S Curve
- Exponential
- Parabolic

Atomic v0.5.0 represents all four presets but generates Linear by default for drifting.

## Not yet fully enumerated

`Standby after` is a dropdown. The screenshots confirm `Disabled`, but do not expose every dropdown choice. Atomic therefore stores this as a preference string rather than inventing a fixed enum.

`Restart Wheelbase` is an action button, not a tune value, so it is intentionally not part of `AzomSettings`.


## Live integration

v0.5.0 maps the settings with documented `AZOM.*` properties/actions where available. See `AZOM_LIVE_INTEGRATION.md`. Controls that are not publicly exposed are preserved/manual rather than guessed.
