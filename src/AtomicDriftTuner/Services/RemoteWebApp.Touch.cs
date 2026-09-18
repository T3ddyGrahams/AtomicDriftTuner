namespace AtomicDriftTuner.Services;

public static partial class RemoteWebApp
{
    private const string TouchStyles = """
html{scroll-padding-top:90px}
body{font-size:16px;overflow-wrap:anywhere}
button,input,select,summary{min-height:48px;touch-action:manipulation}
button{cursor:pointer}
button:disabled{cursor:default}
input,select{font-size:16px}
input[type=range]{min-height:48px;touch-action:pan-y}
button:focus-visible,input:focus-visible,select:focus-visible,summary:focus-visible{outline-offset:3px}
header .brandline>div,.row>div,.selectrow>*{min-width:0}
.touchscreen .wrap{max-width:1280px}
.touchscreen{font-size:18px}
.touchscreen button,.touchscreen select,.touchscreen input{min-height:54px}
.touchscreen .notice{font-size:16px}
.touchscreen .card h2{font-size:19px}
.keypad{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:10px}
.keypad button{margin:0;font-size:20px}
.pair{max-width:440px;margin:10px auto}
.pair button:disabled{opacity:1}
.control-grid{display:grid;gap:12px}
.control-grid .card{min-width:0}
.recorder-actions{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin:14px 0}
.recorder-actions button{min-height:62px;padding:12px 5px}
.control-message{white-space:pre-line;line-height:1.5}
.control-stats{display:flex;gap:18px;flex-wrap:wrap;margin:12px 0;font-size:20px;font-variant-numeric:tabular-nums}
.control-stats small{font-size:13px;color:var(--muted)}
.control-help summary{padding:12px 0;cursor:pointer;color:var(--heading)}
.control-help p{white-space:pre-line;line-height:1.55}
.control-notice{border-left:3px solid var(--accent);padding-left:12px}
.setting{grid-template-columns:minmax(0,1fr) auto}
.setting-label{grid-column:1/-1}
.stepper{display:grid;grid-template-columns:48px minmax(64px,1fr) 48px;gap:6px}
.stepper input{text-align:center;min-width:0;padding:8px}
.setting .edited{border-color:var(--accent)}
.bottomnav button{font-size:12px;min-width:0;padding:8px 4px}
#controlReply{margin-top:10px;white-space:pre-line}
dialog{background:var(--surface);color:var(--text);border:1px solid var(--border);border-radius:16px;padding:22px;max-width:480px;width:calc(100% - 32px)}
dialog::backdrop{background:var(--bg);opacity:.82}
dialog h2{margin-top:0}dialog p{line-height:1.5}dialog .recorder-actions{grid-template-columns:1fr 1fr}
@media(min-width:900px){.control-grid{grid-template-columns:minmax(0,1.05fr) minmax(0,1fr)}.touchscreen #view-dashboard.active{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.touchscreen .control-grid{grid-column:1/-1}.touchscreen #view-dashboard>.card{margin-bottom:0}.touchscreen #view-dashboard>.control-grid .card{margin-bottom:0}}
@media(max-width:520px){.setting{grid-template-columns:minmax(0,1fr) auto}.recorder-actions{gap:6px}.recorder-actions button{font-size:14px}.touchscreen .notice{font-size:15px}}
""";

    private const string PairKeypad = """
    <div class="keypad" role="group" aria-label="Pairing code keypad">
      <button type="button" onclick="pairDigit('1')">1</button><button type="button" onclick="pairDigit('2')">2</button><button type="button" onclick="pairDigit('3')">3</button>
      <button type="button" onclick="pairDigit('4')">4</button><button type="button" onclick="pairDigit('5')">5</button><button type="button" onclick="pairDigit('6')">6</button>
      <button type="button" onclick="pairDigit('7')">7</button><button type="button" onclick="pairDigit('8')">8</button><button type="button" onclick="pairDigit('9')">9</button>
      <button type="button" onclick="pairDigit('clear')" aria-label="Clear pairing code">Clear</button><button type="button" onclick="pairDigit('0')">0</button><button type="button" onclick="pairDigit('back')" aria-label="Delete last digit">⌫</button>
    </div>
""";

    private const string TouchControls = """
      <div class="control-grid">
        <div class="card">
          <h2>RUN CONTROLS</h2>
          <div class="row"><b id="controlCar">Prepare your car in ADT</b><span class="pill" id="controlState">CONNECTING</span></div>
          <div class="muted" id="controlDriver"></div>
          <div class="control-stats"><span><b id="controlTime">0:00</b> <small>recorded</small></span><span><b id="controlSamples">0</b> <small>samples</small></span></div>
          <div class="recorder-actions" role="group" aria-label="Recording controls">
            <button id="recordStart" class="primary" onclick="recordCommand('start')" disabled>Start run</button>
            <button id="recordStop" onclick="recordCommand('stop')" disabled>Stop run</button>
            <button id="recordSave" onclick="recordCommand('save')" disabled>Save run</button>
          </div>
          <div id="controlMessage" class="notice control-message">Connecting to the desktop recorder…</div>
          <p id="controlEvidence" class="control-message"></p>
          <details class="control-help"><summary>Recording evidence and setup</summary><p id="controlEvidenceDetails"></p><p id="controlSetup"></p></details>
          <div id="controlReply" class="notice" role="status" aria-live="polite"></div>
          <details class="control-help"><summary>First time using the touchscreen?</summary><p>In desktop ADT, select your car and driver. Open Telemetry Recorder, enter your conditions/driving task and confirm the setup you will use. Keep ADT running. Once AC telemetry is connected, use Start run here. Tap Stop run after driving, then Save run. Saving keeps the complete recording and analysis in ADT; it does not apply a tune.</p></details>
        </div>
        <div class="card">
          <h2>YOUR NEXT STEP</h2>
          <h3 id="controlNext">Checking your workflow…</h3>
          <p id="controlInstructions" class="control-message"></p>
          <p id="controlCompletion" class="control-notice notice"></p>
          <details class="control-help"><summary>More explanation and progress</summary><p id="controlDetails"></p><p id="controlProgress" class="notice"></p></details>
        </div>
      </div>
""";

    private const string TouchScript = """
let statusLastSeen=0, azomLastSeen=0, controlLastSeen=0;
let controlCache=null, controlRefreshRunning=false, controlCommandBusy=false, controlEpoch=0;
let azomMutationBusy=false;
let settingsContext='';
const settingsDrafts=new Map(), settingsRows=new Map();
const freshnessMs=5000;
let confirmPending=null;

function confirmAction(message){
  if(confirmPending)return Promise.resolve(false);
  const dialog=$('confirmDialog'),previous=document.activeElement;
  $('confirmMessage').textContent=message;
  return new Promise(resolve=>{
    confirmPending=result=>{confirmPending=null;dialog.close();if(previous?.isConnected)previous.focus();resolve(result);};
    $('confirmAccept').onclick=()=>confirmPending?.(true);
    $('confirmCancel').onclick=()=>confirmPending?.(false);
    dialog.oncancel=event=>{event.preventDefault();confirmPending?.(false);};
    dialog.showModal();$('confirmCancel').focus();
  });
}

function storageGet(key){try{return localStorage.getItem(key);}catch{return null;}}
function storageSet(key,value){try{localStorage.setItem(key,value);}catch{}}
function storageRemove(key){try{localStorage.removeItem(key);}catch{}}
async function fetchRemote(path,options={}){
  const controller=new AbortController();
  const timeout=setTimeout(()=>controller.abort(),options.method==='POST'?20000:6000);
  try{
    const response=await fetch(path,{...options,signal:controller.signal});
    const text=await response.text();
    return {ok:response.ok,status:response.status,text:async()=>text};
  }finally{clearTimeout(timeout);}
}
function pairDigit(value){
  if($('pairButton').disabled)return;
  const field=$('pairCode');
  field.value=value==='clear'?'':value==='back'?field.value.slice(0,-1):(field.value+value).replace(/\D/g,'').slice(0,6);
  $('pairError').textContent='';
}
function remoteOnline(){return !!token&&document.visibilityState==='visible'&&statusLastSeen>0&&performance.now()-statusLastSeen<freshnessMs;}
function markRemoteOffline(){
  statusLastSeen=azomLastSeen=controlLastSeen=0;
  $('connection').textContent=token?'Disconnected • checking connection…':'Not paired';
  $('connection').className='sub bad';
  $('writePill').textContent='WRITES UNAVAILABLE';$('writePill').className='pill warn';
  renderControl(); renderSettings();
}
function resetTouchState(){
  confirmPending?.(false);
  controlEpoch++;controlCache=null;statusLastSeen=azomLastSeen=controlLastSeen=0;writesEnabled=false;
  settingsDrafts.clear();settingsCache=[];settingsContext='';
  $('pairCode').value='';$('controlReply').textContent='';
  renderControl();renderSettings();
}
function syncSettingsContext(tune){
  const key=JSON.stringify([tune.wheelbase,tune.steeringWheel,tune.driftPack,tune.car,tune.intent]);
  if(settingsContext&&settingsContext!==key){
    const hadDrafts=settingsDrafts.size>0;settingsDrafts.clear();confirmPending?.(false);
    settingsCache=[];azomLastSeen=0;renderSettings();setTimeout(refreshAzom,0);
    if(hadDrafts)toast('Car or rig changed. Unsaved wheelbase edits were cleared; review the current settings.');
  }
  settingsContext=key;
}
function controlIsFresh(){return remoteOnline()&&controlLastSeen>0&&performance.now()-controlLastSeen<freshnessMs&&controlCache?.protocolVersion===1;}
function renderControl(){
  const fresh=controlIsFresh(), r=controlCache?.recorder;
  $('recordStart').disabled=!fresh||controlCommandBusy||!r?.canStart;
  $('recordStop').disabled=!fresh||controlCommandBusy||!r?.canStop;
  $('recordSave').disabled=!fresh||controlCommandBusy||!r?.canSave;
  if(!fresh){
    $('controlEvidence').textContent='';$('controlEvidenceDetails').textContent='';$('controlSetup').textContent='';
    $('controlState').textContent=token?'OFFLINE':'NOT PAIRED';$('controlState').className='pill bad';
    $('controlMessage').textContent=controlCache?.protocolVersion&&controlCache.protocolVersion!==1?'Update desktop ADT to use these recording controls.':'Waiting for current recorder status. Controls return when ADT reconnects. A running recording stays in desktop ADT.';
    return;
  }
  $('controlState').textContent=controlCommandBusy?'WORKING…':String(r?.state||'unprepared').toUpperCase();
  $('controlState').className='pill '+(r?.state==='recording'?'ok':r?.state==='unsaved'?'warn':'');
  $('controlCar').textContent=r?.car||'Prepare your car in desktop ADT';
  $('controlDriver').textContent=r?.driver?'Driver: '+r.driver:'';
  const seconds=Math.max(0,Math.floor(Number(r?.elapsedSeconds)||0));
  $('controlTime').textContent=Math.floor(seconds/60)+':'+String(seconds%60).padStart(2,'0');
  $('controlSamples').textContent=String(r?.samples||0);
  $('controlMessage').textContent=r?.message||'Open Telemetry Recorder in desktop ADT to prepare a run.';
  $('controlEvidence').textContent=r?.evidence?.message||'';
  $('controlEvidenceDetails').textContent=r?.evidence?.details||'';
  $('controlSetup').textContent=r?.setupMessage||'';
  $('controlNext').textContent=controlCache.nextStep||'Follow the workflow in ADT.';
  $('controlInstructions').textContent=controlCache.instructions||'';
  $('controlCompletion').textContent=controlCache.completion||'';
  $('controlDetails').textContent=controlCache.details||'';
  $('controlProgress').textContent=controlCache.progress||'';
}
async function refreshControl(){
  if(!token||controlRefreshRunning||controlCommandBusy||document.visibilityState!=='visible')return;
  controlRefreshRunning=true;const epoch=controlEpoch;
  try{
    const current=await api('/api/control/status');
    if(epoch!==controlEpoch||!token)return;
    controlCache=current;controlLastSeen=performance.now();renderControl();
  }catch(error){if(epoch===controlEpoch){controlLastSeen=0;renderControl();}}
  finally{controlRefreshRunning=false;}
}
async function recordCommand(action){
  if(controlCommandBusy||!['start','stop','save'].includes(action))return;
  const r=controlCache?.recorder;
  if(!controlIsFresh()||!r?.['can'+action[0].toUpperCase()+action.slice(1)]){toast('Wait for the current recorder status before trying again.',true);return;}
  const request={action,windowId:r.windowId,sessionId:r.sessionId,controlVersion:r.controlVersion};
  controlCommandBusy=true;controlEpoch++;$('controlReply').textContent='Sending '+action+' command…';renderControl();
  try{
    const response=await api('/api/control/recording',{method:'POST',body:JSON.stringify(request)});
    if(!response.ok)throw new Error(response.message||'The recorder could not complete this action.');
    $('controlReply').textContent=response.message||'Recording command completed.';
  }catch(error){$('controlReply').textContent=normalizeErrorMessage(error)+' Check the refreshed recorder state before trying again; the action may have completed.';}
  finally{controlCommandBusy=false;controlLastSeen=0;renderControl();await refreshControl();}
}
function renderSettings(){
  const root=$('settings');
  const keys=new Set(settingsCache.map(s=>s.propertyName));
  for(const [key,row] of settingsRows){if(!keys.has(key)){row.element.remove();settingsRows.delete(key);}}
  const online=remoteOnline()&&azomLastSeen>0&&performance.now()-azomLastSeen<freshnessMs;
  for(const setting of settingsCache){
    const key=setting.propertyName;let row=settingsRows.get(key);
    if(!row){
      const element=document.createElement('div');element.className='setting';
      const label=document.createElement('div');label.className='setting-label';
      const title=document.createElement('b'),range=document.createElement('small');label.append(title,range);
      const stepper=document.createElement('div');stepper.className='stepper';
      const minus=document.createElement('button'),input=document.createElement('input'),plus=document.createElement('button'),apply=document.createElement('button');
      minus.textContent='−';plus.textContent='+';input.type='number';input.step='1';input.dataset.prop=key;input.inputMode='numeric';
      input.setAttribute('aria-label',setting.displayName+' value');minus.setAttribute('aria-label','Decrease '+setting.displayName);plus.setAttribute('aria-label','Increase '+setting.displayName);
      input.oninput=()=>{settingsDrafts.set(key,input.value);input.classList.add('edited');};
      const step=delta=>{const current=settingsCache.find(s=>s.propertyName===key);if(!current)return;input.value=String(Math.min(current.max,Math.max(current.min,(Number(input.value)||current.min)+delta)));input.oninput();};
      minus.onclick=()=>step(-1);plus.onclick=()=>step(1);
      apply.textContent='APPLY';apply.onclick=()=>{const current=settingsCache.find(s=>s.propertyName===key);if(current)applySetting(current,input);};
      stepper.append(minus,input,plus);element.append(label,stepper,apply);root.append(element);
      row={element,title,range,input,minus,plus,apply};settingsRows.set(key,row);
    }
    row.title.textContent=setting.displayName||key;
    row.range.textContent='Live: '+(setting.current??'unavailable')+(setting.unit||'')+' · Range '+setting.min+'–'+setting.max;
    row.input.min=setting.min;row.input.max=setting.max;
    if(document.activeElement!==row.input)row.input.value=settingsDrafts.has(key)?settingsDrafts.get(key):setting.current??'';
    row.input.classList.toggle('edited',settingsDrafts.has(key));
    const disabled=!online||!writesEnabled||azomMutationBusy||!setting.writable||setting.current==null;
    row.apply.disabled=row.input.disabled=row.minus.disabled=row.plus.disabled=disabled;
  }
  $('revertButton').disabled=!online||!writesEnabled||azomMutationBusy;
}
if(document.body.classList.contains('touchscreen'))$('pairCode').inputMode='none';
setInterval(()=>{
  if(token&&document.visibilityState==='visible'){
    if(!remoteOnline()){$('connection').textContent='Disconnected • checking connection…';$('connection').className='sub bad';}
    renderControl();renderSettings();refreshControl();
  }
},900);
window.addEventListener('offline',markRemoteOffline);
window.addEventListener('online',()=>{if(token)refreshAll();});
""";
}
