local create = assert(loadfile(arg[1]))()
local pending, text, pressed, disabled = nil, {}, nil, 0
require = function(name) assert(name == 'companion_client'); return create end
ac = {storage=function() return {port='5190'} end}
JSON = {stringify=function(x) return x end, parse=function(x) return x end}
rgbm = function(...) return {...} end
vec2 = function(...) return {...} end
web = {timeouts=function() end, request=function(method,url,headers,body,callback) pending={method=method,url=url,body=body,callback=callback} end}
ui = {
  text=function(t) text[#text+1]=t end, textWrapped=function(t) text[#text+1]=t end,
  textColored=function(t) text[#text+1]=t end, separator=function() end,
  setNextItemWidth=function() end,
  inputText=function(label,value) return label == 'Pairing code' and '123456' or value end,
  pushDisabled=function() disabled=disabled+1 end, popDisabled=function() disabled=disabled-1 end,
  button=function(label) return pressed==label and disabled==0 end
}
script = {}
assert(loadfile(arg[2]))()
script.windowMain(0)
assert(pending==nil, 'drawing starts network traffic before pairing')
pressed='Pair with ADT'; script.windowMain(0); pressed=nil
assert(pending.body.code=='123456', 'pairing UI not wired')
pending.callback(nil,{status=200,body={ok=true,token=string.rep('t',32)}})
script.update(0.1)
pending.callback(nil,{status=200,body={protocolVersion=1,appVersion='test',telemetryConnected=true,nextStep='Record baseline',instructions='Test instructions',recorder={car='Test Car',driver='Driver',state='ready',canStart=true,canStop=false,canSave=false,windowId='w',sessionId='s',controlVersion='v'}}})
text={}; script.windowMain(0)
local joined=table.concat(text,'\n')
assert(joined:find('Test Car') and joined:find('Record baseline') and joined:find('AC TELEMETRY: LIVE'), 'status/guidance missing')
pressed='Start recording'; script.windowMain(0); pressed=nil
assert(pending.body.action=='start', 'record button not wired')
pending.callback(nil,{status=409,body={message='Save first'}})
text={}; script.windowMain(0)
assert(table.concat(text,'\n'):find('Save first'), 'failure not displayed')
assert(disabled==0,'unbalanced disabled controls')
print('PASS 6 companion UI assertions under a mocked CSP drawing/transport API.')
