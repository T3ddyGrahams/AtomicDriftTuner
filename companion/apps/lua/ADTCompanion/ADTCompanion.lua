local createClient = require('companion_client')
local client = createClient(web.request, JSON.stringify, JSON.parse)
local preferences = ac.storage({ port = '5190' })
local port = preferences.port
local code = ''
local accent = rgbm(0.1, 0.85, 0.95, 1)
local good = rgbm(0.4, 0.9, 0.55, 1)
local warning = rgbm(1, 0.75, 0.3, 1)
web.timeouts(1000, 1500, 2000, 4000)

function script.update(dt)
  client:update(dt)
end

local function button(label, allowed, action)
  if not allowed then ui.pushDisabled() end
  local clicked = ui.button(label, vec2(-1, 34))
  if not allowed then ui.popDisabled() end
  if clicked and allowed then client:command(action) end
end

function script.windowMain(dt)
  ui.textColored('ATOMIC DRIFT TUNER', accent)
  ui.textWrapped('Record your run. See what comes next.')
  ui.separator()
  ui.textWrapped(client.message)
  if not client.token then
    ui.textWrapped('On the PC: open ADT 0.9.0-preview.4 or newer, open ADT Remote and press START. Enter the displayed code below.')
    ui.setNextItemWidth(-1)
    port = ui.inputText('Port', port)
    ui.setNextItemWidth(-1)
    code = ui.inputText('Pairing code', code)
    if client.busy then ui.pushDisabled() end
    local pair = ui.button('Pair with ADT', vec2(-1, 34))
    if client.busy then ui.popDisabled() end
    if pair and not client.busy then client:pair(code, port); preferences.port = client.port; code = '' end
    ui.textWrapped('This companion connects only to ADT on this PC. SimHub is not required for recording AC telemetry.')
    return
  end

  local fresh = client:fresh()
  local status = fresh and client.status or nil
  if status then
    ui.textColored(status.telemetryConnected and 'AC TELEMETRY: LIVE' or status.telemetryStale and 'AC TELEMETRY: STALE' or 'AC TELEMETRY: WAITING', status.telemetryConnected and good or warning)
    local r = status.recorder
    ui.separator()
    ui.textWrapped('Car: ' .. (r.car and r.car ~= '' and r.car or 'Prepare in ADT'))
    ui.textWrapped('Driver: ' .. (r.driver and r.driver ~= '' and r.driver or 'Prepare in ADT'))
    ui.textColored(string.upper(r.state or 'unprepared'), r.state == 'recording' and accent or warning)
    ui.text(string.format('%.1f s  |  %d samples', r.elapsedSeconds or 0, r.samples or 0))
    ui.textWrapped(r.message or '')
    button('Start recording', not client.busy and r.canStart, 'start')
    button('Stop recording', not client.busy and r.canStop, 'stop')
    button('Save session', not client.busy and r.canSave, 'save')
    ui.separator()
    ui.textColored('YOUR NEXT STEP', accent)
    ui.textWrapped(status.nextStep or 'Continue in desktop ADT.')
    ui.textWrapped(status.instructions or '')
  else
    ui.textWrapped('Waiting for fresh ADT status. Recording controls are unavailable until the connection recovers.')
  end
  if client.commandMessage ~= '' then ui.separator(); ui.textWrapped(client.commandMessage) end
  ui.separator()
  ui.textWrapped('Keep desktop ADT open; minimizing it is fine. Setup selection and tuning changes stay in desktop ADT.')
  if ui.button('Disconnect / pair again') then client:forget() end
end
