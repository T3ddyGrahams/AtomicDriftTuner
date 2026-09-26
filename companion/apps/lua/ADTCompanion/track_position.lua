-- Optional, read-only local position evidence. No HTTP, setup or FFB operations.
-- This wire layout is shared with TrackPositionReader.cs in desktop ADT.
return function(ac, clock)
  clock = clock or function() return os.preciseGameTime() end
  local request, response
  local lastToken, retry, idle = 0, 0, 0
  local status = 'Waiting for desktop track capture.'
  local layout = [[
    int sequence; int protocol; int token; int valid;
    double sourceTime; double sourceAge;
    double x; double y; double z; double progress;
    double splineX; double splineY; double splineZ; double left; double right;
    char track[128]; char trackLayout[128]; char car[128];
  ]]
  local function identity(buffer, text, allowEmpty)
    text = type(text) == 'string' and text or ''
    local valid = #text <= 100 and not text:find('[^%w_%.%-]') and (allowEmpty or #text > 0)
    for i = 0, 127 do buffer[i] = 0 end
    if not valid then return false end
    for i = 1, #text do buffer[i - 1] = text:byte(i) end
    return true
  end
  local function finite(n) return type(n) == 'number' and n == n and math.abs(n) < 1000000 end
  local self = {}
  function self:update(dt)
    idle = idle + (finite(dt) and math.max(0, dt) or 0)
    retry = math.max(0, retry - (finite(dt) and math.max(0, dt) or 0))
    if retry > 0 then return end
    local ok = pcall(function()
      -- Opening maps is optional: unsupported CSP or an absent desktop must not interrupt the normal companion.
      if not request then request = ac.readMemoryMappedFile('ADT.TrackPosition.Request.v1', 'int token; int reserved;') end
      if not response then response = ac.writeMemoryMappedFile('ADT.TrackPosition.Position.v1', layout) end
      local token = tonumber(request.token)
      if not token or token <= 0 or token == lastToken then
        if idle > 0.5 then status = 'Waiting for desktop track capture.' end
        return
      end
      idle = 0
      lastToken = token
      local car, sim = ac.getCar(0), ac.getSim()
      response.sequence = (tonumber(response.sequence) + 2) % 2000000000 + 1
      if response.sequence % 2 == 0 then response.sequence = response.sequence + 1 end
      response.protocol, response.token, response.valid = 1, token, 0
      response.sourceTime = car.timestamp
      response.sourceAge = (clock() - car.timestamp) / 1000
      response.x, response.y, response.z = car.position.x, car.position.y, car.position.z
      response.progress = -1
      response.splineX, response.splineY, response.splineZ = 0, 0, 0
      response.left, response.right = -1, -1
      local ids = identity(response.track, ac.getTrackID(), false)
      ids = identity(response.trackLayout, ac.getTrackLayout(), true) and ids
      ids = identity(response.car, ac.getCarID(0), false) and ids
      if ids and car.physicsAvailable and not sim.isPaused and not sim.isReplayActive
        and finite(car.position.x) and finite(car.position.y) and finite(car.position.z)
        and response.sourceAge >= 0 and response.sourceAge <= 0.05 then
        response.valid = 1
        -- Optional AI context is never adopted as the driver's intended drift line.
        pcall(function()
          if not ac.hasTrackSpline() then return end
          local progress = ac.worldCoordinateToTrackProgress(car.position)
          if not finite(progress) or progress < 0 or progress > 1 then return end
          local center = ac.trackProgressToWorldCoordinate(progress, true)
          if not finite(center.x) or not finite(center.y) or not finite(center.z) then return end
          local dx, dy, dz = center.x - car.position.x, center.y - car.position.y, center.z - car.position.z
          if dx*dx + dz*dz > 10000 or math.abs(dy) > 10 then return end
          response.progress = progress
          response.splineX, response.splineY, response.splineZ = center.x, center.y, center.z
          local sides = ac.getTrackAISplineSides(progress)
          if finite(sides.x) and finite(sides.y) and sides.x > 0.1 and sides.x < 100 and sides.y > 0.1 and sides.y < 100 then
            response.left, response.right = sides.x, sides.y
          end
        end)
      end
      response.sequence = response.sequence + 1
      status = response.valid == 1 and 'Position capture active.' or 'Position unavailable, paused, replayed or stale.'
    end)
    if not ok then
      status = 'Position unavailable. Desktop ADT and compatible CSP are needed; normal companion controls remain available.'
      request, retry = nil, 0.5
    end
  end
  function self:status() return status end
  return self
end
