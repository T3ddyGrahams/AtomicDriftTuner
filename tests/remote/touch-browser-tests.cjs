// Runs the compiled production HTML against isolated fake HTTP responses.
// No Assetto Corsa, SimHub, user settings, or live wheelbase writes are used.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const http=require('node:http');
const vm=require('node:vm');
const {chromium}=require('playwright');
const output=path.resolve(process.argv[2]||'artifacts/touch-browser');
const dash=fs.readFileSync(path.join(output,'dash.html'),'utf8');
const remote=fs.readFileSync(path.join(output,'remote.html'),'utf8');
const launcher=fs.readFileSync(path.join(output,'launch.html'),'utf8');
new vm.Script(dash.match(/<script>([\s\S]*?)<\/script>/)[1]);
let calls=[],offline=false,token='fixture-token',version=0,current=40,writeAllowed=true;
let state='ready',canStart=true,canStop=false,canSave=false;
let commandDelay=0;
let dropCommandReply=false,carName='Example drift car';
const recorder=()=>({windowId:'a'.repeat(32),sessionId:'fixture-run',controlVersion:version.toString(16).padStart(64,'0'),car:'Example drift car',driver:'Test driver',state,canStart,canStop,canSave,samples:state==='ready'?0:3000,elapsedSeconds:state==='ready'?0:75,message:state==='ready'?'Ready to record.':state==='recording'?'Recording in ADT.':state==='unsaved'?'Stop complete. Save your run.':'Session saved.'});
const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');
  const send=(status,value,type='application/json',headers={})=>{res.writeHead(status,{'Content-Type':type,...headers});res.end(type==='application/json'?JSON.stringify(value):value);};
  if(url.pathname==='/dash')return send(200,dash,'text/html',{'X-Frame-Options':'DENY'});
  if(url.pathname==='/dash/launch')return send(200,launcher,'text/html',{'Content-Security-Policy':"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'"});
  if(url.pathname==='/')return send(200,remote,'text/html');
  if(offline){req.socket.destroy();return;}
  let raw='';for await(const chunk of req)raw+=chunk;
  const body=raw?JSON.parse(raw):{};
  if(url.pathname==='/api/pair')return body.code==='654321'?send(200,{ok:true,token}):send(400,{error:'Wrong pairing code'});
  if(req.headers['x-adt-token']!==token)return send(401,{error:'Pairing required'});
  switch(url.pathname){
    case '/api/status':return send(200,{atomicVersion:'test fixture',remoteWritesEnabled:writeAllowed,lastActivity:'Isolated browser test',tune:{car:carName,wheelbase:'Example base',steeringWheel:'Example wheel',driftPack:'Example pack',intent:'Realistic',hasGeneratedTune:false}});
    case '/api/intents':return send(200,[{name:'Realistic'}]);
    case '/api/telemetry':return send(200,{connected:true,sample:{packetId:12,speedKmh:45,slipAngleDeg:30,steeringAngleDeg:270,finalFfb:0.4},isDrifting:true});
    case '/api/behavior':return send(200,{ok:true,values:{},contextKey:'example'});
    case '/api/azom':return send(200,{ok:true,bridgeVersion:'fixture',settingsReadable:true,baseConnected:true,settings:[{propertyName:'AZOM.GameFFBStrength',displayName:'Game FFB strength',min:0,max:100,current,writable:true,unit:'%'}]});
    case '/api/control/status':return send(200,{protocolVersion:1,telemetryConnected:true,telemetryStale:false,nextStep:'4 · Record and save a baseline',instructions:'Drive the same section, stop, then save your run.',details:'Follow the same goals and track conditions.',completion:'Ready when: your run is saved.',progress:'Car ✓ → Goals ✓ → Baseline ○',recorder:recorder()});
    case '/api/control/recording':{
      if(body.controlVersion!==recorder().controlVersion)return send(409,{ok:false,message:'The recorder changed. Refresh its status before trying again.'});
      if(commandDelay)await new Promise(resolve=>setTimeout(resolve,commandDelay));
      if(body.action==='start'&&canStart){state='recording';canStart=false;canStop=true;}
      else if(body.action==='stop'&&canStop){state='unsaved';canStop=false;canSave=true;}
      else if(body.action==='save'&&canSave){state='saved';canSave=false;canStart=true;}
      else return send(409,{ok:false,message:'Action unavailable'});
      calls.push(body.action);version++;if(dropCommandReply){dropCommandReply=false;req.socket.destroy();return;}return send(200,{ok:true,message:body.action==='save'?'Session saved in ADT (JSON and CSV).':'Recording '+body.action+' completed.'});
    }
    case '/api/azom/apply':calls.push('apply');current=body.value;return send(200,{ok:true,verified:true,message:'Verified setting'});
    case '/api/azom/revert':calls.push('revert');current=40;return send(200,{ok:true,verified:true,message:'Reverted setting'});
    default:return send(404,{error:'No fixture for '+url.pathname});
  }
});
// A separate origin and a proportionally scaled canvas reproduce the relevant
// SimHub WebPageItem behavior. Only the public launcher is framed.
let adtBase;
const simhub=http.createServer((req,res)=>{
  res.writeHead(200,{'Content-Type':'text/html'});
  res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body{margin:0;background:black;overflow:hidden}iframe{position:absolute;border:0;width:1280px;height:720px;transform-origin:0 0}</style></head><body><iframe title="SimHub ADT" src="'+adtBase+'/dash/launch"></iframe><script>function fit(){const s=Math.min(innerWidth/1280,innerHeight/720);document.querySelector("iframe").style.transform="translate("+((innerWidth-1280*s)/2)+"px,"+((innerHeight-720*s)/2)+"px) scale("+s+")"}addEventListener("resize",fit);fit();</script></body></html>');
});
let browser,checks=0;
const check=(value,message)=>{assert.ok(value,message);checks++;};
(async()=>{
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base='http://127.0.0.1:'+server.address().port;
  adtBase=base;
  await new Promise(resolve=>simhub.listen(0,'127.0.0.1',resolve));
  const simhubBase='http://127.0.0.1:'+simhub.address().port;
  browser=await chromium.launch({headless:true,channel:'msedge'});
  const context=await browser.newContext({viewport:{width:1280,height:720},hasTouch:true});
  const page=await context.newPage(),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  for(const [width,height] of [[800,480],[320,568],[1024,600]]){
    await page.setViewportSize({width,height});
    await page.goto(simhubBase);
    const open=page.frameLocator('iframe').getByRole('link',{name:'Open ADT',exact:true});
    const target=await open.boundingBox();
    check(target.width>=44&&target.height>=44,'SimHub launch target was too small after canvas scaling');
    await open.tap();
    await page.waitForURL(base+'/dash');
    check(await page.evaluate(()=>window.top===window.self&&window.frames.length===0),'ADT remained inside a fixed canvas');
    check(await page.evaluate(()=>document.querySelector('.wrap').getBoundingClientRect().width===document.documentElement.clientWidth),'Opened dashboard did not fill the device width');
  }
  await page.setViewportSize({width:1280,height:720});
  await page.goto(base+'/dash/launch');
  await page.waitForURL(base+'/dash');
  check(await page.locator('#pairCard').isVisible(),'Native/top-level launcher did not open ADT');
  await page.locator('#fullscreenButton').tap();
  await page.waitForFunction(()=>!!document.fullscreenElement||!!document.getElementById('displayHelp').textContent);
  check(await page.evaluate(()=>!!document.fullscreenElement),'Fullscreen failed in the browser fixture');
  check(await page.locator('#fullscreenButton').getAttribute('aria-pressed')==='true','Fullscreen button state did not follow the browser');
  await page.locator('#fullscreenButton').tap();
  await page.waitForFunction(()=>!document.fullscreenElement);
  await page.evaluate(()=>{
    window.testRequestFullscreen=document.documentElement.requestFullscreen;
    document.documentElement.requestFullscreen=()=>Promise.reject(new Error('Fullscreen blocked by host'));
  });
  await page.locator('#fullscreenButton').tap();
  await page.waitForFunction(()=>document.getElementById('displayHelp').textContent.includes('browser could not'));
  check(!await page.locator('#fullscreenButton').isDisabled()&&await page.locator('#fullscreenButton').getAttribute('aria-pressed')==='false','Fullscreen rejection left the page stuck');
  await page.evaluate(()=>{document.documentElement.requestFullscreen=window.testRequestFullscreen;document.getElementById('displayHelp').textContent='';});
  await page.locator('.keypad button', {hasText:/^1$/}).tap();
  await page.getByRole('button',{name:'Delete last digit',exact:true}).tap();
  check(await page.locator('#pairCode').inputValue()==='', 'Touch backspace failed');
  for(const digit of '654321')await page.locator('.keypad button').filter({hasText:new RegExp('^'+digit+'$')}).tap();
  await page.locator('#pairButton').tap();
  await page.waitForFunction(()=>!document.getElementById('recordStart').disabled);
  check(await page.locator('#controlNext').textContent()==='4 · Record and save a baseline','Next step missing');
  await page.waitForFunction(()=>document.getElementById('speed').textContent.includes('45'));
  check((await page.locator('#telemetryStatus').innerText()).includes('LIVE'),'Live telemetry status missing');
  await page.screenshot({path:path.join(output,'Touchscreen-1280.png'),fullPage:true});
  commandDelay=700;
  await page.locator('#recordStart').tap();
  check(await page.locator('#recordStart').isDisabled(),'Double tap not blocked');
  await page.waitForFunction(()=>!document.getElementById('recordStop').disabled);
  check(calls.filter(x=>x==='start').length===1,'Start sent more than once');
  for(const [width,height] of [[800,480],[480,800],[1920,1080]]){
    await page.setViewportSize({width,height});
    await page.waitForFunction(()=>!document.getElementById('recordStop').disabled);
    check((await page.locator('#controlState').textContent())==='RECORDING'&&calls.join(',')==='start','Resizing restarted or stopped a recording');
  }
  commandDelay=0;
  dropCommandReply=true;
  await page.locator('#recordStop').tap();
  await page.waitForFunction(()=>!document.getElementById('recordSave').disabled);
  check((await page.locator('#controlReply').innerText()).includes('may have completed'),'Lost reply was not reported as uncertain: '+await page.locator('#controlReply').innerText()+'; requests='+calls.join(','));
  check(calls.filter(x=>x==='stop').length===1,'A completed stop with a lost reply was executed again');
  await page.locator('#recordSave').tap();
  await page.waitForFunction(()=>document.getElementById('controlState').textContent==='SAVED');
  check(calls.join(',')==='start,stop,save','Start/stop/save flow incomplete');
  check((await page.locator('#controlReply').innerText()).includes('JSON and CSV'),'Save confirmation missing');
  offline=true;
  await page.waitForFunction(()=>document.getElementById('recordStart').disabled&&document.getElementById('connection').textContent.includes('Disconnected'));
  check(await page.locator('#recordStop').isDisabled()&&await page.locator('#recordSave').isDisabled(),'Offline recording actions remained active');
  offline=false;
  await page.waitForFunction(()=>!document.getElementById('recordStart').disabled);
  check(calls.length===3,'Reconnect repeated a command');
  await page.locator('#nav-azom').tap();
  const input=page.locator('#settings input');
  await input.waitFor();await input.fill('65');
  await page.waitForTimeout(2200);
  check(await input.inputValue()==='65','Polling overwrote the edited value');
  check(await input.evaluate(el=>el===document.activeElement),'Polling replaced the focused input');
  await page.getByRole('button',{name:'Increase Game FFB strength',exact:true}).tap();
  check(await input.inputValue()==='66','Touch increment failed');
  await page.locator('#settings button').filter({hasText:'APPLY'}).tap();
  await page.locator('#confirmCancel').tap();
  check(!calls.includes('apply'),'Cancel still applied the setting');
  await page.locator('#settings button').filter({hasText:'APPLY'}).tap();
  await page.locator('#confirmAccept').tap();
  await page.waitForFunction(()=>document.getElementById('toast').textContent.includes('Verified'));
  check(calls.filter(x=>x==='apply').length===1&&current===66,'Apply was duplicated or used the wrong draft');
  await input.fill('70');carName='Changed car';
  await page.waitForFunction(()=>document.getElementById('car').textContent==='Changed car'&&document.querySelector('#settings input')?.value==='66');
  check(!(await input.getAttribute('class')||'').includes('edited'),'Old-car edit remained pending');
  carName='Example drift car';
  await page.waitForFunction(()=>document.getElementById('car').textContent==='Example drift car');
  writeAllowed=false;
  await page.waitForFunction(()=>document.querySelector('#settings input').disabled);
  check(await page.locator('#revertButton').isDisabled(),'Remote-write opt-out did not disable revert');
  for(const [width,height] of [[1280,720],[800,480],[1024,600],[480,320],[390,844],[320,568],[720,1280],[1920,1080],[3440,1440]]){
    await page.setViewportSize({width,height});
    for(const tab of ['dashboard','tune','behavior','azom']){
      await page.locator('#nav-'+tab).tap();
      check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),tab+' overflowed at '+width);
      check(await page.evaluate(()=>Math.abs(document.querySelector('.wrap').getBoundingClientRect().width-document.documentElement.clientWidth)<2),tab+' left fixed-width borders at '+width);
    }
    await page.locator('#nav-dashboard').tap();
    const undersized=await page.locator('button:visible').evaluateAll(buttons=>buttons.filter(b=>{const r=b.getBoundingClientRect();return r.width<44||r.height<44;}).map(b=>b.textContent));
    check(undersized.length===0,'Small touch targets: '+undersized.join(','));
    for(const id of ['recordStart','recordStop','recordSave']){
      const button=page.locator('#'+id);await button.scrollIntoViewIfNeeded();
      check(await button.evaluate(el=>{const r=el.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('button')===el;}),id+' was covered at '+width+'x'+height);
    }
    await page.evaluate(()=>window.scrollTo(0,0));
    await page.screenshot({path:path.join(output,'Touchscreen-'+width+'x'+height+'.png'),fullPage:false});
  }
  await page.setViewportSize({width:480,height:320});
  await page.evaluate(()=>{void confirmAction('Long confirmation message for a small touchscreen. '.repeat(35));});
  const cancel=page.locator('#confirmCancel');await cancel.scrollIntoViewIfNeeded();
  check(await cancel.evaluate(el=>{const r=el.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight;}),'Confirmation buttons cannot be reached on a short display');
  await cancel.tap();
  check(!await page.locator('#confirmDialog').isVisible(),'Touch confirmation did not close');
  check(errors.length===0,'Browser errors: '+errors.join('; '));
  token='rotated-fixture-token';
  await page.waitForFunction(()=>!document.getElementById('pairCard').classList.contains('hidden'));
  check(await page.locator('#app').isHidden(),'Revoked pairing left the app active');
  const storageContext=await browser.newContext({viewport:{width:390,height:844},hasTouch:true});
  await storageContext.addInitScript(()=>{Storage.prototype.getItem=()=>{throw new Error('blocked')};Storage.prototype.setItem=()=>{throw new Error('blocked')};Storage.prototype.removeItem=()=>{throw new Error('blocked')};});
  const storagePage=await storageContext.newPage();
  await storagePage.goto(base+'/');
  await storagePage.locator('#pairCode').fill('654321');await storagePage.locator('#pairButton').tap();
  await storagePage.waitForFunction(()=>!document.getElementById('app').classList.contains('hidden'));
  check(await storagePage.locator('#app').isVisible(),'Blocked browser storage prevented session pairing');
  console.log('PASS '+checks+' browser assertions: cross-origin SimHub launch, real fullscreen and refusal, device resizing during recording, touch pairing, recorder lifecycle, uncertain reply, reconnect, draft retention, confirmations, responsive tabs and revoked credentials.');
})().catch(error=>{console.error(error.stack);process.exitCode=1;}).finally(async()=>{if(browser)await browser.close();server.closeAllConnections();simhub.closeAllConnections();await Promise.all([new Promise(resolve=>server.close(resolve)),new Promise(resolve=>simhub.close(resolve))]);});
