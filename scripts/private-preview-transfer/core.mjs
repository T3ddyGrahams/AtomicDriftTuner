import {
  constants, createCipheriv, createDecipheriv, createPublicKey,
  privateDecrypt, publicEncrypt, randomBytes,
} from 'node:crypto';

export const BUCKET = 'adt-preview-builds';
export const MANAGER = 'https://adt-preview-downloads.atomicdrifttuner.workers.dev';
export const GRANT_SECONDS = 30 * 60;
const MAX_FILE_SIZE = 250 * 1024 * 1024;
const VERSION = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-preview\.(0|[1-9]\d*)$/;
const ALGORITHM = 'RSA-OAEP-256+A256GCM';
const AAD = Buffer.from('ADT private upload envelope v1', 'utf8');

function reject() { throw new Error('Invalid private transfer data.'); }
function exactKeys(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) reject();
  const actual = Object.keys(value).sort();
  if (actual.length !== keys.length || actual.some((key, index) => key !== [...keys].sort()[index])) reject();
}
function decode(value, minimum = 1, maximum = 128 * 1024) {
  if (typeof value !== 'string' || value.length > maximum * 2 || !/^[A-Za-z0-9+/]+={0,2}$/.test(value)) reject();
  const bytes = Buffer.from(value, 'base64');
  if (bytes.length < minimum || bytes.length > maximum || bytes.toString('base64') !== value) reject();
  return bytes;
}
function validVersion(version) {
  return typeof version === 'string' && version.length <= 48 && VERSION.test(version);
}
function rsaPublicKey(encoded) {
  const key = createPublicKey({ key: decode(encoded, 300, 1024), type: 'spki', format: 'der' });
  if (key.asymmetricKeyType !== 'rsa' || key.asymmetricKeyDetails?.modulusLength !== 3072 ||
      key.asymmetricKeyDetails?.publicExponent !== 65537n) reject();
  return key;
}

export function parseManifest(raw) {
  try {
    if (Buffer.isBuffer(raw)) raw = raw.toString('utf8');
    if (typeof raw === 'string' && raw.length > 16 * 1024) reject();
    const value = typeof raw === 'string' ? JSON.parse(raw) : raw;
    exactKeys(value, ['schema', 'requestId', 'version', 'hours', 'publicKey', 'files']);
    if (value.schema !== 1 || !/^[0-9a-f]{32}$/.test(value.requestId) || !validVersion(value.version) ||
        !Number.isInteger(value.hours) || value.hours < 1 || value.hours > 168) reject();
    rsaPublicKey(value.publicKey);
    if (!Array.isArray(value.files) || value.files.length !== 2) reject();
    const names = ['setup.exe', 'portable.zip'].map(suffix => `AtomicDriftTuner-${value.version}-${suffix}`);
    for (const file of value.files) {
      exactKeys(file, ['name', 'size', 'sha256']);
      if (!names.includes(file.name) || !Number.isSafeInteger(file.size) || file.size < 1 || file.size > MAX_FILE_SIZE ||
          typeof file.sha256 !== 'string' || !/^[0-9a-f]{64}$/.test(file.sha256)) reject();
    }
    if (new Set(value.files.map(file => file.name)).size !== 2) reject();
    // Return a detached, normalized object; callers cannot accidentally retain extra fields.
    return { schema: 1, requestId: value.requestId, version: value.version, hours: value.hours,
      publicKey: value.publicKey, files: names.map(name => ({ ...value.files.find(file => file.name === name) })) };
  } catch { reject(); }
}

export function seal(payload, publicKey) {
  try {
    const plain = Buffer.from(JSON.stringify(payload), 'utf8');
    if (plain.length > 128 * 1024) reject();
    const key = randomBytes(32);
    const iv = randomBytes(12);
    try {
      const cipher = createCipheriv('aes-256-gcm', key, iv);
      cipher.setAAD(AAD);
      const ciphertext = Buffer.concat([cipher.update(plain), cipher.final()]);
      const wrapped = publicEncrypt({ key: rsaPublicKey(publicKey), padding: constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha256' }, key);
      return { schema: 1, alg: ALGORITHM, key: wrapped.toString('base64'), iv: iv.toString('base64'),
        tag: cipher.getAuthTag().toString('base64'), ciphertext: ciphertext.toString('base64') };
    } finally { key.fill(0); plain.fill(0); }
  } catch { reject(); }
}

export function open(envelope, privateKey) {
  try {
    if (typeof envelope === 'string') {
      if (envelope.length > 256 * 1024) reject();
      envelope = JSON.parse(envelope);
    }
    exactKeys(envelope, ['schema', 'alg', 'key', 'iv', 'tag', 'ciphertext']);
    if (envelope.schema !== 1 || envelope.alg !== ALGORITHM) reject();
    const key = privateDecrypt({ key: privateKey, padding: constants.RSA_PKCS1_OAEP_PADDING,
      oaepHash: 'sha256' }, decode(envelope.key, 384, 384));
    try {
      if (key.length !== 32) reject();
      const decipher = createDecipheriv('aes-256-gcm', key, decode(envelope.iv, 12, 12));
      decipher.setAAD(AAD);
      decipher.setAuthTag(decode(envelope.tag, 16, 16));
      const plain = Buffer.concat([decipher.update(decode(envelope.ciphertext)), decipher.final()]);
      try { return JSON.parse(plain.toString('utf8')); } finally { plain.fill(0); }
    } finally { key.fill(0); }
  } catch { reject(); }
}

export function fileKey(version, name) { return `ADT ${version}/${name}`; }
export function announcementKey(version) { return `.adt-announcements/${version}.json`; }

function permittedKey(key) {
  if (typeof key !== 'string') return false;
  if (key.startsWith('.adt-announcements/') && key.endsWith('.json')) {
    return validVersion(key.slice('.adt-announcements/'.length, -'.json'.length));
  }
  const match = /^ADT ([^/]+)\/(.+)$/.exec(key);
  return !!match && validVersion(match[1]) && ['setup.exe', 'portable.zip'].some(suffix =>
    match[2] === `AtomicDriftTuner-${match[1]}-${suffix}`);
}

export function validateUrl(raw, accountId, key) {
  try {
    if (typeof raw !== 'string' || raw.length > 16 * 1024 || /[\s\\]/.test(raw) ||
        typeof accountId !== 'string' || !/^[a-fA-F0-9]{32}$/.test(accountId) || !permittedKey(key)) reject();
    const url = new URL(raw);
    const expectedPath = `/${BUCKET}/${key}`.split('/').map(part => encodeURIComponent(part)).join('/');
    if (url.protocol !== 'https:' || url.hostname !== `${accountId.toLowerCase()}.r2.cloudflarestorage.com` ||
        url.port || url.username || url.password || url.hash || url.pathname !== expectedPath ||
        url.searchParams.get('X-Amz-Algorithm') !== 'AWS4-HMAC-SHA256' ||
        !/^[a-f0-9]{64}$/.test(url.searchParams.get('X-Amz-Signature') ?? '') ||
        !/^\d{8}T\d{6}Z$/.test(url.searchParams.get('X-Amz-Date') ?? '') ||
        !url.searchParams.get('X-Amz-Credential') || !url.searchParams.get('X-Amz-SignedHeaders')) reject();
    const expiry = Number(url.searchParams.get('X-Amz-Expires'));
    if (!Number.isInteger(expiry) || expiry < 1 || expiry > GRANT_SECONDS) reject();
    const names = [...url.searchParams.keys()];
    if (new Set(names).size !== names.length) reject();
    return url.href;
  } catch { reject(); }
}
