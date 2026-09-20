import assert from "node:assert/strict";
import test from "node:test";

import worker from "./worker.js";

const ORIGIN = "https://adt-preview-downloads.timjamesharr.workers.dev";
const SECRET = "unit-test-only-download-token";
const INSTALLER_KEY =
  "ADT 0.9.0-preview.8/AtomicDriftTuner-0.9.0-preview.8-setup.exe";
const PORTABLE_KEY =
  "ADT 0.9.0-preview.8/AtomicDriftTuner-0.9.0-preview.8-portable.zip";

class MockBucket {
  constructor(objects, pageSize = 2) {
    this.objects = objects;
    this.pageSize = pageSize;
    this.listCalls = [];
  }

  async list(options = {}) {
    this.listCalls.push(options);
    const start = Number(options.cursor || 0);
    const limit = Math.min(options.limit || 1000, this.pageSize);
    const end = Math.min(start + limit, this.objects.length);

    return {
      cursor: end < this.objects.length ? String(end) : undefined,
      objects: this.objects.slice(start, end).map((object) => ({
        key: object.key,
        size: object.size,
        uploaded: object.uploaded,
      })),
      truncated: end < this.objects.length,
    };
  }

  async head(key) {
    const object = this.objects.find((candidate) => candidate.key === key);
    return object ? this.metadata(object) : null;
  }

  async get(key) {
    const object = this.objects.find((candidate) => candidate.key === key);

    if (!object) {
      return null;
    }

    return {
      ...this.metadata(object),
      body: object.body,
      httpEtag: `"etag-${object.key.length}"`,
      writeHttpMetadata(headers) {
        headers.set("Content-Type", object.contentType);
      },
    };
  }

  metadata(object) {
    return {
      key: object.key,
      size: object.size,
      uploaded: object.uploaded,
    };
  }
}

function fixture() {
  const bucket = new MockBucket([
    {
      key: "ADT 0.8.9-preview.2/AtomicDriftTuner-0.8.9-preview.2-setup.exe",
      size: 58_000_000,
      uploaded: new Date("2026-09-10T13:00:00Z"),
      body: "older-installer",
      contentType: "application/octet-stream",
    },
    {
      key: INSTALLER_KEY,
      size: 61_865_984,
      uploaded: new Date("2026-09-12T15:00:00Z"),
      body: "installer-body",
      contentType: "application/vnd.microsoft.portable-executable",
    },
    {
      key: PORTABLE_KEY,
      size: 59_000_000,
      uploaded: new Date("2026-09-12T15:01:00Z"),
      body: "portable-body",
      contentType: "application/zip",
    },
    {
      key: "ADT 0.9.0-preview.8/readme.txt",
      size: 400,
      uploaded: new Date("2026-09-12T15:02:00Z"),
      body: "notes",
      contentType: "text/plain",
    },
  ]);

  return {
    bucket,
    env: {
      DOWNLOAD_TOKEN: SECRET,
      PREVIEW_BUILDS: bucket,
    },
  };
}

function adminRequest(method = "GET", { body, cookie, origin = ORIGIN } = {}) {
  const headers = new Headers();

  if (body) {
    headers.set("Content-Type", "application/x-www-form-urlencoded");
  }

  if (cookie) {
    headers.set("Cookie", cookie);
  }

  if (origin) {
    headers.set("Origin", origin);
  }

  return new Request(`${ORIGIN}/admin`, { method, headers, body });
}

function formBody(entries) {
  const body = new URLSearchParams();

  for (const [name, value] of entries) {
    body.append(name, value);
  }

  return body;
}

function sessionCookie(response) {
  return response.headers.get("Set-Cookie").split(";", 1)[0];
}

function decodeHtml(value) {
  return value
    .replace(/&amp;/g, "&")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">");
}

function textareaValue(html, id) {
  const match = html.match(
    new RegExp(`<textarea id="${id}"[^>]*>([\\s\\S]*?)<\\/textarea>`)
  );
  assert.ok(match, `Expected textarea #${id}`);
  return decodeHtml(match[1]);
}

async function login(env, token = SECRET) {
  return worker.fetch(
    adminRequest("POST", {
      body: formBody([
        ["action", "login"],
        ["token", token],
      ]),
    }),
    env
  );
}

test("ADT Preview Release Manager", async (t) => {
  await t.test("fails closed when bindings are missing", async () => {
    const response = await worker.fetch(new Request(`${ORIGIN}/admin`), {});
    assert.equal(response.status, 503);
    assert.equal(await response.text(), "Download service is not configured.");
  });

  await t.test("shows a no-store login page without listing R2", async () => {
    const { bucket, env } = fixture();
    const response = await worker.fetch(adminRequest(), env);
    const html = await response.text();

    assert.equal(response.status, 200);
    assert.match(html, /Preview Release Manager/);
    assert.match(html, /type="password"/);
    assert.doesNotMatch(html, new RegExp(SECRET));
    assert.equal(response.headers.get("Cache-Control"), "no-store");
    assert.equal(response.headers.get("X-Frame-Options"), "DENY");
    assert.equal(bucket.listCalls.length, 0);
  });

  await t.test("rejects an incorrect master token", async () => {
    const { env } = fixture();
    const response = await login(env, "incorrect-token");
    const html = await response.text();

    assert.equal(response.status, 401);
    assert.match(html, /was not accepted/);
    assert.equal(response.headers.get("Set-Cookie"), null);
  });

  await t.test("creates a secure admin session without exposing the token", async () => {
    const { env } = fixture();
    const response = await login(env);
    const setCookie = response.headers.get("Set-Cookie");

    assert.equal(response.status, 303);
    assert.equal(response.headers.get("Location"), "/admin");
    assert.match(setCookie, /^__Host-adt_preview_admin=/);
    assert.match(setCookie, /HttpOnly/);
    assert.match(setCookie, /Secure/);
    assert.match(setCookie, /SameSite=Strict/);
    assert.doesNotMatch(setCookie, new RegExp(SECRET));
  });

  await t.test("discovers paginated builds and identifies installer and portable", async () => {
    const { bucket, env } = fixture();
    const cookie = sessionCookie(await login(env));
    const response = await worker.fetch(
      adminRequest("GET", { cookie, origin: null }),
      env
    );
    const html = await response.text();

    assert.equal(response.status, 200);
    assert.ok(bucket.listCalls.length > 1, "Expected paginated R2 listing");
    assert.match(html, /ADT 0\.9\.0-preview\.8/);
    assert.match(html, /ADT 0\.8\.9-preview\.2/);
    assert.match(html, /Installer/);
    assert.match(html, /Portable ZIP/);
    assert.match(html, /Newest upload/);
    assert.match(html, /Generate Discord Post/);
    assert.doesNotMatch(html, /readme\.txt/);
    assert.doesNotMatch(html, new RegExp(SECRET));
  });

  await t.test("generates both signed links and a Discord post", async () => {
    const { env } = fixture();
    const cookie = sessionCookie(await login(env));
    const response = await worker.fetch(
      adminRequest("POST", {
        cookie,
        body: formBody([
          ["action", "generate-discord"],
          ["objectKey", INSTALLER_KEY],
          ["objectKey", PORTABLE_KEY],
          ["hours", "6"],
        ]),
      }),
      env
    );
    const html = await response.text();
    const installerUrl = textareaValue(html, "download-0");
    const portableUrl = textareaValue(html, "download-1");
    const discordPost = textareaValue(html, "discord-post");

    assert.equal(response.status, 200);
    assert.match(installerUrl, /\/download\/ADT%200\.9\.0-preview\.8\//);
    assert.match(installerUrl, /expires=\d+&sig=[A-Za-z0-9_-]+$/);
    assert.match(portableUrl, /portable\.zip/);
    assert.match(discordPost, /ADT 0\.9\.0-preview\.8 supporter preview/);
    assert.match(discordPost, /\*\*Installer:\*\*/);
    assert.match(discordPost, /\*\*Portable ZIP:\*\*/);
    assert.match(discordPost, /<t:\d+:R>/);
    assert.doesNotMatch(html, new RegExp(SECRET));

    const download = await worker.fetch(new Request(installerUrl), env);
    assert.equal(download.status, 200);
    assert.equal(await download.text(), "installer-body");
    assert.match(
      download.headers.get("Content-Disposition"),
      /AtomicDriftTuner-0\.9\.0-preview\.8-setup\.exe/
    );
    assert.equal(download.headers.get("Cache-Control"), "private, no-store");

    const head = await worker.fetch(
      new Request(installerUrl, { method: "HEAD" }),
      env
    );
    assert.equal(head.status, 200);
    assert.equal((await head.arrayBuffer()).byteLength, 0);

    const tampered = new URL(installerUrl);
    tampered.searchParams.set("sig", `${tampered.searchParams.get("sig")}x`);
    const rejected = await worker.fetch(new Request(tampered), env);
    assert.equal(rejected.status, 401);
  });

  await t.test("rejects expired and unsigned download URLs", async () => {
    const { env } = fixture();
    const encodedKey = INSTALLER_KEY.split("/").map(encodeURIComponent).join("/");
    const expired = await worker.fetch(
      new Request(`${ORIGIN}/download/${encodedKey}?expires=1&sig=present`),
      env
    );
    const unsigned = await worker.fetch(
      new Request(`${ORIGIN}/download/${encodedKey}`),
      env
    );

    assert.equal(expired.status, 403);
    assert.equal(await expired.text(), "Link expired");
    assert.equal(unsigned.status, 401);
  });

  await t.test("preserves the original token/objectKey POST flow", async () => {
    const { env } = fixture();
    const response = await worker.fetch(
      adminRequest("POST", {
        body: formBody([
          ["token", SECRET],
          ["objectKey", INSTALLER_KEY],
          ["hours", "1"],
        ]),
      }),
      env
    );
    const html = await response.text();

    assert.equal(response.status, 200);
    assert.match(html, /secure link ready/);
    assert.match(textareaValue(html, "download-0"), /expires=\d+&sig=/);
    assert.doesNotMatch(html, new RegExp(SECRET));
  });

  await t.test("blocks cross-origin admin posts and invalid selections", async () => {
    const { env } = fixture();
    const cookie = sessionCookie(await login(env));
    const crossOrigin = await worker.fetch(
      adminRequest("POST", {
        cookie,
        origin: "https://example.com",
        body: formBody([
          ["action", "generate-links"],
          ["objectKey", INSTALLER_KEY],
          ["hours", "24"],
        ]),
      }),
      env
    );
    const unsupported = await worker.fetch(
      adminRequest("POST", {
        cookie,
        body: formBody([
          ["action", "generate-links"],
          ["objectKey", "ADT 0.9.0-preview.8/readme.txt"],
          ["hours", "24"],
        ]),
      }),
      env
    );

    assert.equal(crossOrigin.status, 403);
    assert.equal(unsupported.status, 400);
    assert.match(await unsupported.text(), /Only Windows installer/);
  });

  await t.test("expires the admin cookie on sign out", async () => {
    const { env } = fixture();
    const cookie = sessionCookie(await login(env));
    const response = await worker.fetch(
      adminRequest("POST", {
        cookie,
        body: formBody([["action", "logout"]]),
      }),
      env
    );

    assert.equal(response.status, 303);
    assert.match(response.headers.get("Set-Cookie"), /Max-Age=0/);
  });
});
