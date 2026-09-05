# ADT Beta Testing Guide

Thank you for helping test Atomic Drift Tuner (ADT).

ADT is currently in public beta, and real-world testing across different drivers, cars, drift packs, wheelbases, rims, and SimHub/AZOM configurations is extremely valuable.

This guide explains how to test ADT in a way that gives us useful, repeatable feedback.

You do **not** need to be an expert tuner to help. If something feels better, worse, confusing, incorrect, or just plain weird, we want to know about it.

## What We're Testing

During the public beta, we're especially interested in:

- AC setup recommendations
- FFB and AZOM recommendations
- Telemetry reliability and accuracy
- Desired Behavior results
- Different cars and drift packs
- Different wheelbases and rims
- SimHub bridge reliability
- Profile and configuration persistence
- Installation and update issues
- UI/UX problems or confusing workflows
- Crashes, errors, or unexpected behavior

Even if ADT works perfectly during your test, that result is useful to us.

## Before You Start

Before testing, please record the basic details of your setup.

This helps us understand whether a problem is related to ADT itself, a specific car, hardware combination, SimHub/AZOM configuration, or another part of the setup.

Please include:

- ADT version
- Assetto Corsa version
- SimHub version
- AZOM version, if used
- Wheelbase model
- Rim model
- Wheelbase firmware version, if known
- Car name
- Drift pack and pack version, if known
- Track
- Whether the ADT SimHub bridge is installed and running
- Whether you are using the installer or portable build

If you're reporting a tuning issue, also include:

- Your current Desired Behavior settings
- Whether the car has already been calibrated in ADT
- Any important setup changes you made before testing

## Start With a Baseline

Before applying any ADT recommendations, drive the car in its current state.

Try to complete several representative laps or drift runs without changing the setup.

Pay attention to how the car behaves during:

- Initiation
- Mid-drift
- Transitions
- Throttle changes
- Braking
- High-speed sections
- Low-speed sections
- Recovery from mistakes

Try to describe the car in simple terms.

Examples:

- Front pushes wide
- Rear has too much grip
- Rear feels unstable
- Transitions are too slow
- Car snaps during transitions
- Self-steer feels too slow
- Wheel oscillates too much
- Car feels numb
- Car rotates too aggressively

There are no "wrong" descriptions. We want to know what **you actually feel while driving**.

## Set Your Desired Behavior

Before collecting your main test run, configure **Desired Behavior** for the car you are testing.

Desired Behavior tells ADT how **you want that specific car to drive**. Two drivers can use the same car and legitimately want different behavior, so this information is important when evaluating ADT's recommendations.

Set Desired Behavior based on what you actually want from the car rather than what you think ADT expects.

When submitting feedback, tell us what you were trying to achieve.

Examples:

- Faster transitions
- More rear stability
- More rotation
- More front grip
- Less aggressive rotation
- More predictable initiation
- Better high-speed stability
- Easier throttle control

If you change Desired Behavior during testing, mention that in your report.

## Collect a Representative Telemetry Run

Once your baseline and Desired Behavior are established, collect telemetry from normal drifting.

For the most useful test:

- Use the same car and track as your baseline
- Drive the car normally
- Include multiple initiations and transitions
- Include both left and right turns when possible
- Include a reasonable range of speeds
- Avoid intentionally crashing or spinning just to create more telemetry
- Try to capture the type of driving where you noticed the behavior you want to improve

A perfect run is **not** required.

We would rather see representative driving than a carefully staged run that does not reflect how you normally drive.

If something unusual happens during the run — such as frozen telemetry, incorrect car detection, missing data, a session reset, or obviously incorrect values — please report it even if ADT continues working.

### Bad Telemetry Is Also a Test Result

ADT should not confidently make tuning decisions from telemetry that is clearly missing, frozen, malformed, or otherwise unusable.

If ADT appears to use bad telemetry anyway, **please report it as a bug**.

## Test ADT's Recommendations

After collecting your telemetry, review the recommendations ADT provides.

For each recommendation you test:

1. Note what ADT recommended.
2. Apply the recommended change.
3. Avoid making unrelated tuning changes at the same time when possible.
4. Drive the **same car and track** again.
5. Try to reproduce the same type of driving as your baseline run.
6. Compare the result.

We want to know whether the recommendation made the car:

- **Better**
- **Worse**
- **No noticeable difference**
- **Different, but with a tradeoff**

A tradeoff is important feedback.

For example, a change might improve transitions but reduce stability, or increase rotation while making the car harder to control.

Please tell us about both sides of the result.

## What Changed?

After testing a recommendation, describe what you actually felt.

Pay particular attention to:

- Initiation behavior
- Front grip
- Rear grip
- Mid-drift balance
- Rotation
- Transition speed
- Transition stability
- Throttle response
- Braking behavior
- High-speed stability
- Low-speed behavior
- Self-steer and wheel return speed
- Oscillation
- Steering weight and detail
- Predictability
- Overall confidence in the car

You do not need to use technical tuning terminology.

Something like:

> "It stopped snapping during transitions, but now the rear feels a little too planted."

is more useful than simply saying:

> "It's better."

## Test One Step at a Time

When practical, avoid changing several unrelated settings between comparison runs.

If ADT recommends multiple changes, you may test them together, but tell us which recommendations were applied.

If a recommendation seems obviously incorrect, extreme, unsafe, or likely to cause a problem, **do not apply it just for the sake of testing it**.

Instead, report the recommendation and explain why it concerned you.

The goal of beta testing is to validate ADT — not to blindly follow it.

## Testing FFB and AZOM Recommendations

FFB and AZOM changes should be tested separately and carefully.

Wheelbase behavior can vary significantly between hardware, firmware versions, rims, and driver preferences. A recommendation that feels excellent on one setup may behave differently on another.

When testing FFB or AZOM recommendations, please record:

- Wheelbase model
- Rim model
- Wheelbase firmware version, if known
- SimHub version
- AZOM version
- Your original FFB/AZOM settings
- ADT's recommended settings
- Which recommendations you actually applied

After applying a recommendation, pay attention to:

- Self-steer speed
- Wheel return speed
- Transition behavior
- Steering weight
- Road and tire detail
- Damping
- Oscillation
- High-speed stability
- Low-speed behavior
- Catching the wheel during transitions
- Overall predictability and control

## Compare Against Your Original Settings

Whenever possible, compare ADT's recommendation directly against the settings you were using before.

Tell us whether the result was:

- **Better**
- **Worse**
- **No noticeable difference**
- **Different, but with a tradeoff**

If you prefer your original settings, that is completely valid feedback.

We especially want to know **why** you preferred one configuration over the other.

## FFB Safety

Do not apply or continue using any FFB or AZOM setting that causes unexpectedly violent, unstable, or uncomfortable wheel behavior.

If an ADT recommendation appears unreasonable for your hardware, stop the test and report:

- The setting ADT recommended
- Your previous setting
- Your wheelbase and rim
- What happened when the recommendation was applied
- Whether the behavior stopped after reverting the change

Do **not** repeatedly reproduce potentially unsafe wheel behavior just to gather more data.

A recommendation that a tester reasonably considers unsafe should be treated as a serious beta-testing result.

## Report Anything That Seems Wrong

Beta testing is not limited to tuning accuracy.

If ADT behaves unexpectedly, please report it — even if you are not sure whether it is actually a bug.

We especially want to know about:

- Crashes or freezes
- Error messages
- ADT failing to launch
- Telemetry stopping, freezing, or displaying incorrect values
- Incorrect car detection
- SimHub bridge connection or communication failures
- AZOM settings failing to apply
- Recommendations that appear unreasonable or contradictory
- Profiles or settings not saving correctly
- Settings unexpectedly changing between cars
- Desired Behavior not persisting correctly
- Calibration data being lost or applied to the wrong car
- Tune data disappearing
- Installer or portable-build problems
- Problems after updating ADT
- UI elements that are confusing, misleading, or difficult to use
- Anything else that makes you think, "That doesn't seem right."

## Before Restarting ADT

If ADT is still running and the problem is not preventing you from using the application, take a moment to record what happened **before restarting it**.

When possible:

1. Take a screenshot of the problem or error.
2. Note what you were doing immediately before it happened.
3. Record the car and track being used.
4. Record whether SimHub and the ADT bridge were running.
5. Note whether the problem happens repeatedly.
6. Generate an ADT support bundle if the application allows you to do so.

Restarting ADT may clear information that could help diagnose the problem.

If ADT is unstable or continuing to use it could cause another problem, however, stop using it and restart normally.

## Try to Reproduce the Problem — Safely

If the issue appears harmless, try the same action one more time to see whether it happens again.

Please tell us whether the problem is:

- **Always reproducible**
- **Sometimes reproducible**
- **Only happened once**
- **Not tested again**

You do not need to repeatedly reproduce crashes, corrupted data, unsafe FFB behavior, or anything else that could cause damage or data loss.

## Support Bundles

When appropriate, include an ADT support bundle with your report.

Support bundles can provide diagnostic information that is difficult to capture manually and may make it much easier to identify the cause of a problem.

Before sharing one, you are welcome to review its contents.

If you notice sensitive information, credentials, personal files, or unrelated private information in a support bundle, **do not upload it**. Please tell us what you found so the support-bundle system itself can be corrected.

## Beta Test Report Template

You can use the template below when submitting testing feedback.

You do not need to fill out every field if it is not relevant to your test, but more context usually makes the feedback easier to investigate.

### Environment

- **ADT version:**
- **Installer or portable:**
- **Assetto Corsa version:**
- **SimHub version:**
- **AZOM version:**
- **ADT bridge installed/running:** Yes / No
- **Wheelbase:**
- **Rim:**
- **Wheelbase firmware:**
- **Car:**
- **Drift pack/version:**
- **Track:**

### Test Goal

**What were you trying to improve or test?**

Describe the behavior you wanted to change or the ADT feature you were testing.

### Desired Behavior

**What did you tell ADT you wanted from this car?**

Include your Desired Behavior settings or describe the goal in your own words.

### Baseline

**How did the car or FFB feel before applying ADT's recommendation?**

Examples: slow transitions, too much rear grip, unstable initiation, slow self-steer, excessive oscillation, etc.

### ADT Recommendation

**What did ADT recommend?**

List the setup, FFB, or AZOM changes you tested.

### Result

**Overall result:**

- [ ] Better
- [ ] Worse
- [ ] No noticeable difference
- [ ] Different, but with a tradeoff
- [ ] Recommendation was not applied

**What changed after applying the recommendation?**

Describe what you actually felt while driving.

### Telemetry

- **Telemetry appeared normal:** Yes / No / Unsure
- **Car detected correctly:** Yes / No / Unsure
- **Any missing, frozen, or suspicious data:** Yes / No

If yes, explain what you noticed.

### Problems or Unexpected Behavior

Did ADT crash, freeze, lose data, behave strangely, provide a questionable recommendation, or have any other problem?

If yes, describe:

1. What you were doing
2. What you expected to happen
3. What actually happened
4. Whether you could reproduce it

### Attachments

When relevant, please include:

- Screenshots
- ADT support bundle
- Error messages
- Before/after settings
- Other information that may help reproduce the result

### Anything Else?

Tell us anything else you noticed — including things you liked.

Positive results are useful too. Knowing that a recommendation worked correctly on a specific car and hardware combination helps us validate ADT just as much as finding a problem.

## Where to Submit Feedback

There are two main ways to send ADT beta feedback.

### GitHub

For bugs, reproducible problems, feature requests, or technical issues, GitHub is preferred because reports can be tracked through development and linked to fixes.

Repository:

https://github.com/T3ddyGrahams/AtomicDriftTuner

When submitting an issue, include as much information from the Beta Test Report Template above as possible.

If an existing issue already describes your problem, feel free to add your testing results there instead of creating a duplicate.

### ADT Discord

Join the ADT Discord:

https://discord.gg/XphUD738t

Discord is great for:

- General testing feedback
- Questions
- Quick observations
- Discussing tuning results
- Sharing screenshots
- Comparing experiences with other testers
- Asking whether something should become a GitHub issue

If a Discord discussion identifies a reproducible bug or actionable development task, it may later be moved or linked to GitHub for tracking.

## What Makes a Great Test Report?

The most useful reports tell us four things:

1. **What you started with**
2. **What ADT recommended**
3. **What you changed**
4. **What happened afterward**

You do not need to know why something happened.

For example:

> "The car snapped during transitions. ADT recommended a setup change. After applying it, transitions became much more stable, but the car rotated slightly slower."

That is excellent feedback.

## Quick Test Checklist

If you do not have time for a detailed testing session, this shorter process is still useful:

- [ ] Record your ADT version, hardware, car, pack, and track
- [ ] Drive a baseline run
- [ ] Set your Desired Behavior
- [ ] Collect a representative telemetry run
- [ ] Review ADT's recommendations
- [ ] Apply the recommendation you want to test
- [ ] Drive the same car and track again
- [ ] Decide: Better / Worse / No Difference / Tradeoff
- [ ] Report anything strange or unexpected
- [ ] Submit your results

## Thank You

Every useful test helps make ADT more accurate, reliable, and easier to use.

That includes successful tests.

If ADT correctly identifies a problem, recommends a change, and the car behaves closer to what you wanted afterward, **tell us**. Successful results help validate the tuning logic across different cars, hardware, and drivers.

Likewise, if ADT gets something wrong, we want to know exactly that.

The goal is not to prove that ADT is always right.

The goal is to make ADT trustworthy.
