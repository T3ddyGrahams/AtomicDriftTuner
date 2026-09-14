import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { S3Client, HeadObjectCommand, PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { BUCKET, MANAGER, GRANT_SECONDS, parseManifest, seal, validateUrl, fileKey, announcementKey } from './core.mjs';

const REQUIRED_PUT_HEADERS = ['content-length', 'content-type', 'if-none-match', 'x-amz-meta-sha256'];
const FINALIZE_WAIT_MS = 20 * 60 * 1000;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function dependencies(overrides) {
  const env = overrides.env ?? process.env;
  if (!/^[a-fA-F0-9]{32}$/.test(env.R2_ACCOUNT_ID ?? '') ||
      !env.R2_ACCESS_KEY_ID || !env.R2_SECRET_ACCESS_KEY || !env.PREVIEW_PUBLISH_TOKEN ||
      (env.R2_BUCKET_NAME && env.R2_BUCKET_NAME !== BUCKET)) {
    throw new Error('Missing private publishing configuration.');
  }
  // An explicit credential object prevents any profile, metadata or credential-store lookup.
  const accountId = env.R2_ACCOUNT_ID.toLowerCase();
  const s3 = overrides.s3 ?? new S3Client({
    region: 'auto', endpoint: `https://${accountId}.r2.cloudflarestorage.com`, forcePathStyle: true,
    credentials: { accessKeyId: env.R2_ACCESS_KEY_ID, secretAccessKey: env.R2_SECRET_ACCESS_KEY },
    maxAttempts: 2, requestChecksumCalculation: 'WHEN_REQUIRED', responseChecksumValidation: 'WHEN_REQUIRED',
  });
  return { s3, accountId, token: env.PREVIEW_PUBLISH_TOKEN,
    fetch: overrides.fetch ?? globalThis.fetch, presign: overrides.presign ?? getSignedUrl,
    now: overrides.now ?? Date.now, sleep: overrides.sleep ?? sleep };
}

async function head(s3, key) {
  try { return await s3.send(new HeadObjectCommand({ Bucket: BUCKET, Key: key })); }
  catch (error) {
    if (error?.$metadata?.httpStatusCode === 404) return null;
    throw new Error('Private object availability check failed.');
  }
}

async function managerRequest(deps, method, body) {
  try {
    const response = await deps.fetch(`${MANAGER}/api/prepare-preview`, {
      method, redirect: 'error', signal: AbortSignal.timeout(90_000),
      headers: { Authorization: `Bearer ${deps.token}`, ...(body ? { 'Content-Type': 'application/json' } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
    if (!response.ok) throw new Error('Rejected');
    if (method === 'GET') return null;
    const result = await response.json();
    const expected = `${MANAGER}/admin?announcement=${encodeURIComponent(body.version)}`;
    if (!result || result.managerUrl !== expected) throw new Error('Unexpected result');
    return expected;
  } catch { throw new Error('Private release manager request failed.'); }
}

function assertSignedHeaders(raw) {
  const url = new URL(raw);
  const signed = (url.searchParams.get('X-Amz-SignedHeaders') ?? '').split(';');
  if (REQUIRED_PUT_HEADERS.some(header => !signed.includes(header)) ||
      [...url.searchParams.keys()].some(key => key.toLowerCase() === 'x-amz-meta-sha256')) {
    throw new Error('Required upload constraints were not signed.');
  }
}

/** Return only an encrypted, short-lived capability bundle; never return credentials. */
export async function grant(rawManifest, overrides = {}) {
  const manifest = parseManifest(rawManifest);
  const deps = dependencies(overrides);
  await managerRequest(deps, 'GET');
  for (const file of manifest.files) {
    if (await head(deps.s3, fileKey(manifest.version, file.name)) !== null) {
      throw new Error('This preview already exists; existing packages will not be overwritten.');
    }
  }
  const signedAt = deps.now();
  const options = { expiresIn: GRANT_SECONDS, signingDate: new Date(signedAt) };
  const files = [];
  for (const file of manifest.files) {
    const key = fileKey(manifest.version, file.name);
    const headers = { 'content-length': String(file.size),
      'content-type': file.name.endsWith('.zip') ? 'application/zip' : 'application/octet-stream',
      'if-none-match': '*', 'x-amz-meta-sha256': file.sha256 };
    const putUrl = validateUrl(await deps.presign(deps.s3, new PutObjectCommand({
      Bucket: BUCKET, Key: key, ContentLength: file.size, ContentType: headers['content-type'],
      IfNoneMatch: '*', Metadata: { sha256: file.sha256 },
    }), { ...options, signableHeaders: new Set(REQUIRED_PUT_HEADERS),
      unhoistableHeaders: new Set(['x-amz-meta-sha256']) }), deps.accountId, key);
    assertSignedHeaders(putUrl);
    const getUrl = validateUrl(await deps.presign(deps.s3,
      new GetObjectCommand({ Bucket: BUCKET, Key: key }), options), deps.accountId, key);
    files.push({ ...file, key, putUrl, getUrl, headers });
  }
  const announcementUrl = validateUrl(await deps.presign(deps.s3,
    new GetObjectCommand({ Bucket: BUCKET, Key: announcementKey(manifest.version) }), options),
  deps.accountId, announcementKey(manifest.version));
  return seal({ schema: 1, manifest, expiresAt: signedAt + GRANT_SECONDS * 1000,
    accountId: deps.accountId, bucket: BUCKET, files, announcementUrl }, manifest.publicKey);
}

async function verifyObject(deps, manifest, file, metadata) {
  if (metadata.ContentLength !== file.size || metadata.Metadata?.sha256 !== file.sha256 ||
      typeof metadata.ETag !== 'string' || !metadata.ETag) throw new Error('Uploaded package metadata does not match.');
  let result;
  try {
    result = await deps.s3.send(new GetObjectCommand({ Bucket: BUCKET,
      Key: fileKey(manifest.version, file.name), IfMatch: metadata.ETag }));
  } catch { throw new Error('Uploaded package could not be verified.'); }
  if (result.ContentLength !== file.size || result.ETag !== metadata.ETag || !result.Body?.[Symbol.asyncIterator]) {
    result.Body?.destroy?.();
    throw new Error('Uploaded package response does not match.');
  }
  const hash = createHash('sha256');
  let length = 0;
  try {
    for await (const chunk of result.Body) {
      const bytes = Buffer.from(chunk);
      length += bytes.length;
      if (length > file.size) throw new Error('Length mismatch');
      hash.update(bytes);
    }
  } catch {
    result.Body?.destroy?.();
    throw new Error('Uploaded package could not be verified.');
  }
  if (length !== file.size || hash.digest('hex') !== file.sha256) {
    throw new Error('Uploaded package SHA-256 verification failed.');
  }
}

/** Wait for the two local uploads, verify their actual bytes, then prepare the private announcement. */
export async function finalize(rawManifest, overrides = {}) {
  const manifest = parseManifest(rawManifest);
  const deps = dependencies(overrides);
  await managerRequest(deps, 'GET');
  const deadline = deps.now() + FINALIZE_WAIT_MS;
  let metadata;
  while (true) {
    metadata = [];
    for (const file of manifest.files) metadata.push(await head(deps.s3, fileKey(manifest.version, file.name)));
    if (metadata.every(item => item !== null)) break;
    if (deps.now() >= deadline) throw new Error('Timed out waiting for the two private uploads.');
    await deps.sleep(Math.min(15_000, deadline - deps.now()));
  }
  for (let index = 0; index < manifest.files.length; index++) {
    await verifyObject(deps, manifest, manifest.files[index], metadata[index]);
  }
  // Recheck ETags immediately before preparation so a concurrent replacement cannot pass unnoticed.
  for (let index = 0; index < manifest.files.length; index++) {
    const current = await head(deps.s3, fileKey(manifest.version, manifest.files[index].name));
    if (!current || current.ETag !== metadata[index].ETag || current.ContentLength !== manifest.files[index].size ||
        current.Metadata?.sha256 !== manifest.files[index].sha256) throw new Error('Uploaded package changed during verification.');
  }
  return managerRequest(deps, 'POST', { version: manifest.version, hours: manifest.hours });
}

async function main(args) {
  const [mode, manifestPath, outputPath] = args;
  if ((mode !== 'grant' && mode !== 'finalize') || !manifestPath ||
      (mode === 'grant' ? args.length !== 3 : args.length !== 2)) throw new Error('Invalid arguments.');
  const manifest = parseManifest(await readFile(manifestPath, 'utf8'));
  if (mode === 'grant') {
    const envelope = await grant(manifest);
    // Refuse to replace a previously prepared file or emit plaintext to stdout.
    await writeFile(outputPath, `${JSON.stringify(envelope)}\n`, { flag: 'wx', mode: 0o600 });
  } else {
    process.stdout.write(`${await finalize(manifest)}\n`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).catch(() => {
    process.stderr.write('Private transfer failed. Check the configuration and package manifest; no credentials or links were logged.\n');
    process.exitCode = 1;
  });
}
