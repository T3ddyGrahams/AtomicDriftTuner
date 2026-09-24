// Exercise the shipped remote renderer without connecting to a real ADT server or wheel.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../src/AtomicDriftTuner/Services/RemoteWebApp.cs'), 'utf8');
const script = source.split('<script>')[1].split('</script>')[0];
new vm.Script(script); // Entire shipped script must parse.
function block(name) {
  const start = script.indexOf('function ' + name + '(');
  assert(start >= 0);
  const end = script.indexOf('\nfunction ', start + 1);
  return script.slice(start, end < 0 ? undefined : end);
}
class Element {
  constructor() { this.children = []; this.textContent = ''; this.classes = new Set(); this.classList = { add: x => this.classes.add(x), remove: x => this.classes.delete(x) }; }
  set innerHTML(x) { assert.equal(x, ''); this.children = []; }
  append(...items) { this.children.push(...items); }
  appendChild(item) { this.children.push(item); }
}
const elements = new Map();
const $ = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
const context = vm.createContext({ $, document: { createElement: () => new Element() }, num: (v, places, suffix = '') => Number.isFinite(v) ? v.toFixed(places) + suffix : '—' });
vm.runInContext(block('addTuneRows') + '\n' + block('renderTuneReview'), context);
const g27 = { overallEffectsStrength: 125, springEffectStrength: 15, damperEffectStrength: 20, centeringSpringStrength: 50,
  enableCenteringSpring: false, degreesOfRotation: 540, reportCombinedPedals: false, allowGameToAdjustSettings: true };
context.renderTuneReview({ hasGeneratedTune: true, recommendedAzom: null, logitechG27: g27,
  recommendedAc: { gainPct: 48, filterPct: 0, minimumForcePct: 9, kerbPct: 0, roadPct: 0, slipPct: 0, absPct: 0 }, notes: ['Manual plan'] });
assert.equal($('wheelbaseReviewHeading').textContent, 'LOGITECH G27 MANUAL PLAN');
assert.equal($('recommendedAzom').children.length, 9);
assert.equal($('recommendedAzom').children[1].children[1].textContent, '125%');
assert.equal($('recommendedAc').children[0].children[1].textContent, '48%');
assert.equal($('reviewTorque').textContent, 'Not modelled');
assert.equal($('tuneReview').classes.has('hidden'), false);
context.renderTuneReview({ hasGeneratedTune: true, recommendedAzom: { core: { wheelRotationAngleDeg: 900 } }, recommendedAc: { gainPct: 70 }, notes: [] });
assert.equal($('wheelbaseReviewHeading').textContent, 'AZOM / MOZA RECOMMENDATIONS');
assert.equal($('recommendedAzom').children[0].children[1].textContent, '900°');
context.renderTuneReview(null);
assert.equal($('tuneReview').classes.has('hidden'), true);
console.log('PASS G27 remote review, provider switching, empty state and full JavaScript syntax');
