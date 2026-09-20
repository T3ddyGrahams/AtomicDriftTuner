import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync } from 'node:crypto';
import { Readable } from 'node:stream';
import { S3Client } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { BUCKET, MANAGER, parseManifest, seal, open, validateUrl } from '../core.mjs';
import { grant, finalize } from '../runner.mjs';
import { validateGrant } from '../client.mjs';

const accountId = 'a'.repeat(32);
const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
const contents = [Buffer.from('exact local installer bytes'), Buffer.from('exact local portable bytes')];
const manifest = parseManifest({ schema: 1, requestId: 'b'.repeat(32), version: '0.9.0-preview.12', hours: 24,
  publicKey: publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
  files: ['setup.exe', 'portable.zip'].map((suffix, index) => ({ name: `AtomicDriftTuner-0.9.0-preview.12-${suffix}`,
    size: contents[index].length, sha256: createHash('sha256').update(contents[index]).digest('hex') })) });
const now = Date.now();
const env = { R2_ACCOUNT_ID: accountId, R2_BUCKET_NAME: BUCKET, R2_ACCESS_KEY_ID: 'DUMMY_TEST_ACCESS_KEY',
  R2_SECRET_ACCESS_KEY: 'DUMMY_TEST_SECRET_KEY', PREVIEW_PUBLISH_TOKEN: 'DUMMY_TEST_MANAGER_TOKEN' };
const missing = () => { throw Object.assign(new Error('not found'), { $metadata: { httpStatusCode: 404 } }); };
const metadata = index => ({ ContentLength: manifest.files[index].size,
  Metadata: { sha256: manifest.files[index].sha256 }, ETag: `"test-etag-${index}"` });
function indexFor(command) { return command.input.Key.endsWith('setup.exe') ? 0 : 1; }
function fixture(send = async () => missing()) {
  const calls = [];
  return { calls, env, now: () => now,
    s3: { send },
    fetch: async (url, options) => {
      calls.push({ url, options });
      assert.equal(url, `${MANAGER}/api/prepare-preview`);
      assert.equal(options.redirect, 'error');
      return { ok: true, json: async () => ({ managerUrl: `${MANAGER}/admin?announcement=${manifest.version}` }) };
    },
  };
}
function realSigner() {
  const client = new S3Client({ region: 'auto', endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
    forcePathStyle: true, credentials: { accessKeyId: env.R2_ACCESS_KEY_ID, secretAccessKey: env.R2_SECRET_ACCESS_KEY },
    requestChecksumCalculation: 'WHEN_REQUIRED', responseChecksumValidation: 'WHEN_REQUIRED' });
  return (_unused, command, options) => getSignedUrl(client, command, options);
}
function validObjects(command) {
  const index = indexFor(command);
  if (command.constructor.name === 'HeadObjectCommand') return metadata(index);
  assert.equal(command.constructor.name, 'GetObjectCommand');
  assert.equal(command.input.IfMatch, metadata(index).ETag);
  return { ...metadata(index), Body: Readable.from([contents[index]]) };
}

test('manifest accepts the exact pair and rejects extra fields, duplicate names, path injection and invalid sizes', () => {
  assert.deepEqual(parseManifest(JSON.stringify(manifest)), manifest);
  for (const mutate of [
    value => { value.bucket = 'public'; },
    value => { value.version = '0.9.0-preview.12/../../other'; },
    value => { value.requestId = '$(echo secret)'; },
    value => { value.files[1] = value.files[0]; },
    value => { value.files[0].name = '../setup.exe'; },
    value => { value.files[0].size = 0; },
    value => { value.files[0].size = 250 * 1024 * 1024 + 1; },
    value => { value.files[0].sha256 = 'F'.repeat(64); },
    value => { value.publicKey = 'not a public key'; },
    value => { value.hours = 169; },
  ]) {
    const copy = structuredClone(manifest); mutate(copy);
    assert.throws(() => parseManifest(copy), /Invalid private transfer data/);
  }
});

test('hybrid envelope reveals no plaintext and rejects tampering, algorithm substitution and wrong keys', () => {
  const payload = { privateUrl: 'https://private.example/secret-capability' };
  const envelope = seal(payload, manifest.publicKey);
  assert.equal(JSON.stringify(envelope).includes(payload.privateUrl), false);
  assert.deepEqual(open(envelope, privateKey), payload);
  for (const field of ['key', 'iv', 'tag', 'ciphertext']) {
    const copy = { ...envelope };
    const bytes = Buffer.from(copy[field], 'base64'); bytes[0] ^= 1;
    copy[field] = bytes.toString('base64');
    assert.throws(() => open(copy, privateKey), /Invalid private transfer data/);
  }
  assert.throws(() => open({ ...envelope, alg: 'none' }, privateKey));
  const wrongKey = generateKeyPairSync('rsa', { modulusLength: 3072 }).privateKey;
  assert.throws(() => open(envelope, wrongKey));
});

test('real SDK signed grant survives encryption and the real local client validation', async () => {
  const deps = { ...fixture(), presign: realSigner() };
  const envelope = await grant(manifest, deps);
  const text = JSON.stringify(envelope);
  for (const secret of [env.R2_SECRET_ACCESS_KEY, env.PREVIEW_PUBLISH_TOKEN, 'X-Amz-Signature', manifest.files[0].name]) {
    assert.equal(text.includes(secret), false);
  }
  const payload = open(envelope, privateKey);
  const validated = validateGrant(payload, manifest, now);
  assert.equal(validated.accountId, accountId);
  assert.equal(payload.expiresAt, now + 1800_000);
  for (const file of payload.files) {
    const url = new URL(file.putUrl);
    const signed = url.searchParams.get('X-Amz-SignedHeaders').split(';');
    for (const name of ['host', 'content-length', 'content-type', 'if-none-match', 'x-amz-meta-sha256']) assert.ok(signed.includes(name));
    assert.equal(url.searchParams.has('x-amz-meta-sha256'), false);
    assert.equal(file.headers['if-none-match'], '*');
    assert.equal(file.headers['content-length'], String(file.size));
    assert.equal(url.searchParams.get('X-Amz-Expires'), '1800');
  }
  assert.equal(deps.calls.length, 1);
  assert.equal(deps.calls[0].options.method, 'GET');
});

test('URL validation rejects wrong bucket, object, account, insecure host and duplicate signature parameters', async () => {
  const payload = open(await grant(manifest, { ...fixture(), presign: realSigner() }), privateKey);
  const file = payload.files[0];
  for (const corrupt of [
    url => { url.pathname = url.pathname.replace(BUCKET, 'public-bucket'); },
    url => { url.pathname += '.other'; },
    url => { url.hostname = 'evil.example'; },
    url => { url.protocol = 'http:'; },
    url => { url.searchParams.append('X-Amz-Signature', '1'.repeat(64)); },
    url => { url.searchParams.set('X-Amz-Expires', '1801'); },
  ]) {
    const url = new URL(file.putUrl); corrupt(url);
    assert.throws(() => validateUrl(url.href, accountId, file.key));
  }
  assert.throws(() => validateUrl(file.putUrl, 'c'.repeat(32), file.key));
  assert.throws(() => validateUrl(file.putUrl, accountId, '../secret'));
});

test('grant refuses an existing object, mismatched bucket or unauthorized availability check', async () => {
  let signed = false;
  const deps = { ...fixture(async () => metadata(0)), presign: async () => { signed = true; } };
  await assert.rejects(grant(manifest, deps), /already exists/);
  assert.equal(signed, false);
  await assert.rejects(grant(manifest, { ...fixture(), env: { ...env, R2_BUCKET_NAME: 'another-bucket' } }), /configuration/);
  await assert.rejects(grant(manifest, fixture(async () => { throw Object.assign(new Error('secret-url'),
    { $metadata: { httpStatusCode: 403 } }); })), /availability check failed/);
});

test('grant fails if signer omits required signed constraints', async () => {
  const sign = realSigner();
  await assert.rejects(grant(manifest, { ...fixture(), presign: async (...args) => {
    const url = new URL(await sign(...args));
    url.searchParams.set('X-Amz-SignedHeaders', 'host');
    return url.href;
  } }), /constraints were not signed/);
});

test('finalize verifies actual bytes and ETags before preparing the private announcement', async () => {
  const deps = fixture(async command => validObjects(command));
  assert.equal(await finalize(manifest, deps), `${MANAGER}/admin?announcement=${manifest.version}`);
  assert.deepEqual(deps.calls.map(call => call.options.method), ['GET', 'POST']);
  assert.deepEqual(JSON.parse(deps.calls[1].options.body), { version: manifest.version, hours: 24 });
});

test('finalize rejects hash mismatch even with matching metadata and never prepares announcement', async () => {
  const deps = fixture(async command => {
    const result = validObjects(command);
    if (result.Body) result.Body = Readable.from([Buffer.alloc(manifest.files[indexFor(command)].size, 33)]);
    return result;
  });
  await assert.rejects(finalize(manifest, deps), /SHA-256 verification failed/);
  assert.equal(deps.calls.length, 1);
});

test('finalize rejects length mismatch, missing hash metadata and replacement during verification', async () => {
  for (const change of ['length', 'metadata', 'replacement']) {
    let heads = 0;
    const deps = fixture(async command => {
      const result = validObjects(command);
      if (command.constructor.name === 'HeadObjectCommand') {
        heads++;
        if (change === 'metadata') result.Metadata = {};
        if (change === 'replacement' && heads > 2) result.ETag = '"changed"';
      } else if (change === 'length') result.Body = Readable.from([Buffer.from('short')]);
      return result;
    });
    await assert.rejects(finalize(manifest, deps));
    assert.equal(deps.calls.length, 1);
  }
});

test('finalize polls for late uploads with a bounded timeout and does not publish partial uploads', async () => {
  let clock = now;
  let waits = 0;
  const deps = { ...fixture(async () => missing()), now: () => clock,
    sleep: async ms => { assert.ok(ms > 0 && ms <= 15_000); clock += ms; waits++; } };
  await assert.rejects(finalize(manifest, deps), /Timed out/);
  assert.equal(clock - now, 20 * 60_000);
  assert.equal(waits, 80);
  assert.equal(deps.calls.length, 1);
});

test('finalize accepts files arriving after one wait', async () => {
  let ready = false;
  const deps = { ...fixture(async command => ready ? validObjects(command) : missing()), sleep: async () => { ready = true; } };
  assert.equal(await finalize(manifest, deps), `${MANAGER}/admin?announcement=${manifest.version}`);
});

test('manager failures and unexpected redirects/responses stay sanitized', async () => {
  for (const fetch of [async () => { throw new Error('Bearer DUMMY_TEST_MANAGER_TOKEN private URL'); },
    async () => ({ ok: false }), async (_url, options) => ({ ok: true,
      json: async () => ({ managerUrl: 'https://evil.example/announcement' }) })]) {
    const deps = { ...fixture(async command => validObjects(command)), fetch };
    await assert.rejects(finalize(manifest, deps), error => {
      assert.equal(error.message, 'Private release manager request failed.');
      return true;
    });
  }
});
