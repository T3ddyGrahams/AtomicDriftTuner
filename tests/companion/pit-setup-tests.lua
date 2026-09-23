local create = assert(loadfile(arg[1]))()
local count = 0
local function check(value, why) assert(value, why); count = count + 1 end
local original = '; keep this comment\r\n[CAR]\r\nMODEL=test_car\r\n[CAMBER_LF]\r\n VALUE = -30 ; caster untouched\r\n[PRESSURE_LF]\r\nVALUE=28\r\n[OTHER]\r\nTEXT=leave me alone\r\n'
local function fixture()
  local f = {current=original, saves={}, loads={}, editable=true,
    sim={isLive=true,isPaused=false,isReplayActive=false,isReplayOnlyMode=false,isShowroomMode=false,
      isPreviewsGenerationMode=false,isOnlineRace=false,currentSessionIndex=0,raceSessionType=1,frame=10,time=100,currentSessionTime=100},
    car={index=0,isConnected=true,isActive=true,isRemote=false,isAIControlled=false,physicsAvailable=true,isInPit=true,speedKmh=0},
    carId='test_car',track='track',layout='',
    spinner={name='CAMBER_LF',min=-60,max=0,step=5,value=-30,visible=true,readOnly=false}}
  f.api={getSim=function() return f.sim end,getCar=function(i) assert(i==0);return f.car end,
    FolderID={UserSetups=11},getFolder=function(id) assert(id==11);return 'fake-setups' end,
    getCarID=function() return f.carId end,getTrackID=function() return f.track end,getTrackLayout=function() return f.layout end,
    isSetupAvailableToEdit=function() return f.editable end,
    getSetupSpinners=function() return {f.spinner,{name='PRESSURE_LF',min=10,max=40,step=1,value=28,visible=true,readOnly=false}} end,
    stringifyCurrentSetup=function(metadata,modifiedOnly)
      assert(metadata==false and modifiedOnly==false)
      if f.duringRead then f.duringRead() end
      return f.current
    end,
    onSessionStart=function(callback) f.sessionStart=callback; return {} end,
    saveCurrentSetup=function(name)
      check(name:match('^generic/ADT_[A-Za-z]+_%x+%.ini$') and not name:find('test_car'), 'server path or car name entered filename')
      f.saves[#f.saves+1]={name=name,ini=f.current}
      if f.duringSave then f.duringSave() end
      return f.saveResult ~= false
    end,
    loadSetup=function(contents)
      check(contents:find('[CAMBER_LF]',1,true), 'load used a filesystem path')
      f.loads[#f.loads+1]=contents
      f.current=f.loadedOverride or contents
      f.spinner.value=tonumber(f.current:match('VALUE%s*=%s*([%d-]+)') or f.current:match('VALUE = ([%d-]+)'))
      if f.duringLoad then f.duringLoad() end
      return f.loadResult ~= false
    end,
    refreshSetups=function() f.refreshed=true;return true end}
  f.plan={protocolVersion=1,planId=string.rep('a',32),carId='test_car',label='Small change',
    baselineValues={CAMBER_LF=-30,PRESSURE_LF=28},changes={{section='CAMBER_LF',before=-30,after=-25}}}
  f.fs={exists=function(path)
    check(path:match('^fake%-setups/test_car/generic/ADT_[A-Za-z]+_%x+%.ini$'), 'existence check escaped generated setup filenames')
    if f.existsOverride then return f.existsOverride(path) end
    for _,saved in ipairs(f.saves) do if path=='fake-setups/test_car/'..saved.name then return true end end
    return false
  end}
  return f,create(f.api,f.fs)
end
local function apply(f,c,key)
  local ticket,reason=c:prepare(f.plan,'apply'); check(ticket,reason or 'prepare failed')
  return c:execute(f.plan,key or 'command-1',ticket),ticket
end
local f,c=fixture()
check(#f.saves==0 and #f.loads==0,'module initialization changed the game')
local out,ticket=apply(f,c)
check(out.success and out.state=='applied' and #f.saves==2 and #f.loads==1 and f.refreshed,'successful save/apply did not complete')
check(f.saves[1].ini==original and f.saves[1].name~=f.saves[2].name,'backup not first, or overwritten')
check(f.current==original:gsub('VALUE = %-30','VALUE = -25'),'unapproved content changed')
check(c.previous and c.previous.ini==original and c.previous.planId==f.plan.planId,'original backup unavailable')
check(not c:execute(f.plan,'command-1',ticket).success and #f.loads==1,'command replay loaded twice')
local restore=c:prepare(nil,'restore');check(restore,'explicit restore unavailable')
out=c:execute(nil,'command-2',restore)
check(out.success and out.state=='restored' and f.current==original and #f.loads==2 and #f.saves==4,'restore did not verify/save original')
check(f.saves[3].name~=f.saves[1].name and f.saves[3].name~=f.saves[2].name,'restore reused an existing backup filename')

-- Runtime CSP getCar is a callable table, not a Lua function.
f,c=fixture()
local getCar=f.api.getCar
f.api.getCar=setmetatable({}, {__call=function(_,index) return getCar(index) end})
out=apply(f,c)
check(out.success and #f.saves==2 and #f.loads==1,'CSP callable-table getCar blocked guarded apply')
f.car.speedKmh=10
check(c:prepare(nil,'restore')==nil and #f.loads==1,'callable getCar bypassed parked-car guards')
for _,bad in ipairs({{},setmetatable({}, {__call=false}),setmetatable({}, {__call='invalid'}),setmetatable({}, {__call={}})}) do
  f,c=fixture(); f.api.getCar=bad
  check(c:prepare(f.plan,'apply')==nil and #f.saves==0 and #f.loads==0,
    'non-callable getCar table passed guarded setup capability checks')
end

for _,name in ipairs({'getSim','getCar','getCarID','getTrackID','getTrackLayout','isSetupAvailableToEdit','getSetupSpinners','stringifyCurrentSetup','saveCurrentSetup','loadSetup','getFolder'}) do
  f,c=fixture();f.api[name]=nil
  check(c:prepare(f.plan,'apply')==nil and #f.saves==0 and #f.loads==0,'missing capability allowed: '..name)
end
for _,name in ipairs({'isLive','isPaused','isReplayActive','isReplayOnlyMode','isShowroomMode','isPreviewsGenerationMode','isOnlineRace'}) do
  f,c=fixture();f.sim[name]=nil
  check(c:prepare(f.plan,'apply')==nil,'unknown sim guard accepted: '..name)
end
for _,name in ipairs({'isConnected','isActive','physicsAvailable','isInPit'}) do
  f,c=fixture();f.car[name]=false
  check(c:prepare(f.plan,'apply')==nil,'unsafe car flag accepted: '..name)
end
for _,name in ipairs({'isRemote','isAIControlled'}) do
  f,c=fixture();f.car[name]=true
  check(c:prepare(f.plan,'apply')==nil,'unsafe player guard accepted: '..name)
end
f,c=fixture();f.car.speedKmh=0.2;check(c:prepare(f.plan,'apply')==nil,'moving car accepted')
f,c=fixture();f.car.speedKmh=0/0;check(c:prepare(f.plan,'apply')==nil,'NaN speed accepted')
f,c=fixture();f.editable=false;check(c:prepare(f.plan,'apply')==nil,'fixed/closed setup accepted')
f,c=fixture();f.carId='other';check(c:prepare(f.plan,'apply')==nil,'wrong car accepted')
f,c=fixture();f.sim.isPaused=true;check(c:prepare(f.plan,'apply')==nil,'paused session accepted')
f,c=fixture();f.sim.isReplayActive=true;check(c:prepare(f.plan,'apply')==nil,'replay accepted')

for _,bad in ipairs({'[CAMBER_LF]\nVALUE=-30\n[CAMBER_LF]\nVALUE=-30\n','[CAMBER_LF]\nVALUE=-30\nVALUE=-30\n',
    '[CAMBER_LF]\nVALUE=NaN\n','[CAMBER_LF]\nVALUE=1e999\n','[CAMBER_LF]\nVALUE=0x20\n',
    'VALUE=-30\n','[CAMBER_LF]\nVALUE=-30\0\n',string.rep('x',65537)}) do
  f,c=fixture();f.current=bad;check(c:prepare(f.plan,'apply')==nil,'ambiguous/malformed snapshot accepted')
end
for _,field in ipairs({'visible','readOnly','step','min','value'}) do
  f,c=fixture();f.spinner[field]=nil;check(c:prepare(f.plan,'apply')==nil,'unknown spinner metadata accepted: '..field)
end
f,c=fixture();f.spinner.readOnly=true;check(c:prepare(f.plan,'apply')==nil,'read-only spinner accepted')
f,c=fixture();f.spinner.visible=false;check(c:prepare(f.plan,'apply')==nil,'hidden spinner accepted')
f,c=fixture();f.plan.changes[1].after=-24;check(c:prepare(f.plan,'apply')==nil,'off-step change accepted')
f,c=fixture();f.plan.changes[1].after=10;check(c:prepare(f.plan,'apply')==nil,'out-of-range change accepted')
f,c=fixture();f.plan.changes[2]=f.plan.changes[1];check(c:prepare(f.plan,'apply')==nil,'duplicate change accepted')
f,c=fixture();f.plan.baselineValues.PRESSURE_LF=29;check(c:prepare(f.plan,'apply')==nil,'unchanged baseline drift accepted')
f,c=fixture();f.plan.baselineValues.PRESSURE_LF=nil;out=apply(f,c)
check(out.success and f.current:find('VALUE=28',1,true),'extra live setup values were not retained and verified')
f,c=fixture();f.plan.baselineValues.CAMBER_LF=nil;check(c:prepare(f.plan,'apply')==nil,'changed section absent from baseline accepted')
f,c=fixture();f.plan.changes[1].section='../bad';check(c:prepare(f.plan,'apply')==nil,'section path injection accepted')
f,c=fixture();f.plan.planId='../bad';check(c:prepare(f.plan,'apply')==nil,'invalid plan ID accepted')
f,c=fixture();f.plan.changes[1].before=-35;check(c:prepare(f.plan,'apply')==nil,'changed before value accepted')
f,c=fixture();f.plan.changes[1].after=0/0;check(c:prepare(f.plan,'apply')==nil,'NaN change accepted')
f,c=fixture();f.spinner.itemValues={-30,-25};check(c:prepare(f.plan,'apply')~=nil,'valid LUT value rejected')
f.spinner.itemValues={-30,-20};check(c:prepare(f.plan,'apply')==nil,'invalid LUT value accepted')

for _,change in ipairs({function(x) x.current=x.current:gsub('VALUE=28','VALUE=29') end,
    function(x) x.carId='changed' end,function(x) x.sessionStart() end,function(x) x.sim.frame=0 end,
    function(x) x.editable=false end,function(x) x.car.speedKmh=10 end}) do
  f,c=fixture();ticket=c:prepare(f.plan,'apply');change(f);out=c:execute(f.plan,'once',ticket)
  check(not out.success and #f.saves==0 and #f.loads==0,'game changed during authorization but mutation proceeded')
end
f,c=fixture();ticket=c:prepare(f.plan,'apply');f.plan.changes[1].after=-20
out=c:execute(f.plan,'changed-plan',ticket);check(not out.success and #f.saves==0,'lease changed the explicitly reviewed plan')
f,c=fixture();f.saveResult=false;out=apply(f,c)
check(not out.success and #f.loads==0 and c.previous==nil,'failed backup still mutated or replaced previous')
f,c=fixture();f.duringSave=function() f.current=f.current:gsub('VALUE=28','VALUE=29') end;out=apply(f,c)
check(not out.success and #f.loads==0,'setup changed during backup but load continued')
f,c=fixture();f.loadResult=false;out=apply(f,c)
check(not out.success and out.state=='verification-failed' and c.previous and #f.loads==1,'failed load retried/restored automatically or lost backup')
f,c=fixture();f.loadedOverride=original:gsub('VALUE = %-30','VALUE = -25'):gsub('VALUE=28','VALUE=29');out=apply(f,c)
check(not out.success and out.state=='verification-failed' and #f.loads==1 and #f.saves==1,'unexpected unrelated value change was not detected')
f,c=fixture();f.duringLoad=function() f.sim.currentSessionIndex=1 end;out=apply(f,c)
check(not out.success and out.state=='verification-failed' and c:prepare(nil,'restore')==nil,'session changed during load or restore crossed sessions')
f,c=fixture();f.duringSave=function() if #f.saves==2 then f.current=f.current:gsub('VALUE=28','VALUE=29') end end;out=apply(f,c)
check(not out.success and out.state=='verification-failed','final save changed setup without detection')
f,c=fixture();f.duringLoad=function() f.saveResult=false end;out=apply(f,c)
check(not out.success and c.previous and #f.loads==1,'new tune save failure repeated mutation or lost backup')
f,c=fixture();f.api.saveCurrentSetup=function() error('C:/private/user/secret.ini') end;out=apply(f,c)
check(not out.success and not out.message:find('private') and #f.loads==0,'API error leaked path or changed setup')
f,c=fixture();f.api.loadSetup=function() error('C:/private/user/secret.ini') end;out=apply(f,c)
check(not out.success and out.state=='verification-failed' and c.previous and not out.message:find('private'),'load error leaked path or lost restore')
f,c=fixture();apply(f,c);f.sessionStart();check(c:prepare(nil,'restore')==nil,'restore crossed session generation')
f,c=fixture();f.existsOverride=function() return true end;out=apply(f,c)
check(not out.success and #f.saves==0 and #f.loads==0,'existing backup files overwritten')
f,c=fixture();local collisions=0;f.existsOverride=function() collisions=collisions+1;return collisions==1 end;out=apply(f,c)
check(out.success and collisions>4,'colliding filename was not regenerated')
f,c=fixture();f.fs.exists=nil;check(c:prepare(f.plan,'apply')==nil,'missing collision check allowed a mutation')
-- GT86-style camber: saved -55 corresponds to -5.5 in the car definition.
-- Runtime spinners expose raw saved units; never rescale or bypass their live bounds.
f,c=fixture();f.current=original:gsub('VALUE = %-30','VALUE = -55')
f.spinner.min=-100;f.spinner.max=-20;f.spinner.step=1;f.spinner.value=-55;f.spinner.displayMultiplier=0.1;f.spinner.showClicksMode=1
f.plan.baselineValues.CAMBER_LF=-55;f.plan.changes[1].before=-55;f.plan.changes[1].after=-56
out=apply(f,c);check(out.success and f.current:find('VALUE = -56',1,true),'legal camber tenths did not save/apply')
restore=c:prepare(nil,'restore');check(restore,'camber restore unavailable');out=c:execute(nil,'camber-restore',restore)
check(out.success and f.current:find('VALUE = -55',1,true),'camber restore changed serialization')
for _,invalid in ipairs({'lower','upper','fraction','physical-range','readonly'}) do
  f,c=fixture();f.current=original:gsub('VALUE = %-30','VALUE = -55')
  f.spinner.min=-100;f.spinner.max=-20;f.spinner.step=1;f.spinner.value=-55
  f.plan.baselineValues.CAMBER_LF=-55;f.plan.changes[1].before=-55;f.plan.changes[1].after=-56
  if invalid=='lower' then f.plan.changes[1].after=-101 elseif invalid=='upper' then f.plan.changes[1].after=-19
  elseif invalid=='fraction' then f.plan.changes[1].after=-55.5
  elseif invalid=='physical-range' then f.spinner.min=-10;f.spinner.max=-2
  else f.spinner.readOnly=true end
  check(c:prepare(f.plan,'apply')==nil and #f.loads==0 and #f.saves==0,'camber runtime guard bypassed: '..invalid)
end
print('PASS '..count..' pit setup assertions: guards, unique backup, precise patch, full readback, failure preservation and explicit restore.')
