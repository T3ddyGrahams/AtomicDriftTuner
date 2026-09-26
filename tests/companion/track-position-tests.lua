-- LuaJIT fixtures for the actual integrated position module and C wire layout.
local ffi=require('ffi')
local checks=0
local function check(ok,why) if not ok then error(why) end checks=checks+1 end
script={}
local request=ffi.new('struct { int token; int reserved; }')
local response
local car={timestamp=1000,position={x=10,y=0,z=20},physicsAvailable=true}
local sim={isPaused=false,isReplayActive=false}
local track,trackLayout,carID='test_track','layout_a','test_car'
local sourceAge,hasSpline,sides=5,false,{x=5,y=6}
ac={
 writeMemoryMappedFile=function(name,layout)
  check(name=='ADT.TrackPosition.Position.v1','Wrong production map')
  response=ffi.new('struct {'..layout..'}')
  check(ffi.sizeof(response)==488,'C# wire size mismatch')
  for name,offset in pairs({sequence=0,protocol=4,token=8,valid=12,sourceTime=16,sourceAge=24,x=32,y=40,z=48,progress=56,splineX=64,splineY=72,splineZ=80,left=88,right=96,track=104,trackLayout=232,car=360}) do
   check(ffi.offsetof(ffi.typeof(response),name)==offset,'C# offset mismatch: '..name)
  end
  return response
 end,
 readMemoryMappedFile=function(name,layout) check(name=='ADT.TrackPosition.Request.v1','Wrong request map');return request end,
 getCar=function(index) check(index==0,'Wrong car');return car end,
 getSim=function() return sim end,
 getTrackID=function()return track end,getTrackLayout=function()return trackLayout end,getCarID=function()return carID end,
 hasTrackSpline=function()return hasSpline end,
 worldCoordinateToTrackProgress=function()return .5 end,
 trackProgressToWorldCoordinate=function()return {x=11,y=0,z=21} end,
 getTrackAISplineSides=function()return sides end
}
os.preciseGameTime=function()return car.timestamp+sourceAge end
local messages={}
ui={text=function(s)table.insert(messages,s)end,textWrapped=function(s)table.insert(messages,s)end}
local publisher = assert(loadfile(arg[1]))()(ac)
local function tick() request.token=request.token+1;publisher:update(.02);check(response.sequence%2==0,'Torn completed frame');check(response.token==request.token,'Missing echo') end
tick();check(response.valid==1,'Valid capture missing');check(response.progress==-1 and response.left==-1,'Missing spline invented')
check(ffi.string(response.track)=='test_track' and ffi.string(response.car)=='test_car','Identity lost')
local seq=response.sequence;publisher:update(.02);check(seq==response.sequence,'Reused request published again')
hasSpline=true;tick();check(response.progress==.5 and response.left==5 and response.right==6,'Optional AI context missing')
sides={x=0/0,y=0};tick();check(response.valid==1 and response.left==-1 and response.right==-1,'Bad sides corrupted position or invented width')
ac.trackProgressToWorldCoordinate=function()error('unsupported mod spline')end
tick();check(response.valid==1 and response.progress==-1,'Missing mod API destroyed core position')
sourceAge=100;tick();check(response.valid==0,'Stale physics allowed');sourceAge=5
sim.isPaused=true;tick();check(response.valid==0,'Paused position allowed');sim.isPaused=false
sim.isReplayActive=true;tick();check(response.valid==0,'Replay position allowed');sim.isReplayActive=false
car.physicsAvailable=false;tick();check(response.valid==0,'Unavailable physics allowed');car.physicsAvailable=true
car.position.x=0/0;tick();check(response.valid==0,'NaN position allowed');car.position.x=10
track='../other';tick();check(response.valid==0,'Unsafe identity accepted');track='test_track'
trackLayout=string.rep('a',128);tick();check(response.valid==0,'Truncated layout accepted')
trackLayout=nil;tick();check(response.valid==1 and ffi.string(response.trackLayout)=='','Default layout missing')
check(publisher:status():find('active'), 'Capture status missing')
publisher:update(.6);check(publisher:status():find('Waiting'), 'Stale request still shown active')
tick();check(publisher:status():find('active'), 'Fresh request did not resume capture')
local unavailable = assert(loadfile(arg[1]))()({})
check(pcall(function() unavailable:update(.02) end), 'Unsupported CSP escaped into companion update')
check(unavailable:status():find('unavailable'), 'Missing API status absent')
local attempts=0
local retrying = assert(loadfile(arg[1]))()({readMemoryMappedFile=function() attempts=attempts+1;error('Desktop absent') end})
retrying:update(.02);retrying:update(.02);check(attempts==1, 'Unavailable map retried every frame')
retrying:update(.6);check(attempts==2, 'Unavailable map never retried')
print('PASS '..checks..' position Lua assertions; exact 488-byte layout, mocked CSP, no game writes.')
