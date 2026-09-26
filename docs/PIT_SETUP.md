# Save and apply a car setup in the pits — local preview.15

Desktop **0.9.0-preview.15** and **ADT Companion 0.4.0-preview.1** add an explicit pit action for a setup generated in desktop ADT. Existing setup recommendations, Desired Behavior, telemetry analysis and FFB calculations remain in use. This is a local preview; real-game validation is pending.

## First end-to-end test

1. Install/update the companion from **Remote → Install / Update Companion**, with the driving session closed. Start Remote, launch a new AC session and pair the companion. Select the same installed car in desktop ADT.
2. Save a named baseline in AC. In desktop **AC Setup**, load that baseline, set your goals or guidance and choose **Generate Car Setup**. Review the proposed changes, then choose **Stage for in-game pits**. Staging freezes the numeric plan; it does not change the car or save an AC setup.
3. Stop and save any recording. Park the live player car in its pit box and open the actual pit setup menu, stationary, unpaused and with setup editing allowed. Merely being in the pit lane is insufficient. The current numeric setup must still match the staged baseline.
4. Open the companion's **Pit setup** tab and review the staged car and changes. Choose **Save & Apply Tune** once. The action saves a uniquely named previous setup, applies the numeric changes, checks current-setup readback, then saves and verifies the changed setup. Both files use the current car's AC user `setups/<car>/generic` folder. Wait for the reported result before starting another action.
5. Inspect the actual AC setup values and both saved files. Completion clears the prior recording confirmation. After verified success, return to the live session, wait for fresh captured-setup evidence, prepare the comparison and explicitly confirm the settings in use again. Keep FFB unchanged for a car-only test. Record, stop and save a comparable run, then read the measured result and save your separate driver rating.
6. In the same game session, return to the editable pit setup menu and choose **Restore previous** explicitly. Check the restored values and reported verification. Restore saves another backup of the current values before loading the previous setup. A Keep/Revert choice in comparison feedback only records your decision; it does not invoke this restore action.

The files are named `ADT_Previous_<unique-id>.ini`, `ADT_Tune_<unique-id>.ini` and, after restore, `ADT_Restored_<unique-id>.ini`. A failure never automatically restores the car; inspect the result and choose recovery explicitly.

Generation, saved goals, feedback, **Update recommendations only** (formerly **Apply Feedback + Regenerate**) and test planning never authorize a pit write. Each Save & Apply Tune or Restore previous action needs its own in-game click. Neither action changes FFB or wheelbase settings.

## When an action is unavailable

The companion checks the current car/session, editable pit setup menu, stationary state, recording state, unchanged numeric baseline and CSP's editable parameter limits/steps. Unsupported or ambiguous values must be rejected rather than guessed. Return to desktop ADT to regenerate and stage a fresh plan if the baseline, selected car or recommendation inputs change.

| Situation | Expected behavior and next step |
| --- | --- |
| Fixed setup or setup editing denied | No write. Use a session where AC allows setup editing, or keep the fixed setup. |
| Unsupported CSP or missing setup APIs | No pit write. Use the manual export/load workflow below. No minimum CSP version is claimed yet. |
| Recording active, car moving, wrong car/session or outside the editable setup menu | No write. Finish the recording and restore the required context before trying again. |
| Baseline changed, invalid value or a value is not editable | No write. Review the current setup and generate/stage again if appropriate. |
| Lost response or unknown outcome after an action began | Do not assume failure or click again. If offered, **Sync result with ADT** sends the completed result without repeating the setup change. Recording remains blocked until acknowledgment. If the result cannot be recovered, exit the AC driving session completely, then use desktop **Clear pending pit action**. ADT refuses to clear while `acs` or `acs_x86` is running. Reconnecting alone is not proof that the setup is correct. |
| Readback mismatch or partial failure | Inspect the reported result and current AC values. Use the previous saved setup for recovery; do not treat the changed setup as verified. |

Restore previous is limited to the same game/companion session and must pass the current context and setup checks. Its in-memory backup is lost when the companion restarts; the saved previous file remains available for manual loading in AC. Existing setup files are preserved; ADT uses new unique names for the operation.

If desktop ADT restarts while a result is waiting to sync, its old operation record is no longer available. Close and restart the AC session as well, inspect/load the intended setup, and stage a fresh plan; do not repeatedly try to apply the old plan. After a pit action, a manual recorder attachment is cleared so an old baseline cannot silently represent the new setup.

After clearing a pending action, restart the AC session, inspect/load the intended setup and regenerate/stage a fresh plan before another pit test. Clearing only resolves ADT's pending state; it does not apply or restore any car values. Wait for fresh setup capture and confirm the recording settings again before driving.

## Standard camber values (local preview.28)

ADT now maps standard `CAMBER_LF/RF/LR/RR` bounds into the same saved `VALUE` units used by export and pit plans. For example, a definition range of -10 to -2 with local `SHOW_CLICKS=1` supports saved values -100 through -20, in whole steps. A baseline of -55 can be staged as -56 without confusing it with an out-of-range physical number. Reload the baseline and generate again after updating.

The verified mapping follows Content Manager's [camber fixed step and saved-value loader](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs) and [mode enum](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupStepsMode.cs): modes 0/1 use raw × 0.1; explicit local mode 2 uses MIN + raw × 0.1. These are setup units, not a computed wheel angle. Missing/global 0/1 mode metadata shares the same camber serialization; global-only mode 2, unknown modes and custom LUT/RATIOS mappings remain unsupported.

The companion's live spinner bounds remain an independent requirement. Desktop support never overrides a fixed, read-only, unavailable or incompatible in-game control.

## Other scalar values (local preview.36)

Supported standard scalar controls now use the same saved-value legality check for generation, file export and staging. Explicit per-control modes 0/1/2 are supported: actual value, value divided by STEP, and (value − MIN) divided by STEP. Previewed values are converted for readability; staged commands and files retain the correct saved values. A rebound control with MIN=500, MAX=9000, STEP=500 and mode 2 has legal saved clicks 0–17; 18 is refused.

Unknown/custom mappings, ambiguous global-only display modes, invalid baselines and incompatible live controls remain held. Small requests may round to no change, which ADT now states explicitly. The existing companion verifies raw saved values against its live spinner limits and whole-setup readback; it does not rescale or guess incompatible metadata. Real-car acceptance is still required. The desktop update does not replace the companion.

## Manual fallback

The existing **Save AC Setup File** action remains available after valid generation. Save under a new name, load that exact file in AC's Setup menu and inspect its values. If current-setup capture is unsupported, turn off **Capture the current car setup through the in-game companion** in Telemetry Recorder and attach the saved file with **Attach AC Setup Snapshot**. The attachment cannot represent later unsaved edits. Explicitly confirm settings before recording, and use the same capture method for the baseline and comparison.

## Acceptance still required

Record the desktop, companion, CSP and car versions with Pass/Fail/Blocked/Not run results. Complete the first test above, then repeat the refusal cases in the table. Include a lost action response, a fixed-setup session, unsupported CSP, an unsaved baseline edit, an active recording, a car/session change, a repeated click and manual fallback. Confirm no duplicate apply occurs, existing files remain intact, an unknown result blocks recording, and Restore previous verifies the original values. Automated checks and SDK review do not prove live compatibility, successful setup application or improved handling.
