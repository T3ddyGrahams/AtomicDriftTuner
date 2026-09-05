🏁 Atomic Drift Tuner — Roadmap

Atomic Drift Tuner (ADT) is being built to make drift tuning in Assetto Corsa more understandable, repeatable, and data-driven.

The goal isn’t simply to generate setup numbers.

ADT should eventually be able to take a driver from:

“Something about this car feels wrong.”

to:

“ADT identified what’s happening, explained why, recommended a change based on this car and how I want it to drive, and verified whether the change actually made it better.”

ADT is currently in public beta, so priorities may change as real-world testing exposes new problems and opportunities.

⸻

📌 Status

Status	Meaning
🔴 Now	Active / highest priority
🟠 Next	Planned for upcoming development
🟡 Upcoming	Planned after core intelligence work
🔵 Planned	Larger feature planned for later
🟣 Future	Longer-term development
🧪 Experimental	Research / not committed
✅ Shipped	Available in ADT

⸻

🔴 Now — Public Beta

Beta Testing & Validation

The current priority is proving ADT with real drivers, hardware, cars, and drift packs.

* Expand external beta testing
* Test additional wheelbases and rims
* Test additional drift packs and cars
* Validate AC setup recommendations
* Validate FFB recommendations
* Validate AZOM recommendations
* Validate Desired Behavior across different driving styles
* Identify hardware-specific behavior
* Identify pack/car-specific behavior
* Collect structured before/after feedback

Tester Program

Build a more organized testing process including:

* Tester instructions
* Standardized testing procedure
* Hardware information
* Bug reports
* Tuning feedback
* Before/after ratings
* Known issues
* Tester/contributor recognition

⸻

Stability & Reliability

Before adding large systems, ADT needs a strong foundation.

Current areas of focus:

* Crash fixes
* Telemetry reliability
* Profile loading/saving
* Configuration migration
* Clean-install testing
* Portable-build testing
* Better error handling
* Better diagnostics
* Corrupted configuration protection
* SimHub bridge reliability
* AZOM compatibility and safety

⸻

Hardware Validation

ADT will gradually build a compatibility matrix covering:

Wheelbase → Rim → SimHub → AZOM → ADT

Possible compatibility states:

* Verified
* Community Verified
* Partially Tested
* Experimental
* Known Issue
* Unsupported

⸻

🟠 Next — Smarter Tuning

Telemetry Intelligence 2.0

Improve ADT’s ability to recognize actual drift behavior.

Target detection includes:

* Understeer
* Oversteer
* Front grip deficiency
* Rear grip deficiency
* Snap transitions
* Lazy transitions
* Excessive rotation
* Insufficient rotation
* Difficult initiation
* Poor self-steer behavior
* High-angle instability
* Speed-dependent handling problems

The goal is for ADT to answer:

What is the car doing, why is it doing it, and what should we change?

⸻

Grip Modeling

Expand ADT’s understanding of:

* Front grip
* Rear grip
* Front/rear grip balance
* Grip during initiation
* Grip during transitions
* Grip at sustained angle
* Grip during throttle application
* Speed-dependent behavior

⸻

Initiation Analysis

Treat drift initiation as its own tuning problem.

Potential initiation methods include:

* Clutch kick
* Handbrake
* Feint
* Power-over
* Weight transfer

A car struggling during initiation may require very different changes from one struggling during sustained drift.

⸻

AC Setup Intelligence 2.0

Improve relationships between telemetry, driver intent, and available Assetto Corsa setup parameters.

Areas include:

* Tire pressures
* Camber
* Toe
* Caster
* Steering geometry where available
* Dampers
* Springs
* Anti-roll bars
* Differential
* Brake balance
* Gearing
* Other car-supported setup parameters

ADT should only recommend changes that the specific car actually allows the driver to make.

⸻

Desired Behavior 2.0

Continue developing the per-car driver-intent system.

Examples include:

* More / less self-steer
* Faster / slower transitions
* More / less front bite
* More / less rear grip
* More stability
* More rotation
* Easier initiation
* Higher-angle stability
* More aggressive behavior
* More forgiving behavior

The objective is to tune toward how the driver wants the car to behave, rather than toward one universal drift setup.

⸻

Recommendation Confidence

Give recommendations a confidence level based on available evidence.

Example:

Recommendation: Reduce rear tire pressure
Confidence: High
Reason: Consistent rear grip loss detected across multiple comparable transitions.

Confidence may eventually consider:

* Amount of usable telemetry
* Consistency across runs
* Calibration quality
* Known car behavior
* Hardware validation
* Results of previous changes

⸻

🟡 Upcoming — Tuning Workflow

Before / After Analysis

Turn tuning into an iterative process:

Run A → Recommendation → Change → Run B → Result

ADT should track:

* Telemetry differences
* Setup differences
* FFB differences
* Driver feedback
* Desired Behavior progress
* Whether a recommendation helped

⸻

Tune History

Create a per-car history containing:

* Runs
* Recommendations
* Changes
* Saved setups
* Driver notes
* Results

⸻

Tune Versioning

Allow multiple tunes without overwriting previous work.

Examples:

* Baseline
* Tune 1
* Tune 2
* Tandem
* Competition
* Solo
* Experimental

Support duplication and rollback.

⸻

Recommendation Tracking

Track whether recommendations were:

* Suggested
* Accepted
* Rejected
* Applied
* Reverted

This can eventually help ADT make better future recommendations.

⸻

🔵 Planned — Assetto Corsa Integration

Automatic AC Setup Application

Investigate allowing an approved ADT tune to be applied directly to the corresponding Assetto Corsa car setup.

Safety requirements include:

* Never overwrite unexpectedly
* Back up existing setups
* Validate supported parameters
* Clearly show what will change
* Detect failures
* Support rollback
* Preserve user-created setups

The goal is to reduce repetitive manual copying between ADT and Assetto Corsa.

⸻

🔵 Planned — Modern ADT Experience

UI Overhaul

Modernize ADT while preserving existing functionality.

Goals include:

* Modern window chrome
* Cleaner navigation
* Better spacing and typography
* Improved telemetry presentation
* Better recommendation presentation
* Cleaner profile/car selection
* Stronger visual hierarchy
* Better tune comparison
* Improved scaling across window sizes

Existing ADT functionality should remain intact throughout the redesign.

⸻

First-Run Experience

Create a guided setup process for new users.

Potential flow:

1. Locate Assetto Corsa
2. Configure SimHub
3. Select wheelbase
4. Select rim
5. Verify bridge
6. Test telemetry
7. Select car
8. Run calibration
9. Start first session

The objective is for a new user to successfully configure ADT without developer assistance.

⸻

ADT Dashboard

Create a central overview displaying:

* Current car
* Current hardware
* SimHub status
* Bridge status
* Telemetry status
* Current tune
* Desired Behavior
* Last session
* Outstanding recommendations
* Quick session controls

⸻

Better Recommendation Explanations

Recommendations should explain:

* What ADT detected
* Why it matters
* What ADT recommends
* What the change should feel like
* Possible tradeoffs

ADT should teach the driver rather than simply output numbers.

⸻

🔵 Planned — ADT Control Center

SimHub Dash Studio / Touchscreen Interface

Create an ADT Control Center designed for use through SimHub Dash Studio on a touchscreen.

Potential information:

* Connected car
* Telemetry status
* Current tune
* Current run
* Live telemetry
* ADT status
* Recommendation status

Potential controls:

* Start run
* Stop run
* Mark run good/bad
* Accept recommendation
* Reject recommendation
* Apply tune
* Revert tune
* Adjust Desired Behavior
* Switch tune
* Save session

Goal

Operate ADT from the sim rig without constantly reaching for a mouse and keyboard.

⸻

🔵 Planned — Integration Health

SimHub Bridge Improvements

Continue improving:

* Installation
* Repair
* Diagnostics
* Connection monitoring
* Version compatibility
* Safe AZOM writes
* Validation
* Readback verification
* Failure handling
* Revert support

⸻

Integration Health Center

Provide one location showing the state of ADT’s major integrations.

Example:

Assetto Corsa     Connected
SimHub            Connected
ADT Bridge        Connected
AZOM              Connected
Telemetry         Receiving
Car               Identified
Configuration     Valid

Failures should provide useful troubleshooting information instead of generic errors.

⸻

🟣 Future — Profiles & Community

Profile Sharing

Allow import/export of supported:

* Hardware profiles
* Car profiles
* Desired Behavior profiles
* Tunes
* Calibration information

⸻

Tune Sharing

Potential community tune packages could contain:

* Car
* Drift pack/version
* AC setup
* FFB settings
* AZOM settings
* Desired Behavior
* Hardware context
* Notes

⸻

Tune Comparison

Compare two tunes directly using:

* Parameter differences
* Telemetry differences
* Driver feedback
* ADT assessment

⸻

Community-Verified Profiles

Potential verification levels:

* ADT Official
* Community Verified
* Hardware Verified
* Pack Verified

⸻

Community Knowledge

Build useful knowledge from testing without assuming every community tune is universally correct.

Potential information includes:

* Known unusual cars
* Pack-specific behavior
* Hardware quirks
* SimHub/AZOM issues
* Calibration recommendations

⸻

🟣 Future — ADT Ecosystem

Discord Bot

Continue expanding the ADT Discord bot with useful community functionality such as:

* Release information
* Repository updates
* Known issues
* Version lookup
* Support links
* Documentation
* Tester tools
* Hardware compatibility
* Car/pack information

⸻

Bot Control Center

Create a central management interface for:

* Bot health
* GitHub integration
* Release posting
* Channels
* Tester systems
* Configuration
* Logging
* Commands
* Future integrations

⸻

GitHub → Discord Integration

Continue improving automatic communication between ADT development and the community.

Current direction:

* Releases → #releases
* Repository activity → #github-updates

Future events may include:

* Important issues
* Milestones
* Beta-testing requests
* Major documentation updates

Automation should remain useful without flooding the Discord server.

⸻

⚙️ Continuous — Releases & Support

Release Pipeline

Formalize a repeatable release process:

1. Version bump
2. Changelog
3. Build
4. Installer build
5. Portable build
6. Bridge packaging
7. Clean-install test
8. Upgrade test
9. Generate SHA-256 hashes
10. Write release notes
11. Create Git tag
12. Publish GitHub release/prerelease
13. Post Discord announcement
14. Notify testers

⸻

Compatibility Checking

ADT should eventually identify:

* ADT version
* Bridge version
* SimHub compatibility
* Configuration schema
* Known incompatible components

⸻

Update System

Potential future update support:

* Check for new versions
* Display release notes
* Download updates
* Safely install updates

Users should remain in control of downloading and installing updates.

⸻

Support Bundles

Continue improving privacy-conscious diagnostic packages that provide useful technical information for troubleshooting.

⸻

📚 Continuous — Documentation

ADT User Guide

Maintain fool-proof documentation covering:

* Installation
* First launch
* Hardware setup
* Car selection
* Calibration
* Recording runs
* Desired Behavior
* Recommendations
* AC setup tuning
* FFB
* AZOM
* Saving tunes
* Troubleshooting

⸻

Built-In Help

Bring important documentation directly into ADT through:

* Tooltips
* Help buttons
* Recommendation explanations
* Setup parameter explanations
* Troubleshooting links

⸻

🟣 Long-Term Intelligence

Per-Car Learning

Explore allowing ADT to learn how a specific car responds to tuning changes.

Instead of only:

Car X normally responds well to Y.

ADT could eventually reason:

For this driver, hardware, car, and Desired Behavior, previous results indicate Y is likely to help.

⸻

Recommendation Outcome Learning

Use before/after results and accepted/rejected recommendations to improve future tuning decisions.

⸻

Session Quality Detection

Improve ADT’s ability to reject telemetry that shouldn’t influence tuning.

Examples:

* Crashes
* Spins
* Straight-line driving
* Pit-lane activity
* Incomplete initiations
* Accidental inputs
* Insufficient telemetry

⸻

🧪 Experimental

These ideas are research areas and not commitments.

Assisted Real-Time Tuning

Investigate whether limited real-time tuning assistance could eventually be useful.

Any implementation would require strict:

* Safety limits
* Validation
* User permission
* Logging
* Rollback
* Emergency disable
* Hardware-specific testing

ADT should never silently change critical hardware behavior.

⸻

Automated Session Analysis

Analyze groups of recorded sessions to identify:

* Consistent problems
* Outlier runs
* Changes between sessions
* Highest-confidence tuning opportunities

⸻

Recommendation Simulator

Show the expected tradeoffs of a recommendation before applying it.

Example:

Expected Effect
Front Bite          ↑
Transition Speed    ↑
Rear Stability      ↓ Slightly

⸻

🗓️ Milestone Direction

v0.8.x — Public Beta Stabilization

Focus:

* Bugs
* External testing
* Hardware validation
* Telemetry validation
* AZOM reliability
* Recommendation accuracy
* Diagnostics
* UI pain points

Avoid unnecessary feature creep during stabilization.

⸻

v0.9 — Intelligence

Target areas:

* Telemetry Intelligence 2.0
* Grip modeling
* Initiation analysis
* AC Setup Intelligence 2.0
* Desired Behavior 2.0
* Recommendation confidence
* Better before/after analysis

Goal

Prove that ADT’s recommendations consistently make sense.

⸻

v0.10 — Workflow

Target areas:

* Tune history
* Tune versioning
* Before/after workflow
* Recommendation tracking
* Improved dashboard
* First-run experience
* Integration health

Goal

Make ADT easy enough for someone who didn’t build it to understand.

⸻

v0.11 — Integration

Target areas:

* Automatic AC setup application
* Bridge improvements
* Touchscreen groundwork
* SimHub integration improvements

Goal

Remove repetitive work between ADT and the simulator.

⸻

v0.12 — Control Center

Target areas:

* ADT Control Center
* SimHub Dash Studio touchscreen interface
* Touch-friendly session controls
* Live status
* Recommendation interaction

Goal

Operate ADT directly from the sim rig.

⸻

v0.13+ — Community & Learning

Potential areas:

* Profile sharing
* Tune sharing
* Community verification
* Larger hardware database
* Per-car learning
* Recommendation outcome learning
* Community knowledge

⸻

🏆 What Does v1.0 Mean?

ADT should not become v1.0 simply because enough features have been added.

For ADT 1.0, the project should have:

* Stable installer
* Stable portable build
* Reliable telemetry
* Reliable SimHub integration
* Reliable bridge
* Reliable profile storage
* Major supported hardware tested
* AC setup recommendations validated by external testers
* FFB/AZOM recommendations validated
* Desired Behavior proven useful
* Clean first-run experience
* Useful diagnostics
* Complete user documentation
* No common data-loss bugs
* Safe upgrade path
* Repeatable release process
* Production-quality UI
* Successful use by people who did not build ADT

v1.0 means ADT is trustworthy, not merely feature-rich.

⸻

🧭 Development Principles

Before adding a feature, ask:

1. Does this make ADT’s recommendations more accurate?
2. Does this make ADT easier to use?
3. Does this make ADT safer or more reliable?
4. Does this remove repetitive work from tuning?
5. Does this help validate whether ADT actually works?

If the answer is no to all five, it probably shouldn’t take priority over the existing roadmap.

⸻

🤝 Want to Help?

ADT is currently looking for real-world testing across different:

* Wheelbases
* Wheels/rims
* Drift packs
* Cars
* Driving styles

Testing, bug reports, tuning feedback, and reproducible results are extremely valuable during the public beta.

Links

* Repository: https://github.com/T3ddyGrahams/AtomicDriftTuner
* Discord: https://discord.gg/aUwsVxqcp

⸻

This roadmap represents the current direction of Atomic Drift Tuner. Features, priorities, and milestone targets may change as ADT develops and feedback is collected.
