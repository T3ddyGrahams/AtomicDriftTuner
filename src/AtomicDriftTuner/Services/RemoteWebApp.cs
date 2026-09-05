namespace AtomicDriftTuner.Services;

public static class RemoteWebApp
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#0e1116">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<meta name="apple-mobile-web-app-title" content="ADT Remote">
<title>ADT Remote</title>

<style>
:root{
  color-scheme:dark;
  --bg:#0e1116;
  --surface:#171b22;
  --panel:#20252d;
  --panel2:#272c34;
  --border:#3a414d;
  --text:#f5f7fa;
  --muted:#aeb6c3;
  --accent:#00cfe8;
  --danger:#ff657a;
  --ok:#6be585;
  --warn:#ffd36b
}

*{
  box-sizing:border-box
}

body{
  margin:0;
  background:var(--bg);
  color:var(--text);
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;
  padding:env(safe-area-inset-top) 0 calc(72px + env(safe-area-inset-bottom))
}

header{
  position:sticky;
  top:0;
  z-index:8;
  background:rgba(14,17,22,.94);
  backdrop-filter:blur(14px);
  padding:13px 16px;
  border-bottom:1px solid var(--border)
}

.brandline{
  display:flex;
  justify-content:space-between;
  align-items:center;
  gap:10px
}

.brand{
  font-weight:900;
  letter-spacing:.08em
}

.sub{
  font-size:12px;
  color:var(--muted);
  margin-top:3px
}

.wrap{
  max-width:760px;
  margin:0 auto;
  padding:12px
}

.card{
  background:var(--surface);
  border:1px solid var(--border);
  border-radius:17px;
  padding:15px;
  margin-bottom:12px
}

.card h2{
  font-size:15px;
  letter-spacing:.03em;
  margin:0 0 12px
}

.card h3{
  font-size:13px;
  color:var(--muted);
  margin:15px 0 8px;
  text-transform:uppercase;
  letter-spacing:.05em
}

.grid{
  display:grid;
  grid-template-columns:repeat(2,minmax(0,1fr));
  gap:9px
}

.metric{
  background:var(--panel);
  padding:12px;
  border-radius:12px
}

.metric .k{
  font-size:10px;
  color:var(--muted);
  text-transform:uppercase;
  letter-spacing:.05em
}

.metric .v{
  font-size:22px;
  font-weight:850;
  margin-top:3px
}

.row{
  display:flex;
  gap:10px;
  align-items:center;
  justify-content:space-between;
  padding:9px 0;
  border-bottom:1px solid rgba(58,65,77,.55)
}

.row:last-child{
  border-bottom:0
}

.pill{
  display:inline-block;
  border-radius:999px;
  padding:5px 8px;
  font-size:10px;
  font-weight:850;
  background:var(--panel);
  white-space:nowrap
}

.ok{
  color:var(--ok)
}

.bad{
  color:var(--danger)
}

.warn{
  color:var(--warn)
}

.muted{
  color:var(--muted)
}

.notice{
  font-size:12.5px;
  line-height:1.45;
  color:var(--muted)
}

button,
input,
select{
  font:inherit
}

button{
  border:1px solid var(--border);
  background:var(--panel2);
  color:var(--text);
  border-radius:11px;
  padding:11px 12px;
  font-weight:750
}

button.primary{
  background:var(--accent);
  color:#071014;
  border-color:var(--accent)
}

button.danger{
  border-color:var(--danger);
  color:#ffd9df
}

button:disabled{
  opacity:.43
}

.full{
  width:100%
}

.stack{
  display:grid;
  gap:8px
}

.selectrow{
  display:grid;
  grid-template-columns:1fr auto;
  gap:8px
}

select,
input[type=number]{
  width:100%;
  background:var(--panel2);
  border:1px solid var(--border);
  border-radius:10px;
  color:var(--text);
  padding:11px
}

.setting{
  display:grid;
  grid-template-columns:1fr 88px 68px;
  gap:7px;
  align-items:center;
  padding:9px 0;
  border-bottom:1px solid rgba(58,65,77,.55)
}

.setting small{
  display:block;
  color:var(--muted);
  margin-top:2px
}

.pair input{
  font-size:22px;
  letter-spacing:.15em;
  text-align:center;
  width:100%;
  background:var(--panel2);
  border:1px solid var(--border);
  border-radius:10px;
  color:var(--text);
  padding:12px
}

.pair button{
  width:100%;
  margin-top:10px
}

.view{
  display:none
}

.view.active{
  display:block
}

.hidden{
  display:none!important
}

.bottomnav{
  position:fixed;
  left:0;
  right:0;
  bottom:0;
  z-index:9;
  background:rgba(14,17,22,.96);
  backdrop-filter:blur(14px);
  border-top:1px solid var(--border);
  padding:
    7px
    max(8px,env(safe-area-inset-left))
    calc(7px + env(safe-area-inset-bottom))
    max(8px,env(safe-area-inset-right));
  display:grid;
  grid-template-columns:repeat(4,1fr);
  gap:6px
}

.bottomnav button{
  padding:8px 4px;
  font-size:11px;
  background:transparent;
  border-color:transparent;
  color:var(--muted)
}

.bottomnav button.active{
  color:var(--accent);
  background:var(--panel);
  border-color:var(--border)
}

.behavior{
  padding:10px 0;
  border-bottom:1px solid rgba(58,65,77,.5)
}

.behavior:last-child{
  border-bottom:0
}

.behaviorTop{
  display:flex;
  justify-content:space-between;
  gap:10px;
  margin-bottom:6px
}

.behaviorVal{
  font-weight:800
}

.behavior input[type=range]{
  width:100%;
  accent-color:var(--accent)
}

.presetgrid{
  display:grid;
  grid-template-columns:repeat(2,1fr);
  gap:7px
}

.presetgrid button{
  font-size:12px;
  padding:9px
}

.tuneRow{
  display:grid;
  grid-template-columns:1fr auto;
  gap:10px;
  padding:8px 0;
  border-bottom:1px solid rgba(58,65,77,.5)
}

.tuneRow:last-child{
  border-bottom:0
}

.tuneGroup{
  background:var(--panel);
  padding:10px 12px;
  border-radius:12px;
  margin-bottom:9px
}

.notes{
  margin:0;
  padding-left:20px;
  color:var(--muted);
  font-size:12.5px;
  line-height:1.45
}

#toast{
  position:fixed;
  left:14px;
  right:14px;
  bottom:82px;
  background:#111820;
  border:1px solid var(--border);
  border-radius:12px;
  padding:12px;
  display:none;
  z-index:20;
  box-shadow:0 8px 30px rgba(0,0,0,.4)
}

@media(max-width:520px){
  .wrap{
    padding:9px
  }

  .grid{
    gap:7px
  }

  .metric .v{
    font-size:19px
  }

  .setting{
    grid-template-columns:1fr 82px 64px
  }
}
</style>
</head>

<body>

<header>
  <div class="brandline">
    <div>
      <div class="brand">ADT REMOTE</div>
      <div class="sub" id="connection">Not paired</div>
    </div>

    <span class="pill" id="writePill">WRITES OFF</span>
  </div>
</header>

<div class="wrap">

  <div id="pairCard" class="card pair">
    <h2>PAIR WITH WINDOWS ADT</h2>

    <div class="notice">
      Enter the six-digit code shown in Atomic Drift Tuner → Remote.
      Pairing only works from the local/private network.
    </div>

    <input
      id="pairCode"
      inputmode="numeric"
      pattern="[0-9]*"
      maxlength="6"
      placeholder="000000"
      autocomplete="one-time-code">

    <button
      id="pairButton"
      class="primary"
      onclick="pair()">
      PAIR
    </button>

    <div
      class="notice"
      id="pairError"
      style="margin-top:10px">
    </div>
  </div>

  <div id="app" class="hidden">

    <section id="view-dashboard" class="view active">

      <div class="card">
        <div class="row">
          <div>
            <b>Windows ADT</b>
            <div class="muted" id="version">...</div>
          </div>

          <span class="pill ok">LOCAL</span>
        </div>

        <div class="row">
          <div>
            <b id="car">No current car</b>
            <div class="muted" id="context">...</div>
          </div>
        </div>

        <div
          class="notice"
          id="activity">
          ...
        </div>
      </div>

      <div class="card">
        <h2>LIVE ASSETTO CORSA</h2>

        <div class="grid">
          <div class="metric">
            <div class="k">Speed</div>
            <div class="v" id="speed">--</div>
          </div>

          <div class="metric">
            <div class="k">Slip angle</div>
            <div class="v" id="slip">--</div>
          </div>

          <div class="metric">
            <div class="k">Steering</div>
            <div class="v" id="steer">--</div>
          </div>

          <div class="metric">
            <div class="k">FFB output</div>
            <div class="v" id="ffb">--</div>
          </div>
        </div>

        <div class="row">
          <span>Drift detection</span>
          <b id="drift">--</b>
        </div>

        <div class="row">
          <span>Telemetry link</span>
          <b id="telemetryStatus">WAITING</b>
        </div>

        <div
          class="notice"
          id="telemetryError">
        </div>
      </div>

      <div class="card">
        <h2>GENERATED TUNE SNAPSHOT</h2>

        <div class="grid">
          <div class="metric">
            <div class="k">Self-steer</div>
            <div class="v" id="selfScore">--</div>
          </div>

          <div class="metric">
            <div class="k">Stability</div>
            <div class="v" id="stabilityScore">--</div>
          </div>
        </div>

        <div class="row">
          <span>Detail</span>
          <b id="detailScore">--</b>
        </div>

        <div class="row">
          <span>Est. peak wheel torque</span>
          <b id="peakTorque">--</b>
        </div>
      </div>

      <div class="card">
        <h2>PAIRING</h2>

        <button
          onclick="forgetPairing()"
          class="full">
          FORGET THIS PC
        </button>
      </div>

    </section>

    <section id="view-tune" class="view">

      <div class="card">
        <h2>DRIFT TARGET</h2>

        <div class="notice">
          Changing this updates the Windows ADT Drift Target selector.
          It does not write the wheelbase.
        </div>

        <div
          class="selectrow"
          style="margin-top:10px">

          <select id="intentSelect"></select>

          <button onclick="setIntent()">
            SET
          </button>
        </div>

        <button
          id="generateButton"
          class="primary full"
          onclick="generateTune()"
          style="margin-top:10px">
          GENERATE TUNE ON WINDOWS
        </button>

        <div
          class="notice"
          style="margin-top:8px">
          Generate only computes and displays recommendations.
          AZOM is never applied automatically from this button.
        </div>
      </div>

      <div class="card">
        <h2>GENERATED TUNE REVIEW</h2>

        <div
          id="tuneEmpty"
          class="notice">
          Generate a tune for the current car/target to review it here.
        </div>

        <div
          id="tuneReview"
          class="hidden">

          <div
            class="grid"
            style="margin-bottom:10px">

            <div class="metric">
              <div class="k">Self-steer</div>
              <div class="v" id="reviewSelf">--</div>
            </div>

            <div class="metric">
              <div class="k">Stability</div>
              <div class="v" id="reviewStability">--</div>
            </div>
          </div>

          <div class="row">
            <span>Detail</span>
            <b id="reviewDetail">--</b>
          </div>

          <div class="row">
            <span>Peak wheel torque</span>
            <b id="reviewTorque">--</b>
          </div>

          <h3>AZOM / MOZA RECOMMENDATIONS</h3>
          <div id="recommendedAzom"></div>

          <h3>ASSETTO CORSA FFB</h3>
          <div id="recommendedAc"></div>

          <h3>NOTES</h3>
          <ul
            id="tuneNotes"
            class="notes">
          </ul>
        </div>
      </div>

    </section>

    <section id="view-behavior" class="view">

      <div class="card">
        <h2>DESIRED CAR BEHAVIOR</h2>

        <div
          id="behaviorName"
          class="notice">
          Loading current car profile...
        </div>

        <div
          class="notice"
          style="margin-top:6px">
          These values are the per-car handling goals used by the
          AC Car Setup Tuner. They do not directly change AZOM or your wheelbase.
        </div>

        <h3>PRESETS</h3>

        <div class="presetgrid">
          <button onclick="behaviorPreset('neutral')">
            Neutral
          </button>

          <button onclick="behaviorPreset('stable')">
            Stable &amp; Forgiving
          </button>

          <button onclick="behaviorPreset('tandem')">
            Fast Tandem
          </button>

          <button onclick="behaviorPreset('faststable')">
            Fast + Stable
          </button>

          <button onclick="behaviorPreset('aggressive')">
            Aggressive Rotation
          </button>
        </div>

        <div
          id="behaviorControls"
          style="margin-top:10px">

          <div class="behavior">
            <div class="behaviorTop">
              <span>Front-end bite</span>
              <span
                class="behaviorVal"
                id="b-front-v">
                0
              </span>
            </div>

            <input
              id="b-front"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Rear grip</span>
              <span
                class="behaviorVal"
                id="b-rear-v">
                0
              </span>
            </div>

            <input
              id="b-rear"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Self-steer speed</span>
              <span
                class="behaviorVal"
                id="b-self-v">
                0
              </span>
            </div>

            <input
              id="b-self"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Transition speed</span>
              <span
                class="behaviorVal"
                id="b-transition-v">
                0
              </span>
            </div>

            <input
              id="b-transition"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Angle stability</span>
              <span
                class="behaviorVal"
                id="b-angle-v">
                0
              </span>
            </div>

            <input
              id="b-angle"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Throttle steering</span>
              <span
                class="behaviorVal"
                id="b-throttle-v">
                0
              </span>
            </div>

            <input
              id="b-throttle"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

          <div class="behavior">
            <div class="behaviorTop">
              <span>Initiation</span>
              <span
                class="behaviorVal"
                id="b-init-v">
                0
              </span>
            </div>

            <input
              id="b-init"
              type="range"
              min="-2"
              max="2"
              step="1"
              value="0"
              oninput="behaviorChanged()">
          </div>

        </div>

        <button
          id="saveBehaviorButton"
          class="primary full"
          onclick="saveBehavior()"
          style="margin-top:12px">
          SAVE FOR THIS CAR
        </button>

        <div
          id="behaviorStatus"
          class="notice"
          style="margin-top:8px">
        </div>
      </div>

    </section>

    <section id="view-azom" class="view">

      <div class="card">
        <h2>LIVE AZOM / MOZA</h2>

        <div
          class="notice"
          id="azomStatus">
          Reading bridge...
        </div>

        <div id="settings"></div>

        <button
          id="revertButton"
          class="danger full"
          onclick="revertLast()"
          style="margin-top:12px">
          REVERT LAST REMOTE CHANGE
        </button>

        <div
          class="notice"
          style="margin-top:10px">
          Remote Apply is disabled every time ADT starts.
          Enable it explicitly on the Windows PC.
          Every allowed change still passes through ADT's existing
          range checks, serialized write gate, duplicate/rate guards,
          exact AZOM commit path, and live readback verification.
        </div>
      </div>

    </section>

  </div>
</div>

<nav
  id="bottomnav"
  class="bottomnav hidden">

  <button
    id="nav-dashboard"
    class="active"
    onclick="showView('dashboard')">
    DASH
  </button>

  <button
    id="nav-tune"
    onclick="showView('tune')">
    TUNE
  </button>

  <button
    id="nav-behavior"
    onclick="showView('behavior')">
    BEHAVIOR
  </button>

  <button
    id="nav-azom"
    onclick="showView('azom')">
    AZOM
  </button>

</nav>

<div id="toast"></div>

<script>
const TOKEN_KEY='adtRemoteToken';
const LEGACY_TOKEN_KEY='atomicRemoteToken';

let token=
  localStorage.getItem(TOKEN_KEY) ||
  localStorage.getItem(LEGACY_TOKEN_KEY) ||
  '';

if(
  token &&
  !localStorage.getItem(TOKEN_KEY)
){
  localStorage.setItem(
    TOKEN_KEY,
    token
  );

  localStorage.removeItem(
    LEGACY_TOKEN_KEY
  );
}

let writesEnabled=false;
let settingsCache=[];
let statusCache=null;
let activeView='dashboard';
let behaviorDirty=false;
let behaviorContextKey='';

let telemetryRefreshRunning=false;
let statusRefreshRunning=false;
let azomRefreshRunning=false;

let toastTimer=null;

const $=
  id =>
    document.getElementById(id);

function toast(
  msg,
  bad=false
){
  const t=
    $('toast');

  if(toastTimer){
    clearTimeout(
      toastTimer
    );
  }

  t.textContent=
    msg;

  t.style.display=
    'block';

  t.style.borderColor=
    bad
      ? 'var(--danger)'
      : 'var(--border)';

  toastTimer=
    setTimeout(
      () => {
        t.style.display='none';
        toastTimer=null;
      },
      3600
    );
}

function normalizeErrorMessage(
  error
){
  if(
    error &&
    typeof error.message==='string' &&
    error.message.trim()
  ){
    return error.message;
  }

  return 'Request failed.';
}

async function api(
  path,
  options={}
){
  const headers=
    Object.assign(
      {},
      options.headers || {}
    );

  if(token){
    headers['X-ADT-Token']=
      token;
  }

  if(
    options.body !== undefined &&
    options.body !== null &&
    !headers['Content-Type']
  ){
    headers['Content-Type']=
      'application/json';
  }

  const requestOptions=
    Object.assign(
      {},
      options,
      {
        headers,
        cache:'no-store'
      }
    );

  let response;

  try{
    response=
      await fetch(
        path,
        requestOptions
      );
  }
  catch(error){
    throw new Error(
      'Could not reach ADT Remote.'
    );
  }

  const raw=
    await response.text();

  let data=null;

  if(raw){
    try{
      data=
        JSON.parse(raw);
    }
    catch{
      throw new Error(
        'ADT Remote returned an invalid response.'
      );
    }
  }

  if(response.status===401){
    showPair();

    throw new Error(
      'Pairing required.'
    );
  }

  if(!response.ok){
    throw new Error(
      data?.message ||
      data?.error ||
      ('HTTP '+response.status)
    );
  }

  if(data===null){
    throw new Error(
      'ADT Remote returned an empty response.'
    );
  }

  return data;
}

function showPair(){
  token='';

  localStorage.removeItem(
    TOKEN_KEY
  );

  localStorage.removeItem(
    LEGACY_TOKEN_KEY
  );

  $('pairCard')
    .classList
    .remove('hidden');

  $('app')
    .classList
    .add('hidden');

  $('bottomnav')
    .classList
    .add('hidden');

  $('connection')
    .textContent=
    'Not paired';
}

function showApp(){
  $('pairCard')
    .classList
    .add('hidden');

  $('app')
    .classList
    .remove('hidden');

  $('bottomnav')
    .classList
    .remove('hidden');

  $('connection')
    .textContent=
    'Paired • local network';
}

async function pair(){
  const code=
    $('pairCode')
      .value
      .replace(/\D/g,'')
      .slice(0,6);

  $('pairCode').value=
    code;

  $('pairError').textContent=
    '';

  if(code.length!==6){
    $('pairError').textContent=
      'Enter the six-digit pairing code.';

    return;
  }

  const button=
    $('pairButton');

  button.disabled=
    true;

  button.textContent=
    'PAIRING...';

  try{
    const response=
      await fetch(
        '/api/pair',
        {
          method:'POST',
          cache:'no-store',
          headers:{
            'Content-Type':'application/json'
          },
          body:JSON.stringify({
            code
          })
        }
      );

    const raw=
      await response.text();

    let data={};

    if(raw){
      try{
        data=
          JSON.parse(raw);
      }
      catch{
        throw new Error(
          'ADT Remote returned an invalid pairing response.'
        );
      }
    }

    if(!response.ok){
      throw new Error(
        data.error ||
        data.message ||
        'Pairing failed.'
      );
    }

    if(
      !data.token ||
      typeof data.token!=='string'
    ){
      throw new Error(
        'ADT Remote did not return a pairing token.'
      );
    }

    token=
      data.token;

    localStorage.setItem(
      TOKEN_KEY,
      token
    );

    localStorage.removeItem(
      LEGACY_TOKEN_KEY
    );

    $('pairCode').value=
      '';

    showApp();

    await refreshAll();
  }
  catch(error){
    $('pairError').textContent=
      normalizeErrorMessage(
        error
      );
  }
  finally{
    button.disabled=
      false;

    button.textContent=
      'PAIR';
  }
}

function forgetPairing(){
  showPair();

  toast(
    'Pairing removed from this browser.'
  );
}

function showView(
  name
){
  activeView=
    name;

  document
    .querySelectorAll('.view')
    .forEach(
      view =>
        view.classList.remove('active')
    );

  document
    .querySelectorAll('.bottomnav button')
    .forEach(
      button =>
        button.classList.remove('active')
    );

  $('view-'+name)
    .classList
    .add('active');

  $('nav-'+name)
    .classList
    .add('active');

  if(
    name==='behavior' &&
    !behaviorDirty
  ){
    refreshBehavior();
  }

  if(name==='azom'){
    refreshAzom();
  }
}

function prop(
  object,
  camel,
  pascal
){
  return object==null
    ? undefined
    : (
        object[camel]!==undefined
          ? object[camel]
          : object[pascal]
      );
}

function num(
  value,
  digits,
  suffix=''
){
  if(
    value===null ||
    value===undefined ||
    !Number.isFinite(
      Number(value)
    )
  ){
    return '--';
  }

  return (
    Number(value)
      .toFixed(digits) +
    suffix
  );
}

async function refreshStatus(){
  if(statusRefreshRunning){
    return;
  }

  statusRefreshRunning=
    true;

  try{
    const s=
      await api(
        '/api/status'
      );

    statusCache=
      s;

    $('version').textContent=
      'ADT ' +
      (s.atomicVersion || '');

    writesEnabled=
      !!s.remoteWritesEnabled;

    const pill=
      $('writePill');

    pill.textContent=
      writesEnabled
        ? 'REMOTE WRITES ON'
        : 'WRITES OFF';

    pill.className=
      'pill ' +
      (
        writesEnabled
          ? 'ok'
          : 'bad'
      );

    $('activity').textContent=
      s.lastActivity || '';

    const t=
      s.tune || {};

    $('car').textContent=
      t.car ||
      'No current car';

    $('context').textContent=
      [
        t.wheelbase,
        t.steeringWheel,
        t.driftPack,
        t.intent
      ]
      .filter(Boolean)
      .join(' • ');

    $('selfScore').textContent=
      t.hasGeneratedTune
        ? t.selfSteerScore+'/100'
        : '--';

    $('stabilityScore').textContent=
      t.hasGeneratedTune
        ? t.stabilityScore+'/100'
        : '--';

    $('detailScore').textContent=
      t.hasGeneratedTune
        ? t.detailScore+'/100'
        : '--';

    $('peakTorque').textContent=
      t.hasGeneratedTune
        ? num(
            t.estimatedPeakWheelTorqueNm,
            1,
            ' Nm'
          )
        : '--';

    const newKey=
      (t.driftPack || '') +
      '|' +
      (t.car || '');

    if(
      newKey !== behaviorContextKey &&
      !behaviorDirty
    ){
      behaviorContextKey=
        newKey;

      if(activeView==='behavior'){
        refreshBehavior();
      }
    }

    if(
      $('intentSelect').options.length &&
      document.activeElement !==
        $('intentSelect') &&
      t.intent
    ){
      $('intentSelect').value=
        t.intent;
    }

    renderTuneReview(
      t
    );

    renderSettings();
  }
  catch(error){
    if(token){
      $('activity').textContent=
        'ADT Remote status unavailable.';
    }
  }
  finally{
    statusRefreshRunning=
      false;
  }
}

async function refreshIntents(){
  try{
    const items=
      await api(
        '/api/intents'
      );

    const select=
      $('intentSelect');

    select.innerHTML=
      '';

    for(const item of items){
      const option=
        document.createElement(
          'option'
        );

      option.value=
        item.name;

      option.textContent=
        item.name;

      option.selected=
        !!item.selected;

      select.appendChild(
        option
      );
    }
  }
  catch(error){
    toast(
      normalizeErrorMessage(error),
      true
    );
  }
}

async function setIntent(){
  const name=
    $('intentSelect').value;

  try{
    const response=
      await api(
        '/api/intent',
        {
          method:'POST',
          body:JSON.stringify({
            name
          })
        }
      );

    toast(
      response.message ||
      'Drift target updated.'
    );

    await refreshStatus();
  }
  catch(error){
    toast(
      normalizeErrorMessage(error),
      true
    );
  }
}

async function generateTune(){
  const button=
    $('generateButton');

  button.disabled=
    true;

  button.textContent=
    'GENERATING...';

  try{
    const response=
      await api(
        '/api/tune/generate',
        {
          method:'POST',
          body:'{}'
        }
      );

    toast(
      response.message ||
      'Tune generated.'
    );

    await refreshStatus();
  }
  catch(error){
    toast(
      normalizeErrorMessage(error),
      true
    );
  }
  finally{
    button.disabled=
      false;

    button.textContent=
      'GENERATE TUNE ON WINDOWS';
  }
}

function addTuneRows(
  rootId,
  items
){
  const root=
    $(rootId);

  root.innerHTML=
    '';

  for(const item of items){
    const row=
      document.createElement(
        'div'
      );

    row.className=
      'tuneRow';

    const label=
      document.createElement(
        'span'
      );

    label.textContent=
      item[0];

    const value=
      document.createElement(
        'b'
      );

    value.textContent=
      item[1];

    row.append(
      label,
      value
    );

    root.appendChild(
      row
    );
  }
}

function renderTuneReview(
  tune
){
  if(
    !tune ||
    !tune.hasGeneratedTune ||
    !tune.recommendedAzom ||
    !tune.recommendedAc
  ){
    $('tuneEmpty')
      .classList
      .remove('hidden');

    $('tuneReview')
      .classList
      .add('hidden');

    return;
  }

  $('tuneEmpty')
    .classList
    .add('hidden');

  $('tuneReview')
    .classList
    .remove('hidden');

  $('reviewSelf').textContent=
    tune.selfSteerScore +
    '/100';

  $('reviewStability').textContent=
    tune.stabilityScore +
    '/100';

  $('reviewDetail').textContent=
    tune.detailScore +
    '/100';

  $('reviewTorque').textContent=
    num(
      tune.estimatedPeakWheelTorqueNm,
      1,
      ' Nm'
    );

  const azom=
    tune.recommendedAzom;

  const core=
    azom.core || {};

  const wheelbase=
    azom.wheelbaseEffects || {};

  const highSpeed=
    azom.highSpeedDamping || {};

  addTuneRows(
    'recommendedAzom',
    [
      [
        'Rotation',
        num(
          core.wheelRotationAngleDeg,
          0,
          '°'
        )
      ],

      [
        'Game FFB',
        num(
          core.gameFfbStrengthPct,
          0,
          '%'
        )
      ],

      [
        'Base Torque',
        num(
          core.baseTorqueOutputPct,
          0,
          '%'
        )
      ],

      [
        'Max Wheel Speed',
        num(
          core.maximumWheelSpeedPct,
          0,
          '%'
        )
      ],

      [
        'Interpolation',
        num(
          core.interpolation,
          0
        )
      ],

      [
        'Wheel Damper',
        num(
          wheelbase.wheelDamperPct,
          0,
          '%'
        )
      ],

      [
        'Wheel Friction',
        num(
          wheelbase.wheelFrictionPct,
          0,
          '%'
        )
      ],

      [
        'Natural Inertia',
        num(
          wheelbase.naturalInertia,
          0
        )
      ],

      [
        'High-Speed Damping',
        num(
          highSpeed.dampingLevelPct,
          0,
          '%'
        )
      ],

      [
        'High-Speed Trigger',
        num(
          highSpeed.triggerSpeedKph,
          0,
          ' kph'
        )
      ]
    ]
  );

  const ac=
    tune.recommendedAc || {};

  addTuneRows(
    'recommendedAc',
    [
      [
        'Gain',
        num(
          ac.gainPct,
          0,
          '%'
        )
      ],

      [
        'Filter',
        num(
          ac.filterPct,
          0,
          '%'
        )
      ],

      [
        'Minimum Force',
        num(
          ac.minimumForcePct,
          0,
          '%'
        )
      ],

      [
        'Kerb',
        num(
          ac.kerbPct,
          0,
          '%'
        )
      ],

      [
        'Road',
        num(
          ac.roadPct,
          0,
          '%'
        )
      ],

      [
        'Slip',
        num(
          ac.slipPct,
          0,
          '%'
        )
      ],

      [
        'ABS',
        num(
          ac.absPct,
          0,
          '%'
        )
      ]
    ]
  );

  const notes=
    $('tuneNotes');

  notes.innerHTML=
    '';

  const tuneNotes=
    Array.isArray(tune.notes)
      ? tune.notes
      : [];

  for(const note of tuneNotes){
    const li=
      document.createElement(
        'li'
      );

    li.textContent=
      note;

    notes.appendChild(
      li
    );
  }

  if(!tuneNotes.length){
    const li=
      document.createElement(
        'li'
      );

    li.textContent=
      'No additional notes.';

    notes.appendChild(
      li
    );
  }
}

async function refreshTelemetry(){
  if(telemetryRefreshRunning){
    return;
  }

  telemetryRefreshRunning=
    true;

  try{
    const telemetry=
      await api(
        '/api/telemetry'
      );

    const connected=
      !!prop(
        telemetry,
        'connected',
        'Connected'
      );

    const sample=
      prop(
        telemetry,
        'sample',
        'Sample'
      );

    if(
      !connected ||
      !sample
    ){
      $('telemetryStatus').textContent=
        'OFFLINE';

      $('telemetryStatus').className=
        'bad';

      $('telemetryError').textContent=
        prop(
          telemetry,
          'error',
          'Error'
        ) ||
        'Assetto Corsa telemetry unavailable.';

      [
        'speed',
        'slip',
        'steer',
        'ffb'
      ]
      .forEach(
        id =>
          $(id).textContent='--'
      );

      $('drift').textContent=
        '--';

      $('drift').className=
        '';

      return;
    }

    const packet=
      prop(
        sample,
        'packetId',
        'PacketId'
      );

    const speed=
      prop(
        sample,
        'speedKmh',
        'SpeedKmh'
      );

    const slip=
      prop(
        sample,
        'slipAngleDeg',
        'SlipAngleDeg'
      );

    const steer=
      prop(
        sample,
        'steeringAngleDeg',
        'SteeringAngleDeg'
      );

    const ffb=
      prop(
        sample,
        'finalFfb',
        'FinalFfb'
      );

    $('telemetryError').textContent=
      '';

    $('telemetryStatus').textContent=
      'LIVE • packet ' +
      packet;

    $('telemetryStatus').className=
      'ok';

    $('speed').textContent=
      num(
        speed,
        0,
        ' km/h'
      );

    $('slip').textContent=
      num(
        slip,
        1,
        '°'
      );

    $('steer').textContent=
      num(
        steer,
        0,
        '°'
      );

    $('ffb').textContent=
      ffb===null ||
      ffb===undefined
        ? '--'
        : num(
            Math.abs(
              Number(ffb)
            ) * 100,
            0,
            '%'
          );

    const drifting=
      !!prop(
        telemetry,
        'isDrifting',
        'IsDrifting'
      );

    $('drift').textContent=
      drifting
        ? 'YES'
        : 'NO';

    $('drift').className=
      drifting
        ? 'ok'
        : '';
  }
  catch(error){
    $('telemetryStatus').textContent=
      'API ERROR';

    $('telemetryStatus').className=
      'bad';

    $('telemetryError').textContent=
      'Remote telemetry request failed: ' +
      normalizeErrorMessage(error);

    [
      'speed',
      'slip',
      'steer',
      'ffb'
    ]
    .forEach(
      id =>
        $(id).textContent='--'
    );

    $('drift').textContent=
      '--';

    $('drift').className=
      '';
  }
  finally{
    telemetryRefreshRunning=
      false;
  }
}

const behaviorIds=[
  [
    'frontEndBite',
    'b-front'
  ],

  [
    'rearGrip',
    'b-rear'
  ],

  [
    'selfSteerSpeed',
    'b-self'
  ],

  [
    'transitionSpeed',
    'b-transition'
  ],

  [
    'angleStability',
    'b-angle'
  ],

  [
    'throttleSteering',
    'b-throttle'
  ],

  [
    'initiationSharpness',
    'b-init'
  ]
];

function behaviorChanged(){
  behaviorDirty=
    true;

  $('behaviorStatus').textContent=
    'Unsaved changes.';

  for(
    const [
      key,
      id
    ]
    of behaviorIds
  ){
    $(id+'-v').textContent=
      $(id).value;
  }
}

function setBehaviorValues(
  values
){
  for(
    const [
      key,
      id
    ]
    of behaviorIds
  ){
    $(id).value=
      values[key] ?? 0;

    $(id+'-v').textContent=
      $(id).value;
  }
}

function behaviorPreset(
  name
){
  const presets={
    neutral:{
      frontEndBite:0,
      rearGrip:0,
      selfSteerSpeed:0,
      transitionSpeed:0,
      angleStability:0,
      throttleSteering:0,
      initiationSharpness:0
    },

    stable:{
      frontEndBite:-1,
      rearGrip:2,
      selfSteerSpeed:-1,
      transitionSpeed:-1,
      angleStability:2,
      throttleSteering:-1,
      initiationSharpness:-1
    },

    tandem:{
      frontEndBite:1,
      rearGrip:1,
      selfSteerSpeed:1,
      transitionSpeed:1,
      angleStability:1,
      throttleSteering:0,
      initiationSharpness:1
    },

    faststable:{
      frontEndBite:1,
      rearGrip:1,
      selfSteerSpeed:1,
      transitionSpeed:2,
      angleStability:2,
      throttleSteering:0,
      initiationSharpness:1
    },

    aggressive:{
      frontEndBite:2,
      rearGrip:-1,
      selfSteerSpeed:2,
      transitionSpeed:2,
      angleStability:-1,
      throttleSteering:2,
      initiationSharpness:2
    }
  };

  const preset=
    presets[name];

  if(!preset){
    return;
  }

  setBehaviorValues(
    preset
  );

  behaviorDirty=
    true;

  $('behaviorStatus').textContent=
    'Preset loaded • unsaved.';
}

async function refreshBehavior(){
  if(behaviorDirty){
    return;
  }

  try{
    const behavior=
      await api(
        '/api/behavior'
      );

    if(!behavior.ok){
      $('behaviorName').textContent=
        behavior.error ||
        'Desired Behavior unavailable.';

      return;
    }

    $('behaviorName').textContent=
      behavior.displayName ||
      'Current car';

    setBehaviorValues(
      behavior
    );

    behaviorDirty=
      false;

    $('behaviorStatus').textContent=
      'Saved profile loaded.';
  }
  catch(error){
    $('behaviorName').textContent=
      normalizeErrorMessage(error);
  }
}

async function saveBehavior(){
  const body={};

  for(
    const [
      key,
      id
    ]
    of behaviorIds
  ){
    body[key]=
      Number(
        $(id).value
      );
  }

  const button=
    $('saveBehaviorButton');

  button.disabled=
    true;

  try{
    const response=
      await api(
        '/api/behavior',
        {
          method:'POST',
          body:JSON.stringify(
            body
          )
        }
      );

    behaviorDirty=
      false;

    $('behaviorStatus').textContent=
      response.message ||
      'Saved.';

    toast(
      response.message ||
      'Desired Behavior saved.'
    );
  }
  catch(error){
    const message=
      normalizeErrorMessage(
        error
      );

    $('behaviorStatus').textContent=
      message;

    toast(
      message,
      true
    );
  }
  finally{
    button.disabled=
      false;
  }
}

async function refreshAzom(){
  if(azomRefreshRunning){
    return;
  }

  azomRefreshRunning=
    true;

  try{
    const azom=
      await api(
        '/api/azom'
      );

    if(!azom.ok){
      $('azomStatus').textContent=
        azom.error ||
        'AZOM unavailable.';

      settingsCache=[];

      renderSettings();

      return;
    }

    settingsCache=
      Array.isArray(
        azom.settings
      )
        ? azom.settings
        : [];

    $('azomStatus').textContent=
      'Bridge ' +
      (azom.bridgeVersion || '?') +
      ' • ' +
      (
        azom.settingsReadable
          ? 'settings readable'
          : 'settings unavailable'
      ) +
      ' • ' +
      (
        azom.baseConnected===false
          ? 'base disconnected'
          : 'base status OK/unknown'
      );

    renderSettings();
  }
  catch(error){
    $('azomStatus').textContent=
      normalizeErrorMessage(
        error
      );
  }
  finally{
    azomRefreshRunning=
      false;
  }
}

function renderSettings(){
  const root=
    $('settings');

  root.innerHTML=
    '';

  for(const setting of settingsCache){
    const row=
      document.createElement(
        'div'
      );

    row.className=
      'setting';

    const label=
      document.createElement(
        'div'
      );

    const title=
      document.createElement(
        'b'
      );

    title.textContent=
      setting.displayName ||
      setting.propertyName ||
      'AZOM setting';

    const range=
      document.createElement(
        'small'
      );

    range.textContent=
      String(setting.min) +
      '..' +
      String(setting.max) +
      (setting.unit || '');

    label.append(
      title,
      range
    );

    const input=
      document.createElement(
        'input'
      );

    input.type=
      'number';

    input.min=
      setting.min;

    input.max=
      setting.max;

    input.step=
      '1';

    input.value=
      setting.current==null
        ? ''
        : setting.current;

    input.dataset.prop=
      setting.propertyName || '';

    const button=
      document.createElement(
        'button'
      );

    button.textContent=
      'APPLY';

    button.disabled=
      !writesEnabled ||
      !setting.writable ||
      setting.current==null;

    button.onclick=
      () =>
        applySetting(
          setting,
          input
        );

    row.append(
      label,
      input,
      button
    );

    root.appendChild(
      row
    );
  }

  $('revertButton').disabled=
    !writesEnabled;
}

async function applySetting(
  setting,
  input
){
  const value=
    Number(
      input.value
    );

  if(!Number.isInteger(value)){
    toast(
      'Use a whole-number value.',
      true
    );

    return;
  }

  if(
    value < setting.min ||
    value > setting.max
  ){
    toast(
      'Value must stay within ' +
      setting.min +
      '..' +
      setting.max +
      (setting.unit || ''),
      true
    );

    return;
  }

  const confirmed=
    confirm(
      'Apply ' +
      setting.displayName +
      ' = ' +
      value +
      (setting.unit || '') +
      ' through Windows ADT?'
    );

  if(!confirmed){
    return;
  }

  try{
    const response=
      await api(
        '/api/azom/apply',
        {
          method:'POST',
          body:JSON.stringify({
            propertyName:
              setting.propertyName,
            value
          })
        }
      );

    toast(
      response.message ||
      'Applied.'
    );

    await refreshAzom();
    await refreshStatus();
  }
  catch(error){
    toast(
      normalizeErrorMessage(error),
      true
    );

    await refreshAzom();
  }
}

async function revertLast(){
  const confirmed=
    confirm(
      'Revert the last remote AZOM change from this ADT run?'
    );

  if(!confirmed){
    return;
  }

  const button=
    $('revertButton');

  button.disabled=
    true;

  try{
    const response=
      await api(
        '/api/azom/revert',
        {
          method:'POST',
          body:'{}'
        }
      );

    toast(
      response.message ||
      'Reverted.'
    );

    await refreshAzom();
    await refreshStatus();
  }
  catch(error){
    toast(
      normalizeErrorMessage(error),
      true
    );
  }
  finally{
    button.disabled=
      !writesEnabled;
  }
}

async function refreshAll(){
  await Promise.allSettled(
    [
      refreshStatus(),
      refreshTelemetry(),
      refreshAzom(),
      refreshIntents()
    ]
  );

  if(activeView==='behavior'){
    await refreshBehavior();
  }
}

$('pairCode').addEventListener(
  'input',
  event => {
    const cleaned=
      event.target.value
        .replace(/\D/g,'')
        .slice(0,6);

    if(
      event.target.value !== cleaned
    ){
      event.target.value=
        cleaned;
    }
  }
);

$('pairCode').addEventListener(
  'keydown',
  event => {
    if(event.key==='Enter'){
      event.preventDefault();
      pair();
    }
  }
);

if(token){
  showApp();
  refreshAll();
}
else{
  showPair();
}

setInterval(
  () => {
    if(
      token &&
      document.visibilityState==='visible'
    ){
      refreshTelemetry();
    }
  },
  300
);

setInterval(
  () => {
    if(
      token &&
      document.visibilityState==='visible'
    ){
      refreshStatus();
    }
  },
  1400
);

setInterval(
  () => {
    if(
      token &&
      activeView==='azom' &&
      document.visibilityState==='visible'
    ){
      refreshAzom();
    }
  },
  1800
);

document.addEventListener(
  'visibilitychange',
  () => {
    if(
      token &&
      document.visibilityState==='visible'
    ){
      refreshAll();
    }
  }
);
</script>

</body>
</html>
""";
}
