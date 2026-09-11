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
print('PASS '..count..' companion client assertions (Lua 5.1).')
