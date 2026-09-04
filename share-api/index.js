"use strict";

const http = require("http");
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const zlib = require("zlib");

const HOST = process.env.ATOMIC_SHARE_HOST || "127.0.0.1";
const PORT = Number(process.env.ATOMIC_SHARE_PORT || "8787");
const DATA_FILE = process.env.ATOMIC_SHARE_DATA || path.join(__dirname, "data", "shares.json");
const TRUST_PROXY = process.env.ATOMIC_SHARE_TRUST_PROXY === "1";

const PORTABLE_PREFIX = "AT1-";
const SHORT_PREFIX = "AT-";
const SHORT_ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
const SHORT_CHARS = 6;
const MAX_PORTABLE_CHARS = 2000;
const MAX_COMPRESSED_BYTES = 32 * 1024;
const MAX_JSON_BYTES = 96 * 1024;
const MAX_REQUEST_BYTES = 8 * 1024;

const publishBuckets = new Map();
const readBuckets = new Map();

function nowIso() {
  return new Date().toISOString();
}

function json(res, status, body, extraHeaders = {}) {
  const data = Buffer.from(JSON.stringify(body));
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": data.length,
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    ...extraHeaders,
  });
  res.end(data);
}

function clientIp(req) {
  if (TRUST_PROXY) {
    const forwarded = req.headers["x-forwarded-for"];
    if (typeof forwarded === "string" && forwarded.trim()) {
      return forwarded.split(",")[0].trim();
    }
  }
  return req.socket.remoteAddress || "unknown";
}

function rateAllowed(map, key, max, windowMs) {
  const now = Date.now();
  let bucket = map.get(key);
  if (!bucket || now - bucket.startedAt >= windowMs) {
    bucket = { startedAt: now, count: 0 };
    map.set(key, bucket);
  }
  if (bucket.count >= max) return false;
  bucket.count += 1;
  return true;
}

function normalizeShortCode(value) {
  const raw = String(value || "").trim().toUpperCase();
  const body = raw.startsWith(SHORT_PREFIX) ? raw.slice(SHORT_PREFIX.length) : raw;
  if (!new RegExp(`^[${SHORT_ALPHABET}]{${SHORT_CHARS}}$`).test(body)) {
    throw new Error(`Short Share Codes must look like ${SHORT_PREFIX}XXXXXX.`);
  }
  return SHORT_PREFIX + body;
}

function generateShortCode() {
  const bytes = crypto.randomBytes(SHORT_CHARS);
  let body = "";
  for (let i = 0; i < SHORT_CHARS; i += 1) {
    body += SHORT_ALPHABET[bytes[i] % SHORT_ALPHABET.length];
  }
  return SHORT_PREFIX + body;
}

function base64UrlDecode(value) {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padding = (4 - (base64.length % 4)) % 4;
  return Buffer.from(base64 + "=".repeat(padding), "base64");
}

function decodePortable(payload) {
  const compact = String(payload || "").replace(/\s+/g, "");
  if (!compact.startsWith(PORTABLE_PREFIX)) {
    throw new Error(`Portable Share Codes must start with ${PORTABLE_PREFIX}.`);
  }
  if (compact.length > MAX_PORTABLE_CHARS) {
    throw new Error(`Portable Share Code exceeds ${MAX_PORTABLE_CHARS} characters.`);
  }

  const compressed = base64UrlDecode(compact.slice(PORTABLE_PREFIX.length));
  if (!compressed.length || compressed.length > MAX_COMPRESSED_BYTES) {
    throw new Error("Portable Share Code has an invalid compressed size.");
  }

  let raw;
  try {
    raw = zlib.gunzipSync(compressed, { maxOutputLength: MAX_JSON_BYTES });
  } catch {
    throw new Error("Portable Share Code could not be decompressed.");
  }
  if (!raw.length || raw.length > MAX_JSON_BYTES) {
    throw new Error("Portable Share Code decoded size is invalid.");
  }

  let parsed;
  try {
    parsed = JSON.parse(raw.toString("utf8"));
  } catch {
    throw new Error("Portable Share Code contains invalid JSON.");
  }

  if (parsed?.schema !== "atomic-share/v1") {
    throw new Error("Unsupported Atomic Share schema.");
  }
  if (!parsed?.input?.hardware || !parsed?.input?.wheel || !parsed?.input?.pack || !parsed?.input?.car || !parsed?.input?.intent) {
    throw new Error("Portable Share Code is missing required input sections.");
  }
  if (!parsed?.behavior || !parsed?.recommendation?.azom || !parsed?.recommendation?.assettoCorsa) {
    throw new Error("Portable Share Code is missing required tuning sections.");
  }
  if (String(parsed.input.car.packId || "").toLowerCase() !== String(parsed.input.pack.id || "").toLowerCase()) {
    throw new Error("Portable Share Code car/pack identity does not match.");
  }

  return { compact, parsed };
}

function publicSummary(parsed) {
  return {
    atomicVersion: String(parsed.atomicVersion || ""),
    createdUtc: parsed.createdUtc || null,
    wheelbase: String(parsed.input?.hardware?.model || ""),
    wheel: String(parsed.input?.wheel?.model || ""),
    pack: String(parsed.input?.pack?.name || ""),
    car: String(parsed.input?.car?.displayName || ""),
    driftTarget: String(parsed.input?.intent?.name || ""),
    selfSteerScore: Number(parsed.recommendation?.selfSteerScore || 0),
    stabilityScore: Number(parsed.recommendation?.stabilityScore || 0),
    detailScore: Number(parsed.recommendation?.detailScore || 0),
  };
}

function emptyStore() {
  return { schema: "atomic-share-registry/v1", shares: {} };
}

function loadStore() {
  try {
    const data = JSON.parse(fs.readFileSync(DATA_FILE, "utf8"));
    if (data?.schema !== "atomic-share-registry/v1" || typeof data?.shares !== "object") {
      throw new Error("unsupported registry schema");
    }
    return data;
  } catch (error) {
    if (error.code === "ENOENT") return emptyStore();
    console.error("Failed to read share registry:", error.message);
    throw error;
  }
}

function saveStore(store) {
  fs.mkdirSync(path.dirname(DATA_FILE), { recursive: true });
  const tmp = `${DATA_FILE}.${process.pid}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(store, null, 2), { mode: 0o600 });
  fs.renameSync(tmp, DATA_FILE);
}

function findExistingByHash(store, hash) {
  for (const [code, record] of Object.entries(store.shares)) {
    if (record?.sha256 === hash) return code;
  }
  return null;
}

function createRecord(portable) {
  const { compact, parsed } = decodePortable(portable);
  const sha256 = crypto.createHash("sha256").update(compact, "utf8").digest("hex");
  const store = loadStore();

  const existing = findExistingByHash(store, sha256);
  if (existing) {
    return { code: existing, record: store.shares[existing], deduplicated: true };
  }

  let code;
  for (let attempt = 0; attempt < 100; attempt += 1) {
    const candidate = generateShortCode();
    if (!store.shares[candidate]) {
      code = candidate;
      break;
    }
  }
  if (!code) throw new Error("Could not allocate a short Share Code.");

  const record = {
    schema: "atomic-share-record/v1",
    code,
    payload: compact,
    sha256,
    createdAt: nowIso(),
    summary: publicSummary(parsed),
  };
  store.shares[code] = record;
  saveStore(store);
  return { code, record, deduplicated: false };
}

function getRecord(code) {
  const normalized = normalizeShortCode(code);
  const store = loadStore();
  return store.shares[normalized] || null;
}

async function readJsonBody(req) {
  const chunks = [];
  let total = 0;
  for await (const chunk of req) {
    total += chunk.length;
    if (total > MAX_REQUEST_BYTES) {
      const error = new Error("Request body is too large.");
      error.status = 413;
      throw error;
    }
    chunks.push(chunk);
  }
  if (!chunks.length) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    const error = new Error("Request body must be valid JSON.");
    error.status = 400;
    throw error;
  }
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host || "localhost"}`);
    const ip = clientIp(req);

    if (req.method === "GET" && url.pathname === "/health") {
      return json(res, 200, {
        ok: true,
        service: "atomic-share-api",
        schema: "atomic-share-registry/v1",
        time: nowIso(),
      });
    }

    if (req.method === "POST" && url.pathname === "/v1/shares") {
      if (!rateAllowed(publishBuckets, ip, 10, 60 * 60 * 1000)) {
        return json(res, 429, { error: "Publish rate limit exceeded. Try again later." });
      }
      if (!String(req.headers["content-type"] || "").toLowerCase().startsWith("application/json")) {
        return json(res, 415, { error: "Content-Type must be application/json." });
      }
      const body = await readJsonBody(req);
      const result = createRecord(body.payload);
      return json(res, result.deduplicated ? 200 : 201, {
        code: result.code,
        createdAt: result.record.createdAt,
        sha256: result.record.sha256,
        summary: result.record.summary,
        deduplicated: result.deduplicated,
      });
    }

    const match = /^\/v1\/shares\/([^/]+)$/.exec(url.pathname);
    if (req.method === "GET" && match) {
      if (!rateAllowed(readBuckets, ip, 120, 60 * 1000)) {
        return json(res, 429, { error: "Read rate limit exceeded. Try again later." });
      }
      let code;
      try {
        code = normalizeShortCode(decodeURIComponent(match[1]));
      } catch (error) {
        return json(res, 400, { error: error.message });
      }
      const record = getRecord(code);
      if (!record) return json(res, 404, { error: "Atomic Share Code not found." });
      return json(res, 200, {
        code: record.code,
        payload: record.payload,
        createdAt: record.createdAt,
        sha256: record.sha256,
        summary: record.summary,
      });
    }

    return json(res, 404, { error: "Not found." });
  } catch (error) {
    console.error("Request failed:", error);
    const status = Number(error.status) || 400;
    return json(res, status >= 400 && status < 600 ? status : 500, {
      error: status >= 500 ? "Internal server error." : error.message,
    });
  }
});

server.on("clientError", (_error, socket) => {
  socket.end("HTTP/1.1 400 Bad Request\r\n\r\n");
});

server.listen(PORT, HOST, () => {
  console.log(`Atomic Share API listening on http://${HOST}:${PORT}`);
  console.log(`Registry: ${DATA_FILE}`);
});
