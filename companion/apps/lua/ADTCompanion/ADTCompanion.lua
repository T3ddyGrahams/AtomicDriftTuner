local createClient = require('companion_client')
local setupCapture = require('setup_capture')(ac)
local pitSetup = require('pit_setup')(ac)
local client = createClient(web.request, JSON.stringify, JSON.parse, function() return setupCapture:capture() end, pitSetup)
local preferences = ac.storage({ port = '5190', lastReadyRun = '' })
local port, code = preferences.port, ''
local accent, good, warning = rgbm(0.1, 0.85, 0.95, 1), rgbm(0.4, 0.9, 0.55, 1), rgbm(1, 0.75, 0.3, 1)
local confirm, confirmVersion = false, ''
local rating, nextAction, notes, reviewKey = '', 'Undecided', '', ''
web.timeouts(1000, 1500, 2000, 4000)

function script.update(dt)
  client:update(dt)
  local status = client:fresh() and client.status or nil
  local r = status and status.recorder
  local e = r and r.evidence
  if r and r.state == 'recording' and type(e) == 'table' and e.state == 'ready' and e.readyToReview == true
    and type(r.windowId) == 'string' and #r.windowId > 0 and type(r.sessionId) == 'string' and #r.sessionId > 0 then
    local key = r.windowId .. ':' .. r.sessionId
    if preferences.lastReadyRun ~= key then
      preferences.lastReadyRun = key
      if type(ui.toast) == 'function' and ui.Icons then
        pcall(ui.toast, ui.Icons.Confirm, e.hasGoalLimitations == true
          and 'ADT: Partial review is available. Some goal measurements are still missing. Stop and save when ready.'
          or 'ADT: Enough evidence to review. Stop and save when ready.')
      end
    end
  end
end

local function value(v, fallback)
  return type(v) == 'string' and v ~= '' and v or fallback or ''
end

local function paragraph(label, text)
  if value(text) ~= '' then ui.textWrapped(label .. text) end
end

local function button(label, allowed, callback)
  allowed = allowed == true and not client.busy
  if not allowed then ui.pushDisabled() end
  local clicked = ui.button(label, vec2(-1, 34))
  if not allowed then ui.popDisabled() end
  if clicked and allowed then callback() end
end

local function workflowButton(label, action, fields, extraAllowed)
  button(label, extraAllowed ~= false and client:canWorkflow(action, fields), function()
    client:workflow(action, fields)
    confirm = false
  end)
end

local function choice(label, current, items)
  ui.setNextItemWidth(-1)
  ui.combo(label, current ~= '' and current or 'Choose...', function()
    for _, option in ipairs(items) do
      if ui.selectable(option, current == option) then current = option end
    end
  end)
  return current
end

local function workflowHelp(w)
  if not w then
    ui.textWrapped('In-game guidance and review controls require desktop ADT 0.9.0-preview.13 or newer. Recording still works with compatible older desktop versions.')
  elseif w.available ~= true then
    ui.textWrapped(value(w.message, 'Finish initial setup in desktop ADT.'))
  end
end

local function recordTab(status, w)
  local r = status.recorder
  ui.textWrapped('Car: ' .. value(r.car, 'Prepare in ADT'))
  ui.textWrapped('Driver: ' .. value(r.driver, 'Prepare in ADT'))
  if w then paragraph('Workflow: ', w.focus) end
  ui.textColored(string.upper(value(r.state, 'unprepared')), r.state == 'recording' and accent or warning)
  ui.text(string.format('%.1f s  |  %d samples', tonumber(r.elapsedSeconds) or 0, tonumber(r.samples) or 0))
  paragraph('', r.message)
  if type(r.evidence) == 'table' then
    ui.separator()
    ui.textColored(value(r.evidence.heading, 'RECORDING GUIDANCE'), r.evidence.readyToReview == true and good or warning)
    paragraph('', r.evidence.message)
    if type(r.evidence.neededEvidence) == 'table' then
      for i, item in ipairs(r.evidence.neededEvidence) do if i > 1 then paragraph('• ', item) end end
    end
    ui.treeNode('Recording evidence details', function() paragraph('', r.evidence.details) end)
    ui.textWrapped(r.readyChimeEnabled == true and 'Ready chime: on (PC audio).'
      or 'Optional ready chime: enable it in the desktop Telemetry Recorder (PC audio).')
    ui.separator()
  end
  paragraph('', r.setupMessage)
  button('Start recording', client:fresh() and r.canStart == true, function() client:command('start') end)
  button('Stop recording', client:fresh() and r.canStop == true, function() client:command('stop') end)
  button('Save session', client:fresh() and r.canSave == true, function() client:command('save') end)
  ui.separator()
  ui.textColored('YOUR NEXT STEP', accent)
  paragraph('', status.nextStep)
  paragraph('', status.instructions)
  paragraph('', status.completion)
  if w then
    paragraph('', w.comingNext)
    paragraph('', w.message)
    paragraph('Selected test: ', w.selectedRecommendation)
    paragraph('Setup: ', w.setupName)
    paragraph('Conditions: ', w.conditions)
    if w.canPrepare == true then
      workflowButton('Prepare next recording', 'prepare')
      ui.textWrapped('Uses the plan prepared in desktop ADT. This does not start recording or apply settings.')
    end
    if w.tuneConfirmed == true then
      ui.textWrapped('Settings confirmed for the current recording plan.')
    elseif w.canConfirm == true then
      if confirmVersion ~= w.controlVersion then confirmVersion, confirm = w.controlVersion, false end
      paragraph('', w.confirmationText)
      if ui.checkbox('I checked this is true', confirm) then confirm = not confirm end
      workflowButton('Confirm these settings', 'confirm', {tuneConfirmed = true}, confirm)
    end
  end
  workflowHelp(w)
  ui.treeNode('Progress and more help', function()
    paragraph('', status.progress)
    paragraph('', status.details)
  end)
end

local function findingsTab(w)
  workflowHelp(w)
  if not w then return end
  paragraph('Car: ', w.car)
  paragraph('Driver: ', w.driver)
  ui.textWrapped('Read a saved run when you are ready. ADT keeps the full analysis on the PC.')
  workflowButton('Read saved run', 'findings')
  local report = w.report
  if type(report) ~= 'table' then
    ui.textWrapped('Stop and save a run, then choose Read saved run. Initial setup, manual attachments and driving conditions are prepared in desktop ADT.')
    return
  end
  paragraph('Run: ', report.sessionId)
  paragraph('Goal: ', report.goal)
  paragraph('What ADT noticed: ', report.noticed)
  paragraph('Confidence: ', report.confidence)
  paragraph('Next: ', report.instruction)
  ui.treeNode('Why this next step?', function() paragraph('', report.why) end)
  ui.separator()
  ui.textColored('CHOOSE ONE TEST', accent)
  local recommendations = type(report.recommendations) == 'table' and report.recommendations or {}
  if #recommendations == 0 then ui.textWrapped('No supported change is ready to test. Follow the guidance above or record more evidence.') end
  for i, recommendation in ipairs(recommendations) do
    paragraph('', value(recommendation.domain, 'Recommendation ' .. i))
    paragraph('', recommendation.change)
    paragraph('Confidence: ', recommendation.confidence)
    if recommendation.canSelect ~= true then paragraph('', recommendation.disabledReason) end
    ui.treeNode('Why?##reason' .. i, function() paragraph('', recommendation.why) end)
    workflowButton('Plan this test##plan' .. i, 'plan', {sessionId = report.sessionId, recommendationId = recommendation.id})
    ui.separator()
  end
  ui.textWrapped('Planning saves the test and prepares its recording details. Use Pit setup for a staged supported tune, or make the change yourself. Wait for current setup capture, then confirm the settings before driving again.')
end

local function pitSetupTab()
  local state = client:pitState()
  paragraph('', client.pitMessage)
  if client.pendingPitResult then
    ui.textWrapped('The setup operation has finished locally. ADT must acknowledge its result before recording or another setup action.')
    button('Sync result with ADT', true, function() client:syncPitResult() end)
  end
  if not state then
    ui.textWrapped('Pit setup requires an updated desktop ADT and companion. Stage a supported tune on the PC first.')
    return
  end
  paragraph('', state.message)
  local plan = state.plan
  if type(plan) == 'table' then
    paragraph('Tune: ', plan.label)
    paragraph('Car: ', plan.carId)
    for _, change in ipairs(type(plan.changes) == 'table' and plan.changes or {}) do
      if type(change) == 'table' and type(change.section) == 'string' then
        ui.textWrapped(change.section .. ': ' .. tostring(change.before) .. ' → ' .. tostring(change.after))
      end
    end
    ui.textWrapped('These are stored setup VALUE units; the game may display different units. Check every change before applying.')
  end
  local canApply, reason = client:canPit('apply')
  button('Save & Apply Tune', canApply, function() client:pitAction('apply'); confirm = false end)
  if not canApply then paragraph('', reason) end
  ui.textWrapped('Park in your pit box with the editable setup menu open. This saves a unique previous setup, applies only the listed car settings, saves the tune and reads every numeric value back. Wheelbase and FFB settings still require your own confirmation.')
  ui.separator()
  if pitSetup.previous then
    ui.textWrapped('A previous setup is available for this app session. Restore is an explicit action and replaces the current setup with that backup.')
    local canRestore, restoreReason = client:canPit('restore')
    button('Restore previous', canRestore, function() client:pitAction('restore'); confirm = false end)
    if not canRestore then paragraph('', restoreReason) end
  else ui.textWrapped('Restore previous becomes available after ADT saves a backup for an apply attempt.') end
end

local function compareTab(w)
  workflowHelp(w)
  if not w then return end
  paragraph('Car: ', w.car)
  paragraph('Driver: ', w.driver)
  workflowButton('Read saved run / comparison', 'findings')
  local report = w.report
  if type(report) ~= 'table' then
    ui.textWrapped('Plan one test from Findings, make that change, then record and save a comparable second run. Read the saved run here to compare it with its baseline.')
    return
  end
  local key = value(report.sessionId) .. '|' .. value(report.baselineSessionId)
  if reviewKey ~= key then reviewKey, rating, nextAction, notes = key, '', 'Undecided', '' end
  paragraph('Run: ', report.sessionId)
  paragraph('Baseline: ', value(report.baselineSessionId, 'No comparison baseline selected.'))
  local comparison = report.comparison
  if type(comparison) == 'table' then
    paragraph('Measured result: ', comparison.verdict)
    paragraph('', comparison.summary)
    for _, limitation in ipairs(type(comparison.limitations) == 'table' and comparison.limitations or {}) do
      paragraph('• ', limitation)
    end
    ui.treeNode('Captured setup changes', function()
      local changes = type(comparison.setupChanges) == 'table' and comparison.setupChanges or {}
      for _, change in ipairs(changes) do paragraph('', change) end
      ui.textWrapped(#changes == 0 and 'No numeric setup differences are listed for this comparison.'
        or 'Stored setup VALUE units can differ from game display units. Desktop ADT shows the complete tune comparison; this list shows up to 64 changes.')
    end)
  end
  if report.reviewSaved == true then ui.textColored('Review saved in ADT.', good) end
  if w.canSaveReview ~= true then
    ui.textWrapped(report.reviewSaved == true
      and 'This comparison is already reviewed. Use Findings to plan another supported test, or inspect the full desktop history.'
      or 'Review requires a saved comparison run and a matching baseline. Desktop ADT has the complete history and comparison details.')
    return
  end
  ui.separator()
  ui.textWrapped('Your feedback is saved separately from the measured result. A rating does not prove the change improved the car.')
  rating = choice('How did it feel?', rating, {'Better', 'Worse', 'No noticeable difference', 'Tradeoff'})
  nextAction = choice('What next?', nextAction, {'Undecided', 'Keep and verify', 'Revert manually', 'Test again'})
  ui.textWrapped('Notes (optional, up to 2,000 characters)')
  notes = ui.inputText('##reviewNotes', notes, 0, vec2(-1, 70))
  if client:textLength(notes) > 2000 then ui.textWrapped('Shorten the notes to 2,000 characters before saving. Your draft is still here.') end
  workflowButton('Save run review', 'review', {sessionId = report.sessionId, driverRating = rating, nextAction = nextAction, notes = notes}, rating ~= '')
  ui.textWrapped('Keep / Revert records your decision; it does not apply or undo settings.')
end

local function helpTab(status, w)
  ui.textWrapped('1. Set up your workflow, rig, car and goals in desktop ADT. Prepare driver and conditions. Pair the updated companion for current setup capture, or attach the loaded setup manually.')
  ui.textWrapped('2. Record: check the context, start, drive, stop, then save. Stop alone does not save.')
  ui.textWrapped('3. Findings: read the saved run and plan one supported test. If evidence is weak, repeat the drive first.')
  ui.textWrapped('4. Stage a supported tune in desktop ADT, then use Pit setup → Save & Apply Tune while parked in the setup menu. You can also make the change yourself. Wait for current setup capture, confirm the recording plan, then record a comparable second run.')
  ui.textWrapped('5. Compare: read the measured result and limitations, add your own rating and save the review.')
  ui.separator()
  paragraph('', status.completion)
  paragraph('', status.details)
  paragraph('', status.progress)
  if w then paragraph('', w.comingNext) end
  ui.textWrapped('Keep desktop ADT and its Remote server open; minimizing is fine. SimHub is not required for AC recording. Hiding this panel does not stop a run.')
  ui.textWrapped('After a connection timeout, check the refreshed recorder and review status before trying again. Commands are never repeated automatically.')
  ui.textWrapped('Companion 0.4.0-preview.1. Pit setup: desktop ADT 0.9.0-preview.15 or newer. Automatic setup capture: desktop ADT 0.9.0-preview.14 or newer. Full workflow: desktop ADT 0.9.0-preview.13 or newer. Install/update through Content Manager, then start a new driving session to reload the app.')
  button('Disconnect / pair again', true, function() client:forget(); confirm = false end)
end

function script.windowMain(dt)
  ui.textColored('ATOMIC DRIFT TUNER', accent)
  -- Each pane uses the remaining size, so long explanations and buttons stay reachable at 300 x 280.
  if not client.token then
    ui.childWindow('adtPair', ui.availableSpace(), function()
      paragraph('', client.message)
      ui.textWrapped('On the PC: open ADT Remote and press START REMOTE. Enter its port and current six-digit code. Use ADT preview.13 or newer for the full workflow.')
      ui.setNextItemWidth(-1); port = ui.inputText('Port', port)
      ui.setNextItemWidth(-1); code = ui.inputText('Pairing code', code)
      button('Pair with ADT', true, function() client:pair(code, port); preferences.port = client.port; code = '' end)
      ui.textWrapped('Connects only to ADT on this PC. Pairing credentials stay in memory.')
    end)
    return
  end
  ui.textWrapped(client.message)
  local status = client:fresh() and client.status or nil
  if not status then
    ui.childWindow('adtWaiting', ui.availableSpace(), function()
      ui.textWrapped('Waiting for fresh ADT status. All run and workflow actions stay unavailable until status refreshes.')
      paragraph('', client.commandMessage)
      paragraph('', client.pitMessage)
      if client.pendingPitResult then button('Sync result with ADT', true, function() client:syncPitResult() end) end
      button('Disconnect / pair again', true, function() client:forget(); confirm = false end)
    end)
    return
  end
  ui.textColored(status.telemetryConnected and 'AC TELEMETRY: LIVE' or status.telemetryStale and 'AC TELEMETRY: STALE' or 'AC TELEMETRY: WAITING', status.telemetryConnected and good or warning)
  local evidence = status.recorder and status.recorder.evidence
  if status.recorder and status.recorder.state == 'recording' and type(evidence) == 'table'
    and evidence.readyToReview == true and evidence.state == 'ready' then
    ui.textColored(value(evidence.heading, 'READY TO REVIEW'), good)
    ui.textWrapped(evidence.hasGoalLimitations == true
      and 'Some goal measurements are still missing. Stop and save for a partial review, or open Record for collection instructions. Recording continues.'
      or 'Enough evidence collected. Stop and save when ready. Recording continues.')
  end
  local w = client:workflowState()
  ui.tabBar('adtTabs', function()
    local function pane(label, content)
      ui.tabItem(label, function()
        ui.childWindow('adt' .. label, ui.availableSpace(), function()
          paragraph('', client.commandMessage)
          content()
        end)
      end)
    end
    pane('Record', function() recordTab(status, w) end)
    pane('Findings', function() findingsTab(w) end)
    pane('Pit setup', pitSetupTab)
    pane('Compare', function() compareTab(w) end)
    pane('Help', function() helpTab(status, w) end)
  end)
end
