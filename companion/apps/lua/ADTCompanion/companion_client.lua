-- Transport/state is separate from CSP drawing so timeout and replay behavior can be tested.
return function(request, encode, decode)
  local c = { port = '5190', token = nil, status = nil, online = false, busy = false,
    message = 'Start ADT Remote on your PC, then enter its pairing code here.',
    commandMessage = '', now = 0, nextPoll = 0, lastStatus = -100, generation = 0 }

  function c:forget()
    self.generation = self.generation + 1
    self.token, self.status, self.online, self.busy = nil, nil, false, false
    self.commandMessage = ''
    self.message = 'Pair with the code shown in desktop ADT Remote.'
  end

  function c:fresh()
    return self.online and self.status ~= nil and self.now - self.lastStatus < 3
  end

  function c:send(path, body, callback)
    if self.busy then return false end
    self.busy, self.started = true, self.now
    self.generation = self.generation + 1
    local generation = self.generation
    local headers = { ['Content-Type'] = 'application/json' }
    if self.token then headers['X-ADT-Token'] = self.token end
    local ok = pcall(function() request(body and 'POST' or 'GET', 'http://127.0.0.1:' .. self.port .. path,
      headers, body and encode(body) or nil, function(err, response)
        if generation ~= self.generation then return end -- timed out, disconnected or replaced
        self.busy = false
        self.nextPoll = self.now + 1
        if err or type(response) ~= 'table' or type(response.status) ~= 'number' then
          self.online, self.status = false, nil
          self.message = 'ADT connection unavailable. Keep desktop ADT and its Remote server running.'
          if body and path ~= '/api/pair' then self.commandMessage = 'Command outcome unknown. Check refreshed status before trying again.' end
          return
        end
        if response.status == 401 then
          self:forget()
          self.message = 'Pairing expired or code incorrect. Enter the current code from ADT Remote.'
          return
        end
        local parsed, data = pcall(decode, response.body or '')
        if not parsed or type(data) ~= 'table' then data = {} end
        if response.status < 200 or response.status >= 300 then
          self.online, self.status = false, nil
          self.message = response.status == 404 and (path == '/api/companion/workflow'
            and 'Update desktop ADT to 0.9.0-preview.13 or newer for in-game findings and comparisons.'
            or 'Update desktop ADT to 0.9.0-preview.4 or newer.')
            or data.error or data.message or 'ADT could not complete this request.'
          if body and path ~= '/api/pair' then self.commandMessage = self.message end
          return
        end
        callback(data)
      end) end)
    if not ok then
      self.busy, self.online, self.status = false, false, nil
      self.message = 'Could not contact ADT. Check CSP networking and the desktop Remote server.'
      if body and path ~= '/api/pair' then self.commandMessage = 'Command outcome unknown. Check refreshed status before trying again.' end
      self.nextPoll = self.now + 3
    end
    return ok
  end

  function c:pair(code, port)
    if self.busy then return end
    if type(code) ~= 'string' or not code:match('^%d%d%d%d%d%d$') then
      self.message = 'Enter the six-digit pairing code from ADT Remote.'; return
    end
    if type(port) ~= 'string' or not port:match('^%d+$') or tonumber(port) < 1024 or tonumber(port) > 65535 then
      self.message = 'Use the port shown in ADT Remote (normally 5190).'; return
    end
    self:forget()
    self.port = tostring(tonumber(port))
    self:send('/api/pair', {code = code}, function(data)
      if data.ok ~= true or type(data.token) ~= 'string' or #data.token < 16 then
        self.message = 'ADT returned an invalid pairing response.'; return
      end
      self.token = data.token -- memory only; never stored in ac.storage or logs
      self.message = 'Paired. Reading recorder status...'
      self.nextPoll = 0
    end)
  end

  function c:poll()
    if not self.token or self.busy then return end
    self:send('/api/companion/status', nil, function(data)
      if data.protocolVersion ~= 1 or type(data.recorder) ~= 'table' then
        self.status, self.online = nil, false
        self.message = 'Desktop ADT and this companion use incompatible versions.'; return
      end
      self.status, self.online, self.lastStatus = data, true, self.now
      self.message = 'Connected to desktop ADT ' .. tostring(data.appVersion or '')
    end)
  end

  function c:command(action)
    if self.busy or not self:fresh() then return false end
    local r = self.status.recorder
    local allowed = (action == 'start' and r.canStart == true) or (action == 'stop' and r.canStop == true) or (action == 'save' and r.canSave == true)
    if not allowed then return false end
    self.commandMessage = 'Waiting for ADT...'
    -- Invalidate cached buttons until authoritative status is read again. Never retry a mutation.
    self.online, self.status = false, nil
    return self:send('/api/companion/recording', {action = action, windowId = r.windowId,
      sessionId = r.sessionId, controlVersion = r.controlVersion}, function(data)
      self.commandMessage = data.message or 'Command completed; refreshing status.'
      self.status = nil
      self.nextPoll = 0
    end)
  end

  function c:workflowState()
    if not self:fresh() then return nil end
    local w = self.status.workflow
    if type(w) ~= 'table' or w.protocolVersion ~= 1 then return nil end
    return w
  end

  function c:textLength(text)
    -- Match the desktop's UTF-16 string limit without cutting a UTF-8 character in half.
    local length = 0
    for i = 1, #text do
      local b = text:byte(i)
      if b < 128 or b >= 192 then length = length + (b >= 240 and 2 or 1) end
    end
    return length
  end

  function c:canWorkflow(action, fields)
    local w = self:workflowState()
    if self.busy or not w or w.available ~= true or type(w.controlVersion) ~= 'string' or #w.controlVersion ~= 64 then return false end
    fields = fields or {}
    if action == 'prepare' then return w.canPrepare == true end
    if action == 'confirm' then return w.canConfirm == true and fields.tuneConfirmed == true end
    if action == 'findings' then return w.canReadFindings == true end
    local report = w.report
    if type(report) ~= 'table' or type(report.sessionId) ~= 'string' or fields.sessionId ~= report.sessionId then return false end
    if action == 'plan' then
      for _, recommendation in ipairs(type(report.recommendations) == 'table' and report.recommendations or {}) do
        if recommendation.id == fields.recommendationId and recommendation.canSelect == true then return true end
      end
    end
    if action == 'review' then
      local ratings = {Better = true, Worse = true, ['No noticeable difference'] = true, Tradeoff = true}
      local decisions = {Undecided = true, ['Keep and verify'] = true, ['Revert manually'] = true, ['Test again'] = true}
      return w.canSaveReview == true and ratings[fields.driverRating] == true and decisions[fields.nextAction] == true
        and type(fields.notes) == 'string' and self:textLength(fields.notes) <= 2000
    end
    return false
  end

  function c:workflow(action, fields)
    fields = fields or {}
    if not self:canWorkflow(action, fields) then return false end
    local w = self:workflowState()
    local body = {action = action, controlVersion = w.controlVersion}
    if action == 'confirm' then body.tuneConfirmed = fields.tuneConfirmed end
    if action == 'findings' then body.sessionId = w.savedSessionId end
    if action == 'plan' then body.sessionId, body.recommendationId = fields.sessionId, fields.recommendationId end
    if action == 'review' then
      body.sessionId, body.driverRating, body.nextAction, body.notes = fields.sessionId, fields.driverRating, fields.nextAction, fields.notes
    end
    self.commandMessage = action == 'findings' and 'Reading the saved run in ADT...' or 'Waiting for ADT...'
    -- A plan/review can change the recorder as well. Invalidate EVERY cached button immediately.
    self.online, self.status = false, nil
    return self:send('/api/companion/workflow', body, function(data)
      self.commandMessage = type(data.message) == 'string' and data.message or 'Request completed; refreshing status.'
      self.status = nil
      self.nextPoll = 0
    end)
  end

  function c:update(dt)
    self.now = self.now + math.max(0, dt or 0)
    if self.busy and self.now - self.started > 8 then
      self.generation = self.generation + 1
      self.busy, self.online, self.status = false, false, nil
      self.message = 'ADT did not respond. Reconnecting; commands are never retried automatically.'
      self.commandMessage = 'If you pressed a run or workflow button, its outcome is unknown until status refreshes.'
      self.nextPoll = self.now + 2
    end
    if not self.busy and self.token and self.now >= self.nextPoll then self:poll() end
  end
  return c
end
