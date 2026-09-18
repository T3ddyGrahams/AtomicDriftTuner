-- Read-only snapshot of CSP's current in-memory setup. No saved-file fallback:
-- INIConfig.currentSetup() omits unsaved pit edits and cannot prove what is in use.
-- Call capture() for each desktop nonce, even with the app window hidden. Never reuse
-- a previous successful payload after an unavailable/changed result.
return function(api)
  api = api or ac
  local c = { sequence = 0, sessionGeneration = 1, setupRevision = 0, subscriptions = {} }
  local lastIdentity
  local maxSetupBytes = 65536

  local function finite(v)
    return type(v) == 'number' and v == v and v ~= math.huge and v ~= -math.huge
  end
  local function integer(v, maximum)
    return finite(v) and v >= 0 and v <= maximum and v == math.floor(v)
  end
  local function identifier(v, allowEmpty)
    return type(v) == 'string' and #v <= 128 and (allowEmpty or #v > 0)
      and v ~= '.' and v ~= '..' and not v:find('[%c/\\:*?"<>|]')
      and v == v:match('^%s*(.-)%s*$')
  end
  local function failure(code, message)
    return { available = false, protocolVersion = 1, source = 'csp-current-setup',
      code = code, message = message, captureSequence = c.sequence,
      sessionGeneration = c.sessionGeneration, setupRevision = c.setupRevision }
  end
  local function capability(name) return type(api) == 'table' and type(api[name]) == 'function' end

  -- These events invalidate identity. They do not capture or read any setup files.
  -- Spinner edits need not emit onSetupFile, so fresh serialization is always required.
  if capability('onSetupFile') then
    local ok, subscription = pcall(api.onSetupFile, function(operation)
      if operation == 'load' or operation == 'save' then c.setupRevision = c.setupRevision + 1 end
    end)
    if ok and subscription ~= nil then c.subscriptions[#c.subscriptions + 1] = subscription end
  end
  if capability('onSessionStart') then
    local ok, subscription = pcall(api.onSessionStart, function()
      c.sessionGeneration = c.sessionGeneration + 1
      c.setupRevision = c.setupRevision + 1
      lastIdentity = nil
    end)
    if ok and subscription ~= nil then c.subscriptions[#c.subscriptions + 1] = subscription end
  end

  local function identity()
    local sim, car = api.getSim(), api.getCar(0)
    if not sim or not car then return nil, 'live_unavailable' end
    if sim.isReplayActive ~= false or sim.isReplayOnlyMode ~= false
      or sim.isShowroomMode ~= false or sim.isPreviewsGenerationMode ~= false then
      return nil, 'not_live_session'
    end
    if sim.isLive ~= true or sim.isPaused ~= false or car.index ~= 0
      or car.isConnected ~= true or car.isActive ~= true or car.isRemote ~= false
      or car.isAIControlled ~= false or car.physicsAvailable ~= true then
      return nil, 'live_unavailable'
    end
    local carId, trackId, trackLayout = api.getCarID(0), api.getTrackID(), api.getTrackLayout()
    if not identifier(carId, false) or not identifier(trackId, false) or not identifier(trackLayout, true)
      or not integer(sim.currentSessionIndex, 65535) or not integer(sim.raceSessionType, 255)
      or not integer(sim.frame, 9007199254740991) or not finite(sim.time) or sim.time < 0 or sim.time > 1e15
      or not finite(sim.currentSessionTime) or math.abs(sim.currentSessionTime) > 1e15
      or type(sim.isOnlineRace) ~= 'boolean' then return nil, 'identity_unavailable' end
    return { carId = carId, trackId = trackId, trackLayout = trackLayout,
      sessionIndex = sim.currentSessionIndex, sessionType = sim.raceSessionType,
      sessionTimeMs = sim.currentSessionTime, simTimeMs = sim.time, frame = sim.frame,
      isOnlineRace = sim.isOnlineRace }
  end
  local function sameSession(a, b)
    return a.carId == b.carId and a.trackId == b.trackId and a.trackLayout == b.trackLayout
      and a.sessionIndex == b.sessionIndex and a.sessionType == b.sessionType
      and a.isOnlineRace == b.isOnlineRace
  end

  function c:capture()
    self.sequence = self.sequence + 1
    for _, name in ipairs({'stringifyCurrentSetup', 'getSim', 'getCar', 'getCarID', 'getTrackID', 'getTrackLayout'}) do
      if not capability(name) then
        return failure('unsupported_csp', 'This CSP version cannot provide a live setup snapshot. Attach the setup manually in desktop ADT.')
      end
    end
    local ok, result = pcall(function()
      local before, reason = identity()
      if not before then return failure(reason, 'Live player-car setup is unavailable. Leave replay/pause, enter the driving session, or attach the setup manually.') end
      if lastIdentity and (not sameSession(before, lastIdentity) or before.simTimeMs < lastIdentity.simTimeMs
        or before.sessionTimeMs < lastIdentity.sessionTimeMs or before.frame < lastIdentity.frame) then
        self.sessionGeneration = self.sessionGeneration + 1
      end
      local generation, revision = self.sessionGeneration, self.setupRevision
      -- Include all setup values, not just modifications; omit display/file metadata.
      local ini = api.stringifyCurrentSetup(false, false)
      if type(ini) ~= 'string' or #ini == 0 or #ini > maxSetupBytes
        or ini:find('[%z\1-\8\11\12\14-\31\127]') or not ini:find('%[[^%]\r\n]+%]')
        or not ini:match('[Vv][Aa][Ll][Uu][Ee]%s*=') then
        return failure('setup_unavailable', 'CSP did not provide a complete bounded setup snapshot. Attach the setup manually in desktop ADT.')
      end
      local after = identity()
      if not after or not sameSession(before, after) or after.simTimeMs < before.simTimeMs
        or after.sessionTimeMs < before.sessionTimeMs or after.frame < before.frame
        or generation ~= self.sessionGeneration or revision ~= self.setupRevision then
        return failure('capture_changed', 'The session or setup changed during capture. ADT must request a fresh snapshot.')
      end
      lastIdentity = after
      local patch = ''
      if capability('getPatchVersion') then
        local patchOk, value = pcall(api.getPatchVersion)
        if patchOk and type(value) == 'string' and #value <= 80 and not value:find('%c') then patch = value end
      end
      return { available = true, protocolVersion = 1, source = 'csp-current-setup', setupIni = ini,
        carId = after.carId, trackId = after.trackId, trackLayout = after.trackLayout,
        sessionIndex = after.sessionIndex, sessionType = after.sessionType,
        sessionGeneration = generation, setupRevision = revision, captureSequence = self.sequence,
        simTimeMs = after.simTimeMs, sessionTimeMs = after.sessionTimeMs, frame = after.frame,
        isOnlineRace = after.isOnlineRace, cspVersion = patch }
    end)
    if ok then return result end
    -- SDK error text can contain local paths. Do not forward it or retain old data.
    return failure('capture_failed', 'CSP could not read the current setup. ADT must request another snapshot or use a manual attachment.')
  end

  return c
end
