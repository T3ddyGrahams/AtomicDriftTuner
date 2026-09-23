local create = assert(loadfile(arg[1]))()
local pending, text, pressed, disabled = nil, {}, nil, 0
local activeTab, childDepth, requests, children, count = 'Record', 0, {}, {}, 0
local enabled, notesInput = {}, nil
local preferences
local function check(value, message) assert(value, message); count=count+1 end
require = function(name)
  if name=='setup_capture' then return assert(loadfile(arg[3]))() end
  if name=='pit_setup' then return assert(loadfile((arg[3]:gsub('setup_capture.lua$', 'pit_setup.lua'))))() end
  assert(name=='companion_client'); return create
end
ac = {storage=function(defaults) preferences=defaults; return preferences end}
JSON = {stringify=function(x) return x end, parse=function(x) return x end}
rgbm = function(...) return {...} end
vec2 = function(x,y) return {x=x,y=y} end
web = {timeouts=function() end, request=function(method,url,headers,body,callback)
  pending={method=method,url=url,body=body,callback=callback}; requests[#requests+1]=pending
end}
ui = {
  text=function(t) text[#text+1]=t end, textWrapped=function(t) text[#text+1]=t end,
  textColored=function(t) text[#text+1]=t end, separator=function() end,
  setNextItemWidth=function() end,
  availableSpace=function() return vec2(300,180) end,
  childWindow=function(id,size,callback)
    assert(size.x==300 and size.y==180,'scroll pane did not use available min-window space')
    children[id]=true; childDepth=childDepth+1; callback(); childDepth=childDepth-1
  end,
  tabBar=function(id,callback) callback() end,
  tabItem=function(label,callback) if label==activeTab then callback() end end,
  treeNode=function(label,callback) callback() end,
  combo=function(label,preview,callback) callback() end,
  selectable=function(label) return pressed==label and disabled==0 end,
  checkbox=function(label) return pressed==label and disabled==0 end,
  inputText=function(label,value)
    if label=='Pairing code' then return '123456' end
    if label=='##reviewNotes' and notesInput then return notesInput end
    return value
  end,
  pushDisabled=function() disabled=disabled+1 end, popDisabled=function() disabled=disabled-1 end,
  button=function(label)
    assert(childDepth>0,'button outside scrollable pane: '..label)
    enabled[label]=disabled==0
    return pressed==label and disabled==0
  end
}
script = {}
assert(loadfile(arg[2]))()
local function draw(click,tab)
  text,enabled={},{}; pressed=click
  if tab then activeTab=tab end
  script.windowMain(0); pressed=nil
  assert(disabled==0 and childDepth==0,'unbalanced UI scopes')
  return table.concat(text,'\n')
end
local function reply(data,status) pending.callback(nil,{status=status or 200,body=data}) end
local function state(withWorkflow)
  local s={protocolVersion=1,appVersion='preview.13',telemetryConnected=true,
    nextStep='Record baseline',instructions='Drive the same section',details='Extra explanation',
    completion='Ready when: Save Session completed',progress='Car > Goals > Prepare > Baseline',
    recorder={car='Test Car',driver='Driver',state='ready',canStart=true,canStop=false,canSave=false,
      windowId='w',sessionId='s',controlVersion='v',elapsedSeconds=10.5,samples=125,
      setupMessage='Current setup captured from CSP: 8 numeric values.',
      evidence={message='Collecting useful drift: 10 / 20 s.',details='Missing time is excluded.'}}}
  if withWorkflow then s.workflow={protocolVersion=1,controlVersion=string.rep('a',64),available=true,
    focus='Car tuning only',car='Test Car',driver='Driver',conditions='Dry, solo transitions',setupName='Baseline.ini',
    confirmationText='I loaded this setup and kept FFB fixed.',comingNext='Next: read findings',
    canPrepare=true,canConfirm=true,canReadFindings=true,canSaveReview=true,savedSessionId='run-b',
    report={sessionId='run-b',baselineSessionId='run-a',goal='Sustain more angle',noticed='Longer hold',
      confidence='Worth testing',instruction='Test one change',why='Repeated evidence',
      recommendations={{id='test-1',domain='Rear grip',change='One small supported change',why='Repeated rear slip',confidence='MEDIUM',canSelect=true}},
      comparison={verdict='Tradeoff',summary='More angle; less speed.',comparable=true,setupChanges={'CAMBER_LF: -30 → -25 (+5)'},limitations={'Different pedal use can affect the comparison.'}}}}
  end
  return s
end
local function refresh(s)
  reply({ok=true,message='Done'}); script.update(0.1); reply(s or state(true))
end
local function poll(s) script.update(1.1); reply(s) end
draw()
check(pending==nil and children.adtPair,'drawing starts traffic before pairing or pairing is not scrollable')
draw('Pair with ADT')
check(pending.body.code=='123456','pairing UI not wired')
reply({ok=true,token=string.rep('t',32)}); script.update(0.1); reply(state(true))
local joined=draw()
check(joined:find('Test Car') and joined:find('Record baseline') and joined:find('AC TELEMETRY: LIVE'),'status/guidance missing')
check(joined:find('Collecting useful drift') and joined:find('Missing time is excluded') and joined:find('captured from CSP'),'live setup/evidence guidance missing')
check(joined:find('Ready when:') and joined:find('Next: read findings') and joined:find('Car tuning only') and joined:find('Baseline.ini'),'workflow/readiness/context missing')
check(children.adtRecord and not enabled['Confirm these settings'],'record pane not scrollable or confirmation preselected')
draw('Confirm these settings')
check(pending.method=='GET','unchecked confirmation submitted')
draw('I checked this is true'); draw('Confirm these settings')
check(pending.body.action=='confirm' and pending.body.tuneConfirmed==true,'explicit confirmation not sent')
joined=draw('Start recording')
check(not enabled['Start recording'] and joined:find('All run and workflow actions'),'mutation leaves cached recorder buttons active')
refresh()
draw('Prepare next recording')
check(pending.body.action=='prepare','prepare action missing')
refresh()
local before=#requests
joined=draw(nil,'Findings')
check(#requests==before and children.adtFindings,'opening findings unexpectedly rebuilds report or lacks scrolling')
check(joined:find('Sustain more angle') and joined:find('Longer hold') and joined:find('Worth testing') and joined:find('Repeated rear slip'),'findings evidence missing')
draw('Read saved run')
check(pending.body.action=='findings' and pending.body.sessionId=='run-b','explicit saved-run request missing identity')
refresh()
draw('Plan this test##plan1')
check(pending.body.action=='plan' and pending.body.sessionId=='run-b' and pending.body.recommendationId=='test-1','wrong recommendation planned')
check(pending.body.controlVersion==string.rep('a',64),'plan lacks authoritative revision')
refresh()
joined=draw(nil,'Compare')
check(children.adtCompare and joined:find('Tradeoff') and joined:find('Different pedal use') and joined:find('More angle; less speed.'),'comparison/limitations missing')
check(joined:find('CAMBER_LF') and joined:find('display units'),'captured setup changes or units explanation missing')
check(not enabled['Save run review'],'review enabled without rating')
draw('Better'); draw('Keep and verify'); notesInput=string.rep('n',2001)
joined=draw('Save run review')
check(not enabled['Save run review'] and joined:find('Shorten the notes'),'oversized notes silently truncated or sent')
notesInput='More comfortable'; draw('Save run review'); notesInput=nil
check(pending.body.action=='review' and pending.body.driverRating=='Better' and pending.body.nextAction=='Keep and verify' and pending.body.notes=='More comfortable','driver review payload differs from choices')
local saved=state(true); saved.workflow.report.reviewSaved=true; saved.workflow.canSaveReview=false
refresh(saved); joined=draw()
check(joined:find('Review saved in ADT.'),'saved review acknowledgment missing')
joined=draw(nil,'Help')
check(children.adtHelp and joined:find('Stop alone does not save') and joined:find('never repeated automatically'),'help missing or not scrollable')
poll(state(false)); joined=draw(nil,'Findings')
check(joined:find('preview.13') and not enabled['Read saved run'],'older desktop does not degrade gracefully')
draw('Start recording','Record')
check(pending.body.action=='start','old desktop recording broken')
reply({message='Save first'},409); joined=draw()
check(joined:find('Save first') and not enabled['Start recording'],'server rejection missing or old button remains active')
script.update(1.1); reply(state(true)); script.update(4); joined=draw('Prepare next recording')
check(joined:find('Waiting for fresh ADT status') and not enabled['Prepare next recording'],'stale status retains workflow action')
reply(state(true))
joined=draw(nil,'Pit setup')
check(children['adtPit setup'] and joined:find('updated desktop ADT'),'pit setup fallback missing or pane does not scroll')
local staged=state(true)
staged.pitSetup={protocolVersion=1,controlVersion=string.rep('d',64),canApply=true,busy=false,message='Staged tune',
  plan={protocolVersion=1,planId=string.rep('a',32),carId='test_car',label='Test tune',
    baselineValues={CAMBER_LF=-30},changes={{section='CAMBER_LF',before=-30,after=-25}}}}
poll(staged);local previousRequests=#requests
joined=draw('Save & Apply Tune','Pit setup')
check(joined:find('Test tune') and joined:find('CAMBER_LF: %-30') and joined:find('stored setup VALUE units'),'pit preview omits reviewed changes/units')
check(not enabled['Save & Apply Tune'] and #requests==previousRequests,'pit UI initiated mutation without supported CSP/valid lease guard')
draw('Disconnect / pair again','Help')
check(preferences.port=='5190' and preferences.token==nil and preferences.code==nil,'pairing secret persisted')
check(children.adtWaiting,'disconnected/waiting controls lack scroll access')
print('PASS '..count..' companion UI assertions under mocked CSP APIs; actual entry point, all panes and actions.')
