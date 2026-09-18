local create = assert(loadfile(arg[1]))()
local pending, reads, requests, count = {}, 0, {}, 0
local function check(ok, why) assert(ok, why); count=count+1 end
local function request(method,url,headers,body,callback)
  pending[#pending+1]={method=method,url=url,headers=headers,body=body,callback=callback}
  requests[#requests+1]=pending[#pending]
end
local current='[CAMBER_LF]\nVALUE=-30\n'
local client=create(request,function(x)return x end,function(x)return x end,function()
  reads=reads+1
  return {available=current~=nil,protocolVersion=1,source='csp-current-setup',setupIni=current}
end)
local function answer(data,status)
  local p=table.remove(pending,1); assert(p); p.callback(nil,{status=status or 200,body=data}); return p
end
local offer={protocolVersion=1,nonce=string.rep('a',64),windowId=string.rep('b',32)}
local function state(capture)
  return {protocolVersion=1,appVersion='0.9.0-preview.14',setupCapture=capture,
    recorder={canStart=true,windowId=offer.windowId,sessionId='run',controlVersion=string.rep('c',64)}}
end
client:update(1);check(reads==0 and #requests==0,'unpaired capture attempted')
client:pair('123456','5190');answer({ok=true,token=string.rep('t',32)})
client:update(0);answer(state(nil));check(reads==0 and client:fresh(),'older desktop capture was assumed')
client:update(1.1);answer(state(offer))
check(reads==1 and #pending==1 and pending[1].url:match('/api/companion/setup$'),'hidden update did not capture')
check(pending[1].body.nonce==offer.nonce and pending[1].body.windowId==offer.windowId and pending[1].body.setupIni==current,'challenge or current value lost')
check(not client:command('start'),'command overlapped setup read')
answer({ok=true,refreshStatus=true,message='Captured'})
check(not client:fresh(),'changed setup retained old actionable status')
client:update(.01);answer(state(offer));check(client:fresh() and reads==1,'changed state refresh looped into setup capture')
current='[CAMBER_LF]\nVALUE=-25\n'
client:update(1.1);answer(state(offer));check(reads==2 and pending[1].body.setupIni==current,'unsaved change reused stale payload')
answer({ok=true,refreshStatus=false});check(client:fresh(),'unchanged capture invalidated usable controls')
check(client:command('start'),'recording failed after capture');answer({ok=true,message='Started'})
client:update(0);answer(state(offer));check(reads==2,'command status refresh exceeded capture rate')
current=nil
client:update(1.1);answer(state(offer));check(pending[1].body.available==false and pending[1].body.setupIni==nil,'unsupported CSP retained old payload')
local old=table.remove(pending,1)
client.commandMessage='Saved run'
client:update(8.1);check(not client:fresh() and client.commandMessage=='Saved run' and client.setupMessage:find('timed out'),'read timeout overwrote command outcome')
old.callback(nil,{status=200,body={ok=true,refreshStatus=true,message='late'}})
check(client.setupMessage~='late','late setup reply was accepted')
client:forget();client:update(10);check(reads==3 and client.token==nil,'disconnect continued capture')
local invalid=create(request,function(x)return x end,function(x)return x end,function()error('private path')end)
invalid.token='paired';invalid:captureSetup(offer)
check(pending[1].body.available==false and pending[1].body.message==nil,'capture error leaked detail or retained old data')
answer({ok=true});invalid:captureSetup({protocolVersion=1,nonce='bad',windowId=offer.windowId})
check(#pending==0,'invalid challenge invoked capture')
print('PASS '..count..' setup transport assertions; pairing, hidden polling, freshness, versions, timeouts and old desktop fallback.')
