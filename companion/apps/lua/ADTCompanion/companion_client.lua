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
    local ok = pcall(request, body and 'POST' or 'GET', 'http://127.0.0.1:' .. self.port .. path,
      headers, body and encode(body) or nil, function(err, response)
        if generation ~= self.generation then return end -- timed out, disconnected or replaced
        self.busy = false
        self.nextPoll = self.now + 1
        if err or not response then
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
          self.message = response.status == 404 and 'Update desktop ADT to 0.9.0-preview.4 or newer.'
            or data.error or data.message or 'ADT could not complete this request.'
          if body and path ~= '/api/pair' then self.commandMessage = self.message end
          return
        end
        callback(data)
      end)
    if not ok then
      self.busy, self.online, self.status = false, false, nil
      self.message = 'Could not contact ADT. Check CSP networking and the desktop Remote server.'
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
    local allowed = (action == 'start' and r.canStart) or (action == 'stop' and r.canStop) or (action == 'save' and r.canSave)
    if not allowed then return false end
    self.commandMessage = 'Waiting for ADT...'
    -- Invalidate cached buttons until authoritative status is read again. Never retry a mutation.
    self.online = false
    return self:send('/api/companion/recording', {action = action, windowId = r.windowId,
      sessionId = r.sessionId, controlVersion = r.controlVersion}, function(data)
      self.commandMessage = data.message or 'Command completed; refreshing status.'
      self.status = nil
      self.nextPoll = 0
    end)
  end

  function c:update(dt)
    self.now = self.now + math.max(0, dt or 0)
    if self.busy and self.now - self.started > 8 then
      self.generation = self.generation + 1
      self.busy, self.online, self.status = false, false, nil
      self.message = 'ADT did not respond. Reconnecting; recording commands are never retried automatically.'
      self.commandMessage = 'If you pressed a recording button, its outcome is unknown until status refreshes.'
      self.nextPoll = self.now + 2
    end
    if not self.busy and self.token and self.now >= self.nextPoll then self:poll() end
  end
  return c
end
