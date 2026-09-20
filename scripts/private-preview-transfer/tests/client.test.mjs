import assert from 'node:assert/strict';
import { generateKeyPairSync, createHash } from 'node:crypto';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { open, parseManifest, seal } from '../core.mjs';
import { prepare, upload, validateGrant, validateAnnouncement, verifyResponse } from '../client.mjs';

const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
const accountId = 'a'.repeat(32);
const contents = [Buffer.from('fixture installer'), Buffer.from('fixture archive')];

function request() {
  return parseManifest({ schema: 1, requestId: 'b'.repeat(32), version: '0.9.0-preview.12', hours: 24,
    publicKey: publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
    files: ['setup.exe', 'portable.zip'].map((suffix, i) => ({
      name: `AtomicDriftTuner-0.9.0-preview.12-${suffix}`, size: contents[i].length,
      sha256: createHash('sha256').update(contents[i]).digest('hex'),
    })),
  });
}

function url(key, signed = 'host') {
  const path = `adt-preview-builds/${key}`.split('/').map(encodeURIComponent).join('/');
  return `https://${accountId}.r2.cloudflarestorage.com/${path}?` + new URLSearchParams({
    'X-Amz-Algorithm': 'AWS4-HMAC-SHA256', 'X-Amz-Credential': 'FIXTURE/20260914/auto/s3/aws4_request',
    'X-Amz-Date': '20260914T120000Z', 'X-Amz-Expires': '1800',
    'X-Amz-Signature': 'c'.repeat(64), 'X-Amz-SignedHeaders': signed,
  });
}

function permission() {
  const manifest = request();
  return { schema: 1, manifest, expiresAt: Date.now() + 1_800_000, accountId, bucket: 'adt-preview-builds',
    files: manifest.files.map(file => {
      const key = `ADT ${manifest.version}/${file.name}`;
      const headers = { 'content-length': String(file.size), 'content-type': file.name.endsWith('.zip') ? 'application/zip' : 'application/octet-stream',
        'if-none-match': '*', 'x-amz-meta-sha256': file.sha256 };
      return { ...file, key, headers, getUrl: url(key), putUrl: url(key, ['host', ...Object.keys(headers)].sort().join(';')) };
    }),
    announcementUrl: url(`.adt-announcements/${manifest.version}.json`),
  };
}

async function fixture(t) {
  const dir = await mkdtemp(join(tmpdir(), 'adt-transfer-client-test-'));
  t.after(() => {
    const target = resolve(dir);
    assert.equal(dirname(target), resolve(tmpdir()));
    assert.ok(basename(target).startsWith('adt-transfer-client-test-'));
    return rm(target, { recursive: true, force: true });
  });
  for (const [i, file] of request().files.entries()) await writeFile(join(dir, file.name), contents[i]);
  return dir;
}

test('encrypted handoff contains no plaintext upload URLs and remains bound to its request', () => {
  const grant = permission();
  const encrypted = seal(grant, request().publicKey);
  assert.equal(JSON.stringify(encrypted).includes('cloudflarestorage'), false);
  const restored = validateGrant(open(encrypted, privateKey), request());
  assert.equal(restored.files[0].key, grant.files[0].key);
  const wrong = request();
  wrong.requestId = 'd'.repeat(32);
  assert.throws(() => validateGrant(restored, wrong));
});

test('rejects expired, foreign, duplicate and unsigned upload permissions', () => {
  const mutations = [
    g => { g.expiresAt = Date.now() - 1; },
    g => { g.bucket = 'public'; },
    g => { g.files[0].putUrl = g.files[0].putUrl.replace('.r2.cloudflarestorage.com', '.example.com'); },
    g => { g.files[1] = g.files[0]; },
    g => { delete g.files[0].headers['if-none-match']; },
    g => { g.files[0].putUrl = url(g.files[0].key); },
    g => { g.files[0].headers.authorization = 'unexpected'; },
    g => { g.announcementUrl = g.files[0].getUrl; },
  ];
  for (const mutate of mutations) {
    const grant = permission();
    mutate(grant);
    assert.throws(() => validateGrant(grant, request()));
  }
});

test('prepare never overwrites an earlier private key and binds actual local package bytes', async t => {
  const dir = await fixture(t);
  const state = join(dir, 'state');
  const manifest = await prepare('0.9.0-preview.12', dir, state);
  assert.deepEqual(manifest.files, request().files);
  const keyBefore = await readFile(join(state, 'transfer.private.pem'));
  await assert.rejects(prepare('0.9.0-preview.12', dir, state));
  assert.deepEqual(await readFile(join(state, 'transfer.private.pem')), keyBefore);
  assert.equal((await readFile(join(state, 'request.json'), 'utf8')).includes('PRIVATE KEY'), false);
});

test('changed second package prevents either upload', async t => {
  const dir = await fixture(t);
  await writeFile(join(dir, request().files[1].name), 'modified');
  let calls = 0;
  await assert.rejects(upload(permission(), request(), dir, async () => { calls++; }));
  assert.equal(calls, 0);
});

test('uploads exact bytes with overwrite protection and verifies downloaded bytes', async t => {
  const dir = await fixture(t);
  let puts = 0;
  let gets = 0;
  const mockFetch = async (target, options) => {
    assert.equal(options.redirect, 'error');
    const i = target.includes('setup.exe') ? 0 : 1;
    if (options.method === 'PUT') {
      puts++;
      assert.equal(options.headers['if-none-match'], '*');
      const chunks = [];
      for await (const chunk of options.body) chunks.push(chunk);
      assert.deepEqual(Buffer.concat(chunks), contents[i]);
      return new Response(null, { status: 200 });
    }
    gets++;
    return new Response(contents[i], { headers: { 'content-length': String(contents[i].length) } });
  };
  await upload(permission(), request(), dir, mockFetch);
  assert.equal(puts, 2);
  assert.equal(gets, 2);
});

test('a conditional-put retry succeeds only when the existing object matches', async t => {
  const dir = await fixture(t);
  await upload(permission(), request(), dir, async (target, options) => {
    const i = target.includes('setup.exe') ? 0 : 1;
    return options.method === 'PUT' ? new Response(null, { status: 412 }) :
      new Response(contents[i], { headers: { 'content-length': String(contents[i].length) } });
  });
  const corrupt = Buffer.from(contents[0]);
  corrupt[0] ^= 1;
  await assert.rejects(verifyResponse(new Response(corrupt, { headers: { 'content-length': String(corrupt.length) } }), request().files[0]));
  await assert.rejects(verifyResponse(new Response(contents[0], { headers: { 'content-length': '1' } }), request().files[0]));
});

test('private announcement must contain only this release and its trusted manager links', () => {
  const manifest = request();
  const expires = Math.floor(Date.now() / 1000) + 3600;
  const result = { expires, hours: manifest.hours, links: manifest.files.map(file => {
    const key = `ADT ${manifest.version}/${file.name}`;
    return { fileName: file.name, key, size: file.size,
      url: `https://adt-preview-downloads.atomicdrifttuner.workers.dev/download/${key.split('/').map(encodeURIComponent).join('/')}?expires=${expires}&sig=fixture` };
  }) };
  assert.equal(validateAnnouncement(result, manifest), result);
  assert.throws(() => validateAnnouncement({ ...result, hours: 168 }, manifest));
  assert.throws(() => validateAnnouncement({ ...result, expires: expires + 168 * 3600 }, manifest));
  result.links[0].url = result.links[0].url.replace('.workers.dev', '.example.com');
  assert.throws(() => validateAnnouncement(result, manifest));
});
