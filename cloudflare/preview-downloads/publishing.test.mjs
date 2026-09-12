import assert from 'node:assert/strict';
import test from 'node:test';
import worker from './worker.js';

const origin = 'https://adt-preview-downloads.atomicdrifttuner.workers.dev';
const version = '0.9.0-preview.9';
const master = 'unit-test-only-master';
const publisher = 'unit-test-only-publisher';
function fixture(missing = false) {
  const saved = new Map();
  const bucket = {
    async head(key) {
      if (missing && key.endsWith('.zip')) return null;
      return {key, size: 1234, uploaded: new Date()};
    },
    async put(key, value) { saved.set(key, value); },
    async get(key) { return saved.has(key) ? {async json() { return JSON.parse(saved.get(key)); }} : null; },
  };
  return { saved, env: {DOWNLOAD_TOKEN: master, PREVIEW_PUBLISH_TOKEN: publisher, PREVIEW_BUILDS: bucket} };
}
function prepare(env, body = {version,hours:24}, token = publisher, method = 'POST') {
  return worker.fetch(new Request(origin + '/api/prepare-preview', {
    method, headers:{Authorization: `Bearer ${token}`},
    ...(method === 'POST' ? {body:JSON.stringify(body)} : {}),
  }),env);
}
async function login(env, announcement = version) {
  return worker.fetch(new Request(origin + '/admin', {method:'POST',headers:{Origin:origin},
    body:new URLSearchParams({action:'login',token:master,announcement})}),env);
}
test('publishing fails closed and does not accept the master credential', async () => {
  const {env,saved} = fixture();
  assert.equal((await prepare({...env,PREVIEW_PUBLISH_TOKEN:undefined})).status,503);
  assert.equal((await prepare(env,undefined,master)).status,401);
  assert.equal((await prepare(env,undefined,publisher,'GET')).status,204);
  assert.equal(saved.size,0);
});
test('publishing rejects invalid versions, expiry, malformed JSON, and missing files', async () => {
  const {env,saved} = fixture();
  for (const body of [null, {version:'../../other',hours:24},{version:'1.0.0',hours:24},{version,hours:169},{version,hours:0},{version,hours:'24'}]) {
    assert.equal((await prepare(env,body)).status,400);
  }
  const malformed = new Request(origin+'/api/prepare-preview',{method:'POST',headers:{Authorization:`Bearer ${publisher}`},body:'{'});
  assert.equal((await worker.fetch(malformed,env)).status,400);
  assert.equal((await prepare(fixture(true).env)).status,404);
  assert.equal(saved.size,0);
});
test('prepared links stay in private R2 and API only returns the manager address', async () => {
  const {env,saved} = fixture();
  const response = await prepare(env);
  assert.equal(response.status,200);
  const body = await response.text();
  assert.deepEqual(JSON.parse(body), {managerUrl:origin+'/admin?announcement='+version});
  assert.doesNotMatch(body,/sig=|unit-test-only/);
  const result = JSON.parse(saved.get(`.adt-announcements/${version}.json`));
  assert.equal(result.links.length,2);
  assert.match(result.discordPost,/Installer/);
  assert.match(result.discordPost,/Portable ZIP/);
  assert.ok(result.links.every(link => new URL(link.url).origin === origin));
  assert.doesNotMatch(JSON.stringify(result),/unit-test-only/);
});
test('prepared announcement requires admin sign-in and preserves its destination', async () => {
  const {env} = fixture();
  await prepare(env);
  const url = origin+'/admin?announcement='+version;
  const page = await worker.fetch(new Request(url),env);
  const html = await page.text();
  assert.match(html,/name="announcement"/);
  assert.doesNotMatch(html,/sig=/);
  const signedIn = await login(env);
  assert.equal(signedIn.headers.get('Location'),'/admin?announcement='+version);
  const cookie = signedIn.headers.get('Set-Cookie').split(';')[0];
  const result = await worker.fetch(new Request(url,{headers:{Cookie:cookie}}),env);
  assert.equal(result.status,200);
  assert.match(await result.text(),/id="discord-post"/);
  assert.equal((await login(env,'https://example.com')).headers.get('Location'),'/admin');
});
test('missing and expired announcements have useful authenticated errors', async () => {
  const {env,saved} = fixture();
  const signedIn = await login(env);
  const headers = {Cookie:signedIn.headers.get('Set-Cookie').split(';')[0]};
  const url = origin+'/admin?announcement='+version;
  assert.equal((await worker.fetch(new Request(url,{headers}),env)).status,404);
  await prepare(env);
  const key = `.adt-announcements/${version}.json`;
  const result = JSON.parse(saved.get(key)); result.expires = 1;
  saved.set(key,JSON.stringify(result));
  assert.equal((await worker.fetch(new Request(url,{headers}),env)).status,410);
});
test('same-origin browser form policy and cross-site protection remain intact', async () => {
  const {env} = fixture();
  const page = await worker.fetch(new Request(origin+'/admin'),env);
  assert.equal(page.headers.get('Referrer-Policy'),'same-origin');
  for (const badOrigin of ['null','https://example.com']) {
    const request = new Request(origin+'/admin',{method:'POST',headers:{Origin:badOrigin},body:new URLSearchParams({action:'login',token:master})});
    assert.equal((await worker.fetch(request,env)).status,403);
  }
});
