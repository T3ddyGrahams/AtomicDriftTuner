namespace AtomicDriftTuner.Services;

public static partial class RemoteWebApp
{
    public static string RenderTouchLauncher(Models.ThemeSettings theme)
    {
        ThemeService.Validate(theme);
        static string Css(string value)
        {
            var c = ThemeService.ParseThemeColor(value);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        }
        var palette = new[] { ("bg", theme.AppBackground), ("text", theme.PrimaryText),
            ("heading", theme.SectionHeading), ("accent", theme.Accent),
            ("accent-text", theme.AccentText), ("focus", theme.FocusBorder) };
        var css = ":root{" + string.Join("", palette.Select(p => $"--{p.Item1}:{Css(p.Item2)};")) + "}";
        return TouchLauncherHtml.Replace("/* ADT_LAUNCH_PALETTE */", css);
    }

    // This unpaired launcher contains no telemetry, credentials or mutation controls.
    // SimHub's web renderer has a fixed canvas. A deliberate top-level navigation
    // gives ADT the real viewport without changing SimHub or weakening /dash framing.
    public const string TouchLauncherHtml = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open ADT Control Center</title>
<style>
/* ADT_LAUNCH_PALETTE */
*{box-sizing:border-box}html,body{margin:0;min-height:100%;background:var(--bg);color:var(--text);font-family:system-ui,sans-serif}
body{min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;padding:32px;text-align:center;gap:24px}
h1{color:var(--heading);font-size:56px;line-height:1.1;margin:0}p{font-size:38px;line-height:1.25;margin:0;max-width:1080px}
a{display:flex;align-items:center;justify-content:center;min-height:208px;width:100%;max-width:1080px;padding:24px;background:var(--accent);color:var(--accent-text);border:4px solid var(--accent);border-radius:24px;font-size:72px;font-weight:800;line-height:1.15;text-decoration:none}
a:focus-visible{outline:6px solid var(--focus);outline-offset:6px}
</style></head><body>
<h1>ADT CONTROL CENTER</h1>
<p>Automatically fits this screen.</p>
<a id="openAdt" href="/dash" target="_top">Open ADT</a>
<p>Then tap Full screen. No resolution settings needed.</p>
<script>if(window.top===window.self)location.replace('/dash');</script>
</body></html>
""";

    private const string DisplayToolbar = """
  <div class="display-tools">
    <span class="notice">Fits your screen automatically</span>
    <button id="fullscreenButton" type="button" onclick="toggleFullscreen()" aria-pressed="false">Full screen</button>
  </div>
  <div id="displayHelp" class="notice" role="status" aria-live="polite"></div>
""";

    private const string DisplayStyles = """
.touchscreen{min-height:100vh;min-height:100dvh;padding-bottom:calc(var(--nav-height,80px) + 16px)}
.touchscreen .wrap{max-width:none;width:100%;padding-left:max(12px,env(safe-area-inset-left));padding-right:max(12px,env(safe-area-inset-right))}
.touchscreen header{display:flex;align-items:center;gap:8px 16px;flex-wrap:wrap}
.touchscreen .brandline{flex:1 1 220px;flex-wrap:wrap}
.touchscreen .display-tools{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap;max-width:100%}
.touchscreen .display-tools button{margin:0;min-width:120px}
.touchscreen #displayHelp:empty{display:none}
.touchscreen #displayHelp{flex-basis:100%;padding-top:8px;max-width:75ch}
.touchscreen .bottomnav{max-width:none;left:0;right:0}
.touchscreen dialog{max-height:calc(100vh - 24px);max-height:calc(100dvh - 24px);overflow:auto}
.touchscreen :is(button,input,select,summary){scroll-margin-top:12px;scroll-margin-bottom:12px}
html.touch-display{scroll-padding-top:calc(var(--header-height,140px) + 12px);scroll-padding-bottom:calc(var(--nav-height,80px) + 16px)}
@media(max-height:540px){.touchscreen header{position:static;padding-top:8px;padding-bottom:8px}.touchscreen .display-tools>.notice{display:none}}
@media(max-width:900px){.touchscreen .display-tools>.notice{display:none}}
@media(min-width:1500px){.touchscreen .grid{grid-template-columns:repeat(4,minmax(0,1fr))}.touchscreen .card{padding:20px}}
""";

    private const string DisplayScript = """
document.documentElement.classList.add('touch-display');
function fullscreenElement(){return document.fullscreenElement||document.webkitFullscreenElement;}
function updateFullscreenButton(){
  const active=!!fullscreenElement();
  $('fullscreenButton').textContent=active?'Exit full screen':'Full screen';
  $('fullscreenButton').setAttribute('aria-pressed',String(active));
}
async function toggleFullscreen(){
  const button=$('fullscreenButton');
  if(button.disabled)return;
  button.disabled=true;$('displayHelp').textContent='';
  try{
    if(fullscreenElement()){
      const exit=document.exitFullscreen||document.webkitExitFullscreen;
      if(!exit)throw new Error('Fullscreen unavailable');
      await exit.call(document);
    }else{
      const enter=document.documentElement.requestFullscreen||document.documentElement.webkitRequestFullscreen;
      if(!enter)throw new Error('Fullscreen unavailable');
      await enter.call(document.documentElement);
    }
  }catch{
    $('displayHelp').textContent='This browser could not enter full screen. Use its Full screen menu (F11 in Chromium on a Pi), or open this page in the device’s browser. The layout still fits the available space.';
  }finally{button.disabled=false;updateFullscreenButton();}
}
let displayResizePending=false;
function queueDisplayResize(){
  if(displayResizePending)return;
  displayResizePending=true;
  requestAnimationFrame(()=>{
    displayResizePending=false;
    const header=document.querySelector('header'),nav=$('bottomnav');
    document.documentElement.style.setProperty('--header-height',(getComputedStyle(header).position==='sticky'?header.getBoundingClientRect().height:0)+'px');
    document.documentElement.style.setProperty('--nav-height',nav.getBoundingClientRect().height+'px');
  });
}
document.addEventListener('fullscreenchange',()=>{updateFullscreenButton();queueDisplayResize();});
document.addEventListener('webkitfullscreenchange',()=>{updateFullscreenButton();queueDisplayResize();});
window.addEventListener('resize',queueDisplayResize);
window.visualViewport?.addEventListener('resize',queueDisplayResize);
if(typeof ResizeObserver!=='undefined'){
  const observer=new ResizeObserver(queueDisplayResize);
  observer.observe(document.querySelector('header'));observer.observe($('bottomnav'));
}
updateFullscreenButton();queueDisplayResize();
""";
}
