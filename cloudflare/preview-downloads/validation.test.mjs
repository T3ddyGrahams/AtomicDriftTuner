import assert from "node:assert/strict";
import test from "node:test";
import worker from "./worker.js";

const origin = "https://adt-preview-downloads.atomicdrifttuner.workers.dev";
const testCredential = "validation-fixture-only";
const installer = "ADT 0.9.0-preview.9/AtomicDriftTuner-0.9.0-preview.9-setup.exe";
const notes = "ADT 0.9.0-preview.9/readme.txt";

function fixture(count = 2) {
  const objects = Array.from({ length: count }, (_, i) => ({
    key: i === 0 ? installer : i === 1 ? notes : `notes/${i}.txt`,
    size: 20,
    uploaded: new Date("2026-09-13T12:00:00Z"),
  }));
  const listCalls = [];
  const bucket = {
    async head(key) { return objects.find(object => object.key === key) ?? null; },
    async list(options = {}) {
      listCalls.push(options);
      const start = Number(options.cursor || 0);
      const end = Math.min(start + options.limit, objects.length);
      return {
        objects: objects.slice(start, end),
        truncated: end < objects.length,
        cursor: end < objects.length ? String(end) : undefined,
      };
    },
  };
  return { env: { DOWNLOAD_TOKEN: testCredential, PREVIEW_BUILDS: bucket }, bucket, listCalls };
}

async function session(env) {
  const response = await worker.fetch(new Request(origin + "/admin", {
    method: "POST", headers: { Origin: origin },
    body: new URLSearchParams({ action: "login", token: testCredential }),
  }), env);
  assert.equal(response.status, 303);
  return response.headers.get("Set-Cookie").split(";", 1)[0];
}

async function manager(env) {
  return worker.fetch(new Request(origin + "/admin", {
    headers: { Cookie: await session(env) },
  }), env);
}

test("mixed supported and unsupported selections return an error without partial links", async () => {
  const { env } = fixture();
  const body = new URLSearchParams({ action: "generate-discord", hours: "24" });
  body.append("objectKey", installer);
  body.append("objectKey", notes);
  const response = await worker.fetch(new Request(origin + "/admin", {
    method: "POST", headers: { Origin: origin, Cookie: await session(env) }, body,
  }), env);
  assert.equal(response.status, 400);
  const html = await response.text();
  assert.match(html, /Only Windows installer/);
  assert.doesNotMatch(html, /sig=|id="discord-post"/);
});

test("a complete 5000-object scan does not claim that releases were omitted", async () => {
  const { env, listCalls } = fixture(5000);
  const response = await manager(env);
  assert.equal(response.status, 200);
  assert.doesNotMatch(await response.text(), /Only the first/);
  assert.equal(listCalls.length, 5);
});

test("a truncated scan remains bounded and warns that releases may be omitted", async () => {
  const { env, listCalls } = fixture(5001);
  const response = await manager(env);
  assert.equal(response.status, 200);
  assert.match(await response.text(), /Only the first 5,000 R2 objects were scanned/);
  assert.equal(listCalls.length, 5);
});

test("unexpected upstream errors never leak their contents to logs or the page", async (t) => {
  const { env, bucket } = fixture();
  const sentinel = "sensitive-error-fixture-only";
  bucket.list = async () => { throw new Error(sentinel); };
  const logger = t.mock.method(console, "error", () => {});
  const response = await manager(env);
  assert.equal(response.status, 500);
  assert.doesNotMatch(await response.text(), new RegExp(sentinel));
  assert.ok(logger.mock.calls.length > 0);
  assert.ok(logger.mock.calls.every(call => call.arguments.length === 1 &&
    call.arguments[0] === "ADT preview Worker request failed"));
});

test("every offered expiry generates the requested lifetime and invalid values are rejected", async () => {
  const { env } = fixture();
  const cookie = await session(env);
  for (const hours of [1, 6, 12, 24, 48, 72, 168]) {
    const before = Math.floor(Date.now() / 1000);
    const body = new URLSearchParams({ action: "generate-links", hours: String(hours), objectKey: installer });
    const response = await worker.fetch(new Request(origin + "/admin", {
      method: "POST", headers: { Origin: origin, Cookie: cookie }, body,
    }), env);
    assert.equal(response.status, 200);
    const html = await response.text();
    const expires = Number(html.match(/expires=(\d+)/)?.[1]);
    assert.ok(expires >= before + hours * 3600);
    assert.ok(expires <= Math.floor(Date.now() / 1000) + hours * 3600);
  }
  for (const hours of ["0", "169", "1.5", "bad", ""]) {
    const response = await worker.fetch(new Request(origin + "/admin", {
      method: "POST", headers: { Origin: origin, Cookie: cookie },
      body: new URLSearchParams({ action: "generate-links", hours, objectKey: installer }),
    }), env);
    assert.equal(response.status, 400);
  }
});
