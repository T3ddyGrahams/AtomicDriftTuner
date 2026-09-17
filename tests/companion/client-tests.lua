local create = assert(loadfile(arg[1]))()
local requests = {}
local function request(method, url, headers, body, callback)
  requests[#requests + 1] = {method=method, url=url, headers=headers, body=body, callback=callback}
end
local function identity(x) return x end
local c = create(request, identity, identity)
local count = 0
local function check(value, message) assert(value, message); count = count + 1 end
local function answer(data, status, err) requests[#requests].callback(err, {status=status or 200, body=data}) end
local function state(session)
  return {protocolVersion=1, appVersion='test', recorder={canStart=true, canStop=false, canSave=false, windowId='window', sessionId=session or 'one', controlVersion='v1'}}
end
c:pair('123456', '5190/evil')
check(#requests == 0, 'port injection')
c:pair('invalid', '5190')
check(#requests == 0, 'invalid pairing code')
c:pair('123456', '5190')
check(#requests == 1 and requests[1].url == 'http://127.0.0.1:5190/api/pair', 'pair local only')
check(not c:command('start'), 'unpaired recording')
answer({ok=true, token=string.rep('t', 32)})
c:update(0.1)
check(requests[2].headers['X-ADT-Token'] == c.token, 'token header missing')
answer(state())
check(c:fresh(), 'valid status missing')
check(not c:command('apply') and not c:command('save'), 'unsupported/unavailable action sent')
check(c:command('start'), 'start failed')
check(requests[3].body.sessionId == 'one' and requests[3].body.controlVersion == 'v1', 'command loses concurrency version')
check(not c:command('start') and #requests == 3, 'double click sends two starts')
local delayed = requests[3].callback
c:update(9)
check(not c:fresh() and #requests == 3, 'timeout retries a mutation')
c:update(2.1)
check(#requests == 4 and requests[4].method == 'GET', 'timeout did not recover through status')
answer(state('two'))
delayed(nil, {status=200, body={ok=true, message='late'}})
check(c.status.recorder.sessionId == 'two', 'late callback replaced current run')
c:update(1.1)
answer({}, 401)
check(c.token == nil and not c:fresh(), 'revoked pairing not cleared')
c:pair('123456', '5190'); answer({ok=true, token=string.rep('x',32)})
c:update(0.1); answer(state())
c:update(1.1); answer(nil, nil, 'connection refused')
check(not c:fresh() and c.status == nil and not c:command('start'), 'offline controls enabled')
c:update(1.1); answer({protocolVersion=2, recorder={}})
check(not c:fresh(), 'incompatible protocol allowed')
c:update(1.1); answer({},404)
check(c.message:find('Update desktop ADT'), 'old desktop does not explain update')
c:update(1.1)
local beforeForget = requests[#requests].callback
c:forget(); beforeForget(nil,{status=200,body=state()})
check(c.token == nil and not c:fresh(), 'disconnect undone by late poll')
-- Additive workflow contract: older recorder-only desktops continue to work.
requests = {}
c = create(request, identity, identity)
local function pairAgain()
  c:pair('123456', '5190'); answer({ok=true, token=string.rep('x',32)}); c:update(0.1)
end
local function workflowState(version)
  local s = state()
  s.workflow = {protocolVersion=1, available=true, controlVersion=version or string.rep('a',64),
    canPrepare=true, canConfirm=true, canReadFindings=true, canSaveReview=true, savedSessionId='run-b',
    report={sessionId='run-b', baselineSessionId='run-a', recommendations={
      {id='safe-test',canSelect=true}, {id='low-confidence',canSelect=false}}}}
  return s
end
local function refreshed(data)
  answer({ok=true,message='Done'})
  c:update(0.1); answer(data or workflowState())
end
pairAgain(); answer(state())
check(c:fresh() and c:workflowState()==nil and not c:workflow('prepare'), 'old desktop workflow must be unavailable')
check(c:command('start'), 'additive workflow broke old recording')
refreshed(workflowState())
check(c:canWorkflow('prepare') and c:canWorkflow('findings'), 'advertised actions unavailable')
check(not c:workflow('apply') and not c:workflow('confirm',{tuneConfirmed=false}), 'unsupported or implicit confirmation sent')
check(c:workflow('confirm',{tuneConfirmed=true,controlVersion='forged'}), 'explicit confirmation failed')
check(requests[#requests].url=='http://127.0.0.1:5190/api/companion/workflow' and requests[#requests].body.controlVersion==string.rep('a',64), 'workflow bypasses loopback or uses caller token')
check(not c:fresh() and c.status==nil and not c:command('start') and not c:workflow('prepare'), 'workflow mutation leaves stale recorder/actions active')
refreshed()
check(c:workflow('findings'), 'explicit findings failed')
check(requests[#requests].body.sessionId=='run-b', 'findings omitted exact saved run')
refreshed()
check(not c:workflow('plan',{sessionId='run-a',recommendationId='safe-test'}) and not c:workflow('plan',{sessionId='run-b',recommendationId='low-confidence'}), 'stale/disabled recommendation sent')
check(c:workflow('plan',{sessionId='run-b',recommendationId='safe-test'}), 'valid plan failed')
local oldWorkflow = requests[#requests].callback
local beforeTimeout = #requests
c:update(9)
check(#requests==beforeTimeout and c.status==nil, 'workflow timeout repeats mutation or retains report')
c:update(2.1)
check(#requests==beforeTimeout+1 and requests[#requests].method=='GET', 'workflow recovery does not use status only')
answer(workflowState(string.rep('b',64)))
oldWorkflow(nil,{status=200,body={ok=true,message='late plan'}})
check(c:workflowState().controlVersion==string.rep('b',64), 'late workflow response changes current state')
check(not c:workflow('review',{sessionId='run-b',driverRating='',nextAction='Keep and verify',notes=''}), 'missing feedback allowed')
check(not c:workflow('review',{sessionId='run-b',driverRating='Better',nextAction='apply',notes=''}), 'unrecognized review decision allowed')
check(not c:workflow('review',{sessionId='run-b',driverRating='Better',nextAction='Undecided',notes=string.rep('n',2001)}), 'oversized notes sent')
check(c:textLength('é')==1 and c:textLength('🙂')==2, 'note length splits UTF-8 or does not match desktop UTF-16')
check(c:workflow('review',{sessionId='run-b',driverRating='Tradeoff',nextAction='Test again',notes='More angle; less speed.'}), 'valid feedback blocked')
check(requests[#requests].body.driverRating=='Tradeoff' and requests[#requests].body.notes=='More angle; less speed.', 'review changes driver feedback')
refreshed()
local w = workflowState(); w.workflow.protocolVersion=2
c:update(1.1); answer(w)
check(c:fresh() and c:workflowState()==nil and not c:workflow('prepare'), 'incompatible workflow breaks recorder or enables actions')
c:update(1.1); answer(workflowState())
c:update(4)
check(not c:fresh() and not c:workflow('prepare') and not c:command('start'), 'expired status enables actions')
answer(workflowState())
c:workflow('prepare'); answer({},404)
check(c.message:find('preview.13') and c.status==nil, 'missing workflow route needs an actionable update message')
c:update(1.1); answer(workflowState())
c:workflow('findings'); answer({},401)
check(c.token==nil and c.status==nil and not c:workflow('review'), 'expired pairing retains workflow control')
-- Encoding errors are contained just like transport errors and never retain old controls.
local broken = create(request, function() error('encode failure') end, identity)
local encoded = pcall(function() broken:pair('123456','5190') end)
check(encoded and not broken.busy and broken.status==nil, 'encoding failure escapes client or leaves it busy')
print('PASS '..count..' companion client assertions (Lua 5.1).')
