local create = assert(loadfile(arg[1]))()
local count = 0
local function check(ok, reason) assert(ok, reason); count = count + 1 end
local function fixture()
  local f = { sim = {isLive=true,isPaused=false,isReplayActive=false,isReplayOnlyMode=false,
      isShowroomMode=false,isPreviewsGenerationMode=false,isOnlineRace=false,
      currentSessionIndex=0,raceSessionType=1,frame=20,time=400,currentSessionTime=200},
    car = {index=0,isConnected=true,isActive=true,isRemote=false,isAIControlled=false,physicsAvailable=true},
    carId='test_car',trackId='test_track',trackLayout='layout_a',
    current='[CAMBER_LF]\nVALUE=-30\n',saved='[CAMBER_LF]\nVALUE=-10\n', reads=0, fileReads=0 }
  f.api = {
    getSim=function() return f.sim end, getCar=function(index) assert(index==0); return f.car end,
    getCarID=function(index) assert(index==0); return f.carId end,
    getTrackID=function() return f.trackId end, getTrackLayout=function() return f.trackLayout end,
    getPatchVersion=function() return 'test-csp' end,
    stringifyCurrentSetup=function(metadata,modifiedOnly)
      assert(metadata==false and modifiedOnly==false,'capture did not request all current values without metadata')
      f.reads=f.reads+1
      if f.duringRead then f.duringRead() end
      return f.current
    end,
    onSetupFile=function(callback) f.onSetup=callback; return {subscription='setup'} end,
    onSessionStart=function(callback) f.onSession=callback; return {subscription='session'} end,
    INIConfig={currentSetup=function() f.fileReads=f.fileReads+1; return f.saved end},
    setSetupSpinnerValue=function() error('capture attempted a setup write') end,
    loadSetup=function() error('capture attempted a setup load') end
  }
  return f, create(f.api)
end

local f,c=fixture()
local first=c:capture()
check(first.available and first.setupIni==f.current and first.setupIni~=f.saved,'unsaved current setup was not captured')
check(f.fileReads==0 and f.reads==1,'capture used a saved file or no fresh serializer call')
check(first.source=='csp-current-setup' and first.protocolVersion==1 and first.carId=='test_car'
  and first.trackId=='test_track' and first.trackLayout=='layout_a' and first.frame==20
  and first.sessionIndex==0 and first.sessionType==1 and first.sessionTimeMs==200
  and first.simTimeMs==400 and first.isOnlineRace==false,'live identity metadata missing')
check(#c.subscriptions==2,'CSP subscription lifetime was not retained')
f.current='[CAMBER_LF]\nVALUE=-45\n' -- unsaved spinner edit: deliberately no onSetupFile event
local second=c:capture()
check(second.available and second.setupIni==f.current and second.captureSequence==first.captureSequence+1
  and second.setupRevision==first.setupRevision and f.reads==2,'second capture returned cached setup after unsaved edit')
f.onSetup('save','private-user-path.ini')
check(c:capture().setupRevision==first.setupRevision+1 and f.fileReads==0,'setup save event did not invalidate without reading saved file')
f.onSetup('load','another-private-path.ini')
check(c:capture().setupRevision==first.setupRevision+2,'setup load event did not invalidate')
f.onSession(0,true)
local restarted=c:capture()
check(restarted.sessionGeneration>second.sessionGeneration and restarted.setupRevision>second.setupRevision,'restart kept old identity')
f.sim.isOnlineRace=true
check(c:capture().available,'live online player session was not supported')

for _, name in ipairs({'stringifyCurrentSetup','getSim','getCar','getCarID','getTrackID','getTrackLayout'}) do
  f,c=fixture(); f.api[name]=nil
  local result=c:capture()
  check(not result.available and result.code=='unsupported_csp' and not result.setupIni and f.fileReads==0,'missing SDK capability fell back: '..name)
end
f,c=fixture(); f.api.onSetupFile=nil; f.api.onSessionStart=nil; f.api.getPatchVersion=nil; c=create(f.api)
check(c:capture().available,'optional hooks/version incorrectly required')

for _, field in ipairs({'isReplayActive','isReplayOnlyMode','isShowroomMode','isPreviewsGenerationMode'}) do
  f,c=fixture(); f.sim[field]=true
  local result=c:capture()
  check(not result.available and not result.setupIni and f.reads==0,'non-driving session captured: '..field)
end
for _, field in ipairs({'isLive','isPaused','isReplayActive','isReplayOnlyMode','isShowroomMode','isPreviewsGenerationMode','isOnlineRace'}) do
  f,c=fixture(); f.sim[field]=nil
  check(not c:capture().available,'unknown simulation metadata accepted: '..field)
end
f,c=fixture(); f.sim.isPaused=true; check(not c:capture().available,'paused capture accepted')
for _, field in ipairs({'isConnected','isActive','physicsAvailable'}) do
  f,c=fixture(); f.car[field]=false; check(not c:capture().available,'non-live car captured: '..field)
end
for _, field in ipairs({'isRemote','isAIControlled'}) do
  f,c=fixture(); f.car[field]=true; check(not c:capture().available,'non-player car captured: '..field)
end
f,c=fixture(); f.car.index=1; check(not c:capture().available,'spectated car accepted as player')
f,c=fixture(); f.api.getCar=function() return nil end; check(not c:capture().available,'missing player car accepted')
f,c=fixture(); f.api.getSim=function() error('private/path/runtime-error') end
local unavailable=c:capture()
check(not unavailable.available and unavailable.code=='capture_failed' and not unavailable.message:find('private/path'),'SDK exception leaked details')

for _, invalid in ipairs({'','../car','car/other','car\\other','car\nother',string.rep('x',129)}) do
  f,c=fixture(); f.carId=invalid; check(not c:capture().available,'unsafe car ID accepted')
end
f,c=fixture(); f.trackLayout=''; check(c:capture().available,'base track without a layout was rejected')
f,c=fixture(); f.trackLayout=nil; check(not c:capture().available,'unknown track layout was assumed empty')
for _, field in ipairs({'frame','time','currentSessionTime','currentSessionIndex','raceSessionType'}) do
  f,c=fixture(); f.sim[field]=0/0; check(not c:capture().available,'non-finite session identity accepted: '..field)
end

for _, invalid in ipairs({'','no INI sections','[CAMBER_LF]\nNOTE=2\n','[CAMBER_LF]\nVALUE=2\0',string.rep('x',65537)}) do
  f,c=fixture(); f.current=invalid
  local result=c:capture()
  check(not result.available and result.code=='setup_unavailable' and not result.setupIni,'invalid/oversized setup returned')
end
f,c=fixture(); f.current=nil; check(not c:capture().available,'nil serializer result accepted')
f,c=fixture(); f.current={VALUE=2}; check(not c:capture().available,'non-string serializer result accepted')
f,c=fixture(); f.current='[CAMBER_LF]\r\nVALUE=-30\r\n[PRESSURE_LF]\r\nVALUE=26\r\n'
check(c:capture().setupIni==f.current,'capture changed raw current setup values')
f,c=fixture(); local full='[CAMBER_LF]\nVALUE=-30\n'; f.current=full..string.rep(';',65536-#full)
check(c:capture().available,'exact 64 KiB snapshot was rejected')

for _, change in ipairs({
  function(a) a.carId='other_car' end,
  function(a) a.trackLayout='other_layout' end,
  function(a) a.sim.currentSessionIndex=1 end,
  function(a) a.sim.currentSessionTime=0 end,
  function(a) a.sim.frame=0 end,
  function(a) a.sim.isReplayActive=true end,
  function(a) a.onSetup('load','private.ini') end,
  function(a) a.onSession(0,true) end
}) do
  f,c=fixture(); f.duringRead=function() change(f) end
  local result=c:capture()
  check(not result.available and result.code=='capture_changed' and not result.setupIni,'capture racing a session/setup change accepted')
end
f,c=fixture(); local old=c:capture(); f.api.onSessionStart=nil; f.sim.currentSessionTime=0
check(c:capture().sessionGeneration>old.sessionGeneration,'unannounced session clock rewind kept old identity')
f,c=fixture(); local successful=c:capture(); f.sim.isReplayActive=true
local failed=c:capture()
check(successful.available and not failed.available and failed.setupIni==nil,'unavailable capture reused last good payload')
f.sim.isReplayActive=false; f.current='[CAMBER_LF]\nVALUE=-50\n'
local recovered=c:capture()
check(recovered.available and recovered.setupIni==f.current and recovered.captureSequence>failed.captureSequence,'recovery reused stale snapshot/sequence')

print('PASS '..count..' read-only CSP setup capture assertions; unsaved edits, identity, bounds, replay, restart and failure invalidation.')
