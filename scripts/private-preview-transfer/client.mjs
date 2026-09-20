import { createHash, generateKeyPairSync, randomBytes } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { open, parseManifest, validateUrl } from './core.mjs';

const BUCKET = 'adt-preview-builds';
const MANAGER = 'https://adt-preview-downloads.atomicdrifttuner.workers.dev';
const MAX_JSON = 1024 * 1024;

export async function fileDigest(path) {
  const hash = createHash('sha256');
  let size = 0;
  for await (const chunk of createReadStream(path)) {
    size += chunk.length;
    hash.update(chunk);
  }
  return { size, sha256: hash.digest('hex') };
}

async function readJson(path) {
  if ((await stat(path)).size > MAX_JSON) throw new Error('Oversized transfer document.');
  return JSON.parse(await readFile(path, 'utf8'));
}

export async function prepare(version, packagesDir, stateDir, hours = 24) {
  // A new directory prevents replacing an earlier transfer's private key.
  const files = [];
  for (const suffix of ['setup.exe', 'portable.zip']) {
    const name = `AtomicDriftTuner-${version}-${suffix}`;
    if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-preview\.(0|[1-9]\d*)$/.test(version)) {
      throw new Error('Invalid preview version.');
    }
    files.push({ name, ...await fileDigest(join(packagesDir, name)) });
  }
  const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
  const manifest = parseManifest(JSON.stringify({
    schema: 1, requestId: randomBytes(16).toString('hex'), version, hours,
    publicKey: publicKey.export({ format: 'der', type: 'spki' }).toString('base64'), files,
  }));
  await mkdir(stateDir, { mode: 0o700 });
  await writeFile(join(stateDir, 'transfer.private.pem'), privateKey.export({ format: 'pem', type: 'pkcs8' }), { flag: 'wx', mode: 0o600 });
  await writeFile(join(stateDir, 'request.json'), JSON.stringify(manifest), { flag: 'wx', mode: 0o600 });
  return manifest;
}

export function validateGrant(payload, expected, now = Date.now()) {
  if (!payload || payload.schema !== 1 || payload.bucket !== BUCKET ||
      !Number.isSafeInteger(payload.expiresAt) || payload.expiresAt <= now || payload.expiresAt > now + 31 * 60_000) {
    throw new Error('Invalid or expired upload permission.');
  }
  const actual = parseManifest(JSON.stringify(payload.manifest));
  if (JSON.stringify(actual) !== JSON.stringify(expected)) throw new Error('Upload permission belongs to a different request.');
  if (!Array.isArray(payload.files) || payload.files.length !== 2) throw new Error('Invalid upload file list.');
  const seen = new Set();
  for (const file of payload.files) {
    const original = expected.files.find(item => item.name === file.name);
    if (!original || seen.has(file.name) || file.size !== original.size || file.sha256 !== original.sha256 ||
        file.key !== `ADT ${expected.version}/${file.name}`) throw new Error('Upload file does not match the request.');
    seen.add(file.name);
    validateUrl(file.putUrl, payload.accountId, file.key);
    validateUrl(file.getUrl, payload.accountId, file.key);
    const headers = Object.fromEntries(Object.entries(file.headers ?? {}).map(([k, v]) => [k.toLowerCase(), v]));
    const contentType = file.name.endsWith('.zip') ? 'application/zip' : 'application/octet-stream';
    if (Object.keys(headers).length !== 4 || headers['if-none-match'] !== '*' ||
        String(headers['content-length']) !== String(file.size) || headers['content-type'] !== contentType ||
        headers['x-amz-meta-sha256'] !== file.sha256) throw new Error('Invalid upload restrictions.');
    const signed = new URL(file.putUrl).searchParams.get('X-Amz-SignedHeaders')?.split(';') ?? [];
    for (const name of ['host', ...Object.keys(headers)]) {
      if (!signed.includes(name)) throw new Error('Upload restrictions are not signed.');
    }
  }
  validateUrl(payload.announcementUrl, payload.accountId, `.adt-announcements/${expected.version}.json`);
  return payload;
}

export async function verifyResponse(response, expected) {
  if (!response.ok || !response.body) throw new Error('Private download verification failed.');
  if (Number(response.headers.get('content-length')) !== expected.size) {
    await response.body.cancel();
    throw new Error('Uploaded file length differs from the tested file.');
  }
  const hash = createHash('sha256');
  let size = 0;
  for await (const chunk of response.body) {
    size += chunk.length;
    if (size > expected.size) throw new Error('Uploaded file exceeds expected length.');
    hash.update(chunk);
  }
  if (size !== expected.size || hash.digest('hex') !== expected.sha256) throw new Error('Uploaded file differs from the tested file.');
}

export async function upload(payload, expected, packagesDir, fetchImpl = fetch) {
  validateGrant(payload, expected);
  // Validate both local files before sending either one.
  for (const file of payload.files) {
    const digest = await fileDigest(join(packagesDir, file.name));
    if (digest.size !== file.size || digest.sha256 !== file.sha256) throw new Error('A local package changed after preparation.');
  }
  for (const file of payload.files) {
    validateGrant(payload, expected);
    const body = createReadStream(join(packagesDir, file.name));
    let response;
    try {
      response = await fetchImpl(file.putUrl, {
        method: 'PUT', headers: file.headers, body, duplex: 'half', redirect: 'error',
        signal: AbortSignal.timeout(10 * 60_000),
      });
    } finally { body.destroy(); }
    await response.body?.cancel();
    // A retry may find its own completed object. Never overwrite; require matching bytes.
    if (!response.ok && response.status !== 412) throw new Error('Private upload failed.');
    const downloaded = await fetchImpl(file.getUrl, { redirect: 'error', signal: AbortSignal.timeout(10 * 60_000) });
    await verifyResponse(downloaded, file);
  }
}

export function validateAnnouncement(result, manifest, now = Date.now()) {
  if (!result || !Number.isSafeInteger(result.expires) || result.expires * 1000 <= now ||
      result.hours !== manifest.hours || result.expires * 1000 > now + manifest.hours * 3_600_000 + 60_000 ||
      !Array.isArray(result.links) || result.links.length !== 2) {
    throw new Error('Invalid private announcement.');
  }
  const seen = new Set();
  for (const link of result.links) {
    const file = manifest.files.find(item => item.name === link.fileName);
    if (!file || seen.has(file.name) || link.key !== `ADT ${manifest.version}/${file.name}` || link.size !== file.size) {
      throw new Error('Announcement does not match these packages.');
    }
    seen.add(file.name);
    const url = new URL(link.url);
    if (url.origin !== MANAGER || url.username || url.password || url.hash ||
        decodeURIComponent(url.pathname) !== `/download/${link.key}` ||
        url.searchParams.get('expires') !== String(result.expires) || !url.searchParams.get('sig')) {
      throw new Error('Invalid supporter download link.');
    }
  }
  return result;
}

async function loadPermission(stateDir, grantPath) {
  const manifest = parseManifest(JSON.stringify(await readJson(join(stateDir, 'request.json'))));
  const privateKey = await readFile(join(stateDir, 'transfer.private.pem'), 'utf8');
  const payload = open(await readJson(grantPath), privateKey);
  return { manifest, payload: validateGrant(payload, manifest) };
}

async function main(args) {
  const [command, ...rest] = args;
  if (command === 'prepare' && rest.length >= 3 && rest.length <= 4) {
    await prepare(rest[0], resolve(rest[1]), resolve(rest[2]), rest[3] === undefined ? 24 : Number(rest[3]));
    console.log('Public request saved. The private key stays in the local state directory.');
  } else if (command === 'upload' && rest.length === 3) {
    const { manifest, payload } = await loadPermission(resolve(rest[0]), resolve(rest[1]));
    await upload(payload, manifest, resolve(rest[2]));
    console.log('Both private uploads match the tested local files. Wait for the workflow to finish preparing links.');
  } else if (command === 'links' && rest.length === 2) {
    const stateDir = resolve(rest[0]);
    const { manifest, payload } = await loadPermission(stateDir, resolve(rest[1]));
    const response = await fetch(payload.announcementUrl, { redirect: 'error', signal: AbortSignal.timeout(60_000) });
    if (!response.ok || !response.body) throw new Error('Prepared links are not available yet.');
    const chunks = [];
    let size = 0;
    for await (const chunk of response.body) {
      size += chunk.length;
      if (size > MAX_JSON) throw new Error('Invalid announcement size.');
      chunks.push(Buffer.from(chunk));
    }
    const result = validateAnnouncement(JSON.parse(Buffer.concat(chunks).toString('utf8')), manifest);
    await writeFile(join(stateDir, 'announcement.json'), JSON.stringify(result, null, 2), { flag: 'wx', mode: 0o600 });
    console.log('Supporter links saved privately in the local state directory.');
  } else {
    throw new Error('Use prepare <version> <packages-dir> <new-state-dir> [hours], upload <state-dir> <trusted-grant-file> <packages-dir>, or links <state-dir> <trusted-grant-file>.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).catch(() => {
    // URLs and request exceptions may contain bearer permissions. Never log them.
    console.error('Private transfer did not complete. No credentials or private links were printed. Check the request, trusted workflow artifact, package hashes and transfer expiry.');
    process.exitCode = 1;
  });
}
