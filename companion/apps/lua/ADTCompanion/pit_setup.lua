-- Explicit pit-menu mutations only. Transport must acquire the desktop recorder lease first.
-- Keep backup contents and generated relative filenames local; server paths are never used.
return function(api, filesystem)
  api = api or ac
  filesystem = filesystem or io
  local c = { previous = nil, attempted = {}, subscriptions = {}, generation = 0, serial = 0 }
  local function finite(v) return type(v) == 'number' and v == v and math.abs(v) < math.huge end
  local function integer(v, max) return finite(v) and v >= 0 and v <= max and v == math.floor(v) end
  local function id(v) return type(v) == 'string' and #v == 32 and v:match('^%x+$') ~= nil end
  local function sectionName(v) return type(v) == 'string' and #v > 0 and #v <= 128 and v:match('^[A-Z0-9_]+$') ~= nil end
  local function result(success, state, message) return {success=success, state=state, message=message} end
  local function failure(message, verification) return result(false, verification and 'verification-failed' or 'failed', message) end
  local function near(a, b) return finite(a) and finite(b) and math.abs(a - b) <= 0.000001 end
  local function equal(a, b)
    for key, value in pairs(a) do if b[key] ~= value then return false end end
    for key, value in pairs(b) do if a[key] ~= value then return false end end
    return true
  end
  local function numericEqual(a, b)
    for key, value in pairs(a) do if not near(value, b[key]) then return false end end
    for key, value in pairs(b) do if not near(a[key], value) then return false end end
    return true
  end

  local function parse(ini)
    if type(ini) ~= 'string' or #ini == 0 or #ini > 65536 or ini:find('[%z\1-\8\11\12\14-\31\127]') then return nil end
    local values, lines, sections, section, count = {}, {}, {}, nil, 0
    local position = 1
    while position <= #ini do
      local finish = ini:find('\n', position, true)
      local whole = ini:sub(position, finish or #ini)
      local text = whole:gsub('[\r\n]+$', '')
      local name, remainder = text:match('^%s*%[([^%]]+)%](.*)$')
      if name then
        if not remainder:match('^%s*$') and not remainder:match('^%s*[;#]') then return nil end
        name = name:upper()
        if not sectionName(name) or sections[name] then return nil end
        sections[name], section = true, name
      elseif text:match('^%s*%[') then return nil
      end
      local prefix, raw, suffix = text:match('^(%s*[Vv][Aa][Ll][Uu][Ee]%s*=%s*)([^;#]-)(%s*[;#].*)$')
      if not prefix then prefix, raw = text:match('^(%s*[Vv][Aa][Ll][Uu][Ee]%s*=%s*)(.-)%s*$'); suffix = '' end
      local line = {text=whole}
      if prefix then
        raw = raw:match('^%s*(.-)%s*$')
        local number = tonumber(raw)
        if not section or values[section] ~= nil or not finite(number) or math.abs(number) > 1e12
          or (number == 0 and raw:match('^[^eE]*[1-9][^eE]*[eE]'))
          or (not raw:match('^[+-]?%d*%.?%d+[eE]?[+-]?%d*$') and not raw:match('^[+-]?%d+%.?%d*[eE]?[+-]?%d*$')) then return nil end
        count = count + 1
        if count > 512 then return nil end
        values[section] = number
        line.section, line.prefix, line.suffix = section, prefix, suffix or ''
        line.ending = whole:sub(#text + 1)
      elseif text:upper():match('^%s*VALUE%s*=') then return nil end
      lines[#lines + 1] = line
      position = finish and finish + 1 or #ini + 1
    end
    if count == 0 then return nil end
    return {ini=ini, values=values, lines=lines}
  end

  if type(api.onSessionStart) == 'function' then
    local ok, subscription = pcall(api.onSessionStart, function() c.generation = c.generation + 1 end)
    if ok and subscription then c.subscriptions[#c.subscriptions + 1] = subscription end
  end

  local function context()
    for _, name in ipairs({'getSim','getCar','getCarID','getTrackID','getTrackLayout','isSetupAvailableToEdit',
      'getSetupSpinners','stringifyCurrentSetup','saveCurrentSetup','loadSetup','getFolder'}) do
      if type(api[name]) ~= 'function' then return nil, 'This CSP version does not support guarded setup changes.' end
    end
    if type(filesystem) ~= 'table' or type(filesystem.exists) ~= 'function'
      or type(api.FolderID) ~= 'table' or api.FolderID.UserSetups == nil then
      return nil, 'This CSP version cannot check safe backup filenames.'
    end
    local sim, car = api.getSim(), api.getCar(0)
    if not sim or not car or sim.isLive ~= true or sim.isPaused ~= false or sim.isReplayActive ~= false
      or sim.isReplayOnlyMode ~= false or sim.isShowroomMode ~= false or sim.isPreviewsGenerationMode ~= false
      or car.index ~= 0 or car.isConnected ~= true or car.isActive ~= true or car.isRemote ~= false
      or car.isAIControlled ~= false or car.physicsAvailable ~= true then
      return nil, 'Use the live player car outside replay or pause.'
    end
    if car.isInPit ~= true or not finite(car.speedKmh) or math.abs(car.speedKmh) > 0.1
      or api.isSetupAvailableToEdit() ~= true then
      return nil, 'Park in your pit box and open the editable setup menu.'
    end
    local identity = {carId=api.getCarID(0), trackId=api.getTrackID(), trackLayout=api.getTrackLayout(),
      sessionIndex=sim.currentSessionIndex, sessionType=sim.raceSessionType, online=sim.isOnlineRace, generation=c.generation}
    for _, key in ipairs({'carId','trackId','trackLayout'}) do
      if type(identity[key]) ~= 'string' or #identity[key] > 128 or identity[key]:find('[%c/\\:*?"<>|]')
        or identity[key] == '.' or identity[key] == '..' or identity[key]:match('[%.%s]$')
        or (key ~= 'trackLayout' and #identity[key] == 0) then return nil, 'Current car and session identity is unavailable.' end
    end
    if not integer(identity.sessionIndex, 65535) or not integer(identity.sessionType, 255) or type(identity.online) ~= 'boolean'
      or not integer(sim.frame, 9007199254740991) or not finite(sim.time) or sim.time < 0 or sim.time > 9007199254740991
      or not finite(sim.currentSessionTime) or math.abs(sim.currentSessionTime) > 9007199254740991 then
      return nil, 'Current car and session identity is unavailable.'
    end
    return {identity=identity, frame=sim.frame, time=sim.time, sessionTime=sim.currentSessionTime}
  end
  local function sameContext(a, b)
    return a and b and equal(a.identity, b.identity) and b.frame >= a.frame and b.time >= a.time and b.sessionTime >= a.sessionTime
  end
  local function snapshot()
    local before, reason = context()
    if not before then return nil, reason end
    local setup = parse(api.stringifyCurrentSetup(false, false))
    if not setup then return nil, 'CSP did not provide a complete, unambiguous numeric setup.' end
    local after = context()
    if not sameContext(before, after) then return nil, 'The car or session changed. Review the plan again.' end
    setup.context = after
    return setup
  end
  local function changesAllowed(current, target)
    local all = api.getSetupSpinners()
    if type(all) ~= 'table' then return false end
    local spinners = {}
    for _, spinner in ipairs(all) do
      if type(spinner) == 'table' and type(spinner.name) == 'string' then
        local name = spinner.name:upper()
        if spinners[name] then return false end
        spinners[name] = spinner
      end
    end
    for name, after in pairs(target) do
      if current[name] == nil then return false end
      if current[name] ~= after then
        local s = spinners[name]
        if not s or s.readOnly ~= false or s.visible ~= true or s.value ~= current[name]
          or not finite(s.min) or not finite(s.max) or not finite(s.step) or s.step <= 0
          or after < s.min or after > s.max then return false end
        if s.itemValues ~= nil then
          if type(s.itemValues) ~= 'table' then return false end
          local found = false
          for _, item in ipairs(s.itemValues) do if item == after then found = true end end
          if not found then return false end
        else
          local steps = (after - s.min) / s.step
          if math.abs(steps - math.floor(steps + 0.5)) > 0.000001 then return false end
        end
      end
    end
    for name in pairs(current) do if target[name] == nil then return false end end
    return true
  end
  local function planTarget(plan, current)
    if type(plan) ~= 'table' or plan.protocolVersion ~= 1 or not id(plan.planId)
      or plan.carId ~= current.context.identity.carId or type(plan.baselineValues) ~= 'table'
      or type(plan.changes) ~= 'table' or #plan.changes == 0 or #plan.changes > 64 then return nil end
    local baselineCount = 0
    for name, value in pairs(plan.baselineValues) do
      baselineCount = baselineCount + 1
      if baselineCount > 512 or not sectionName(name) or not near(value, current.values[name]) then return nil end
    end
    if baselineCount == 0 then return nil end
    local target, seen = {}, {}
    for name, value in pairs(current.values) do target[name] = value end
    for _, change in ipairs(plan.changes) do
      if type(change) ~= 'table' or not sectionName(change.section) or seen[change.section]
        or not finite(change.before) or not finite(change.after) or change.before == change.after
        or not near(plan.baselineValues[change.section], change.before)
        or not near(current.values[change.section], change.before) then return nil end
      seen[change.section], target[change.section] = true, change.after
    end
    if not changesAllowed(current.values, target) then return nil end
    return target
  end
  local function patch(current, target)
    local lines = {}
    for _, line in ipairs(current.lines) do
      if line.section and target[line.section] ~= current.values[line.section] then
        lines[#lines + 1] = line.prefix .. string.format('%.17g', target[line.section]) .. line.suffix .. line.ending
      else lines[#lines + 1] = line.text end
    end
    return table.concat(lines)
  end

  function c:newCommandId()
    self.serial = self.serial + 1
    local pieces = {string.format('%08x', os.time() % 4294967296)}
    for _ = 1, 5 do pieces[#pieces + 1] = string.format('%04x', math.random(0, 65535)) end
    pieces[#pieces + 1] = string.format('%04x', self.serial % 65536)
    return table.concat(pieces)
  end

  function c:prepare(plan, operation)
    local ok, ticket, reason = pcall(function()
      local current, why = snapshot()
      if not current then return nil, why end
      local target
      if operation == 'apply' then target = planTarget(plan, current)
      elseif operation == 'restore' and self.previous and sameContext(self.previous.context, current.context) then
        target = self.previous.values
        if not changesAllowed(current.values, target) then target = nil end
      end
      if not target then return nil, operation == 'restore'
        and 'The previous setup cannot be restored in this car, session or menu.'
        or 'The staged plan no longer matches the current setup or editable controls. Stage it again in desktop ADT.' end
      return {current=current, target=target, operation=operation,
        planId=operation == 'restore' and self.previous.planId or plan.planId}
    end)
    if not ok then return nil, 'CSP could not validate the current setup.' end
    return ticket, reason
  end

  function c:execute(plan, commandId, ticket)
    if type(commandId) ~= 'string' or #commandId == 0 or #commandId > 64
      or not commandId:match('^[A-Za-z0-9_-]+$') or self.attempted[commandId] then
      return failure('This command has already been attempted or is invalid. Review the current setup.')
    end
    self.attempted[commandId] = true -- Consume before any API call, including failures.
    local mutated = false
    local ok, response = pcall(function()
      if type(ticket) ~= 'table' then return failure('The setup check expired. Review the plan again.') end
      local current, reason = snapshot()
      if not current or not sameContext(ticket.current.context, current.context)
        or not equal(ticket.current.values, current.values) or current.ini ~= ticket.current.ini then
        return failure(reason or 'The setup changed while waiting for ADT. Review the plan again.')
      end
      local target
      if ticket.operation == 'apply' and type(plan) == 'table' and plan.planId == ticket.planId then target = planTarget(plan, current)
      elseif ticket.operation == 'restore' and self.previous and self.previous.planId == ticket.planId
        and sameContext(self.previous.context, current.context) then target = self.previous.values end
      if not target or not equal(target, ticket.target) or not changesAllowed(current.values, target) then
        return failure('The approved changes no longer match the current setup or editable controls.')
      end
      local baseFolder = api.getFolder(api.FolderID.UserSetups)
      if type(baseFolder) ~= 'string' or #baseFolder == 0 or baseFolder:find('%c') then return failure('The setup folder is unavailable. No tune was applied.') end
      local carFolder = baseFolder .. '/' .. current.context.identity.carId .. '/'
      local backupName, appliedName
      for _ = 1, 16 do
        local fileId = self:newCommandId()
        local candidateBackup = 'generic/ADT_Previous_' .. fileId .. '.ini'
        local candidateApplied = 'generic/ADT_' .. (ticket.operation == 'restore' and 'Restored_' or 'Tune_') .. fileId .. '.ini'
        if filesystem.exists(carFolder .. candidateBackup) == false and filesystem.exists(carFolder .. candidateApplied) == false then
          backupName, appliedName = candidateBackup, candidateApplied
          break
        end
      end
      if not backupName or filesystem.exists(carFolder .. backupName) ~= false then return failure('No unused backup filename is available. No tune was applied.') end
      if api.saveCurrentSetup(backupName) ~= true then return failure('The previous setup could not be saved. No tune was applied.') end
      local saved = snapshot()
      if not saved or not sameContext(current.context, saved.context) or not equal(saved.values, current.values)
        or not changesAllowed(saved.values, target) then return failure('The setup changed during backup. No tune was applied.') end
      if ticket.operation == 'apply' then
        self.previous = {ini=current.ini, values=current.values, context=current.context, planId=ticket.planId, filename=backupName}
      end
      local contents = ticket.operation == 'restore' and self.previous.ini or patch(saved, target)
      mutated = true
      if api.loadSetup(contents) ~= true then return failure('CSP did not confirm the load. Check the setup; Restore previous is available.', true) end
      local loaded = snapshot()
      if not loaded or not sameContext(current.context, loaded.context) or not numericEqual(loaded.values, target) then
        return failure('Setup verification failed. Check the current values or explicitly choose Restore previous.', true)
      end
      if filesystem.exists(carFolder .. appliedName) ~= false then return failure('The values loaded, but the new filename is already in use. Restore previous is available.') end
      if api.saveCurrentSetup(appliedName) ~= true then return failure('The values loaded, but saving the new setup failed. Restore previous is available.') end
      if type(api.refreshSetups) == 'function' then pcall(api.refreshSetups) end
      local final = snapshot()
      if not final or not sameContext(current.context, final.context) or not numericEqual(final.values, target) then
        return failure('Final setup verification failed. Check the current values or explicitly choose Restore previous.', true)
      end
      return result(true, ticket.operation == 'restore' and 'restored' or 'applied', ticket.operation == 'restore'
        and 'Previous setup restored, saved and verified. Confirm the recording plan again.'
        or 'Tune applied, saved and verified. The previous setup was backed up. Confirm the recording plan again.')
    end)
    if ok then return response end
    return failure(mutated and 'CSP interrupted the change. Check the current setup or explicitly choose Restore previous.'
      or 'CSP could not complete the setup check or backup. No tune was applied.', mutated)
  end
  return c
end
