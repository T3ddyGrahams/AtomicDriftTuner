// ADT supporter preview download Worker

const encoder = new TextEncoder();

const ADMIN_PATH = "/admin";
const DOWNLOAD_PREFIX = "/download/";
const ADMIN_SESSION_COOKIE = "__Host-adt_preview_admin";
const ADMIN_SESSION_SECONDS = 8 * 60 * 60;
const DEFAULT_LINK_HOURS = 24;
const MAX_LINK_HOURS = 168;
const MAX_SELECTED_FILES = 10;
const MAX_DISCOVERED_OBJECTS = 5000;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!env.DOWNLOAD_TOKEN || !env.PREVIEW_BUILDS) {
      return textResponse("Download service is not configured.", 503);
    }

    try {
      if (url.pathname === ADMIN_PATH) {
        return await handleAdmin(request, env, url);
      }

      if (url.pathname.startsWith(DOWNLOAD_PREFIX)) {
        return await handleDownload(request, env, url);
      }

      return textResponse("Not found", 404);
    } catch (error) {
      console.error("ADT preview Worker request failed", error);
      return textResponse("The preview service hit an unexpected error.", 500);
    }
  },
};

async function handleAdmin(request, env, url) {
  if (request.method === "GET") {
    const authenticated = await hasValidAdminSession(request, env.DOWNLOAD_TOKEN);

    if (!authenticated) {
      return htmlResponse(loginPage());
    }

    const discovery = await discoverPreviewReleases(env.PREVIEW_BUILDS);
    return htmlResponse(managerPage(discovery));
  }

  if (request.method !== "POST") {
    return textResponse("Method not allowed", 405, { Allow: "GET, POST" });
  }

  if (!isSameOriginRequest(request, url)) {
    return textResponse("Forbidden", 403);
  }

  const form = await request.formData();
  const action = String(form.get("action") || "");

  if (action === "login") {
    const token = String(form.get("token") || "");
    const valid = await tokensMatch(token, env.DOWNLOAD_TOKEN);

    if (!valid) {
      return htmlResponse(loginPage("That master token was not accepted."), {
        status: 401,
      });
    }

    const session = await createAdminSession(env.DOWNLOAD_TOKEN);

    return redirectToAdmin(
      `${ADMIN_SESSION_COOKIE}=${session}; Max-Age=${ADMIN_SESSION_SECONDS}; Path=/; HttpOnly; Secure; SameSite=Strict`
    );
  }

  if (action === "logout") {
    return redirectToAdmin(
      `${ADMIN_SESSION_COOKIE}=; Max-Age=0; Path=/; HttpOnly; Secure; SameSite=Strict`
    );
  }

  // Keep the original link-generator POST contract working for an already-open
  // copy of the old /admin page while the new manager rolls out.
  if (!action && form.has("token") && form.has("objectKey")) {
    const token = String(form.get("token") || "");
    const valid = await tokensMatch(token, env.DOWNLOAD_TOKEN);

    if (!valid) {
      return textResponse("Unauthorized", 401);
    }

    return generateResult(request, env, url, form, "links");
  }

  const authenticated = await hasValidAdminSession(request, env.DOWNLOAD_TOKEN);

  if (!authenticated) {
    return htmlResponse(loginPage("Your manager session expired. Sign in again."), {
      status: 401,
      headers: {
        "Set-Cookie": `${ADMIN_SESSION_COOKIE}=; Max-Age=0; Path=/; HttpOnly; Secure; SameSite=Strict`,
      },
    });
  }

  if (action === "generate-links") {
    return generateResult(request, env, url, form, "links");
  }

  if (action === "generate-discord") {
    return generateResult(request, env, url, form, "discord");
  }

  return adminErrorPage("Unknown manager action.", 400);
}

async function generateResult(request, env, url, form, mode) {
  const objectKeys = [
    ...new Set(
      form
        .getAll("objectKey")
        .filter((value) => typeof value === "string")
        .map((value) => value.trim())
        .filter(Boolean)
    ),
  ];

  if (objectKeys.length === 0) {
    return adminErrorPage("Select at least one preview file.", 400);
  }

  if (objectKeys.length > MAX_SELECTED_FILES) {
    return adminErrorPage(
      `Select no more than ${MAX_SELECTED_FILES} files at a time.`,
      400
    );
  }

  const hours = parseExpirationHours(form.get("hours"));

  if (hours === null) {
    return adminErrorPage(
      `Expiration must be a whole number from 1 to ${MAX_LINK_HOURS} hours.`,
      400
    );
  }

  const storedObjects = await Promise.all(
    objectKeys.map((objectKey) => env.PREVIEW_BUILDS.head(objectKey))
  );

  const missingIndex = storedObjects.findIndex((object) => object === null);

  if (missingIndex !== -1) {
    return adminErrorPage(
      `Preview file not found: ${objectKeys[missingIndex]}`,
      404
    );
  }

  const assets = objectKeys
    .map((objectKey, index) =>
      buildAsset({
        key: objectKey,
        size: storedObjects[index].size,
        uploaded: storedObjects[index].uploaded,
      })
    )
    .filter(Boolean)
    .sort(compareAssets);

  if (assets.length === 0) {
    return adminErrorPage(
      "Only Windows installer (.exe or .msi) and portable (.zip) builds can be shared.",
      400
    );
  }

  const expires = Math.floor(Date.now() / 1000) + hours * 60 * 60;
  const origin = url.origin;
  const links = [];

  for (const asset of assets) {
    const signature = await signDownload(
      env.DOWNLOAD_TOKEN,
      asset.key,
      expires
    );
    const encodedPath = asset.key
      .split("/")
      .map(encodeURIComponent)
      .join("/");

    links.push({
      ...asset,
      url:
        `${origin}${DOWNLOAD_PREFIX}${encodedPath}` +
        `?expires=${expires}&sig=${signature}`,
    });
  }

  const release = releaseForAssets(assets);
  const discordPost =
    mode === "discord"
      ? buildDiscordPost(release.label, links, expires)
      : null;

  return htmlResponse(
    resultPage({
      discordPost,
      expires,
      hours,
      links,
      releaseLabel: release.label,
    })
  );
}

async function handleDownload(request, env, url) {
  if (request.method !== "GET" && request.method !== "HEAD") {
    return textResponse("Method not allowed", 405, { Allow: "GET, HEAD" });
  }

  const expires = Number(url.searchParams.get("expires"));
  const suppliedSignature = url.searchParams.get("sig");

  if (!Number.isSafeInteger(expires) || !suppliedSignature) {
    return textResponse("Unauthorized", 401);
  }

  const now = Math.floor(Date.now() / 1000);

  if (expires <= now) {
    return textResponse("Link expired", 403);
  }

  let objectKey;

  try {
    objectKey = decodeURIComponent(url.pathname.substring(DOWNLOAD_PREFIX.length));
  } catch {
    return textResponse("File not specified", 400);
  }

  if (!objectKey) {
    return textResponse("File not specified", 400);
  }

  const expectedSignature = await signDownload(
    env.DOWNLOAD_TOKEN,
    objectKey,
    expires
  );

  if (!safeEqual(suppliedSignature, expectedSignature)) {
    return textResponse("Unauthorized", 401);
  }

  const object = await env.PREVIEW_BUILDS.get(objectKey);

  if (object === null) {
    return textResponse("File not found", 404);
  }

  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set("etag", object.httpEtag);
  headers.set("Content-Length", String(object.size));
  headers.set("Cache-Control", "private, no-store");
  headers.set("X-Content-Type-Options", "nosniff");

  const fileName = objectKey.split("/").pop() || "ADT-preview-download";
  const safeFileName = fileName.replace(/[\r\n"]/g, "");

  headers.set(
    "Content-Disposition",
    `attachment; filename="${safeFileName}"; filename*=UTF-8''${encodeURIComponent(safeFileName)}`
  );

  if (request.method === "HEAD") {
    return new Response(null, { status: 200, headers });
  }

  return new Response(object.body, { status: 200, headers });
}

async function discoverPreviewReleases(bucket) {
  const objects = [];
  let cursor;
  let incomplete = false;

  while (objects.length < MAX_DISCOVERED_OBJECTS) {
    const options = { limit: Math.min(1000, MAX_DISCOVERED_OBJECTS - objects.length) };

    if (cursor) {
      options.cursor = cursor;
    }

    const page = await bucket.list(options);
    objects.push(...page.objects);

    if (!page.truncated) {
      break;
    }

    if (!page.cursor || page.cursor === cursor) {
      incomplete = true;
      break;
    }

    cursor = page.cursor;
  }

  if (objects.length >= MAX_DISCOVERED_OBJECTS) {
    incomplete = true;
  }

  const assets = objects.map(buildAsset).filter(Boolean);
  const releasesById = new Map();

  for (const asset of assets) {
    const release = releaseForKey(asset.key);

    if (!releasesById.has(release.id)) {
      releasesById.set(release.id, {
        ...release,
        assets: [],
        newestUpload: 0,
      });
    }

    const group = releasesById.get(release.id);
    group.assets.push(asset);
    group.newestUpload = Math.max(group.newestUpload, asset.uploadedAt);
  }

  const releases = [...releasesById.values()];

  for (const release of releases) {
    release.assets.sort(compareAssets);
  }

  releases.sort((a, b) => {
    if (a.newestUpload !== b.newestUpload) {
      return b.newestUpload - a.newestUpload;
    }

    return b.label.localeCompare(a.label, undefined, {
      numeric: true,
      sensitivity: "base",
    });
  });

  return {
    incomplete,
    releaseCount: releases.length,
    releases,
    scannedCount: objects.length,
    supportedCount: assets.length,
  };
}

function buildAsset(object) {
  const key = String(object.key || "");
  const fileName = key.split("/").pop() || "";
  const kind = identifyArtifact(fileName);

  if (!kind) {
    return null;
  }

  const uploadedAt = object.uploaded
    ? new Date(object.uploaded).getTime() || 0
    : 0;

  return {
    fileName,
    key,
    kind,
    kindLabel: kind === "installer" ? "Installer" : "Portable ZIP",
    size: Number(object.size) || 0,
    uploadedAt,
  };
}

function identifyArtifact(fileName) {
  const lower = fileName.toLowerCase();

  if (lower.endsWith(".exe") || lower.endsWith(".msi")) {
    return "installer";
  }

  if (lower.endsWith(".zip")) {
    return "portable";
  }

  return null;
}

function releaseForKey(key) {
  const parts = key.split("/");
  const fileName = parts.pop() || key;
  const directory = parts.join("/");
  const directoryLabel = parts.at(-1) || "";
  const versionMatch = `${directoryLabel} ${fileName}`.match(
    /v?(\d+\.\d+\.\d+(?:-(?:preview|beta|alpha|rc)(?:[.-]?\d+)?)?)/i
  );
  const version = versionMatch ? versionMatch[1] : "";
  const label = directoryLabel
    ? directoryLabel
    : version
      ? `ADT ${version}`
      : "Ungrouped ADT previews";

  return {
    id: directory || version || "__ungrouped__",
    label,
    version,
  };
}

function releaseForAssets(assets) {
  const releases = new Map();

  for (const asset of assets) {
    const release = releaseForKey(asset.key);
    releases.set(release.id, release);
  }

  if (releases.size === 1) {
    return [...releases.values()][0];
  }

  return {
    id: "__selected__",
    label: "ADT supporter previews",
    version: "",
  };
}

function compareAssets(a, b) {
  const kindOrder = { installer: 0, portable: 1 };
  const order = kindOrder[a.kind] - kindOrder[b.kind];

  if (order !== 0) {
    return order;
  }

  return a.fileName.localeCompare(b.fileName, undefined, {
    numeric: true,
    sensitivity: "base",
  });
}

function parseExpirationHours(value) {
  const hours = Number(value ?? DEFAULT_LINK_HOURS);

  if (
    !Number.isSafeInteger(hours) ||
    hours < 1 ||
    hours > MAX_LINK_HOURS
  ) {
    return null;
  }

  return hours;
}

async function createAdminSession(secret) {
  const expires = Math.floor(Date.now() / 1000) + ADMIN_SESSION_SECONDS;
  const signature = await hmac(secret, `admin-session\n${expires}`);
  return `${expires}.${signature}`;
}

async function hasValidAdminSession(request, secret) {
  const session = readCookie(request.headers.get("Cookie"), ADMIN_SESSION_COOKIE);

  if (!session) {
    return false;
  }

  const separator = session.indexOf(".");

  if (separator === -1) {
    return false;
  }

  const expiresText = session.substring(0, separator);
  const suppliedSignature = session.substring(separator + 1);
  const expires = Number(expiresText);
  const now = Math.floor(Date.now() / 1000);

  if (
    !Number.isSafeInteger(expires) ||
    expires <= now ||
    expires > now + ADMIN_SESSION_SECONDS + 60 ||
    !suppliedSignature
  ) {
    return false;
  }

  const expectedSignature = await hmac(
    secret,
    `admin-session\n${expires}`
  );

  return safeEqual(suppliedSignature, expectedSignature);
}

function readCookie(cookieHeader, name) {
  if (!cookieHeader) {
    return null;
  }

  for (const cookie of cookieHeader.split(";")) {
    const separator = cookie.indexOf("=");

    if (separator === -1) {
      continue;
    }

    const cookieName = cookie.substring(0, separator).trim();

    if (cookieName === name) {
      return cookie.substring(separator + 1).trim();
    }
  }

  return null;
}

function isSameOriginRequest(request, url) {
  const origin = request.headers.get("Origin");

  if (origin && origin !== url.origin) {
    return false;
  }

  const referer = request.headers.get("Referer");

  if (!origin && referer) {
    try {
      return new URL(referer).origin === url.origin;
    } catch {
      return false;
    }
  }

  return true;
}

async function tokensMatch(supplied, expected) {
  const [suppliedHash, expectedHash] = await Promise.all([
    crypto.subtle.digest("SHA-256", encoder.encode(supplied)),
    crypto.subtle.digest("SHA-256", encoder.encode(expected)),
  ]);

  return safeEqualBytes(
    new Uint8Array(suppliedHash),
    new Uint8Array(expectedHash)
  );
}

async function signDownload(secret, objectKey, expires) {
  return hmac(secret, `${objectKey}\n${expires}`);
}

async function hmac(secret, payload) {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = await crypto.subtle.sign(
    "HMAC",
    key,
    encoder.encode(payload)
  );

  return base64Url(signature);
}

function base64Url(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";

  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

function safeEqual(a, b) {
  if (a.length !== b.length) {
    return false;
  }

  let result = 0;

  for (let index = 0; index < a.length; index += 1) {
    result |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }

  return result === 0;
}

function safeEqualBytes(a, b) {
  if (a.length !== b.length) {
    return false;
  }

  let result = 0;

  for (let index = 0; index < a.length; index += 1) {
    result |= a[index] ^ b[index];
  }

  return result === 0;
}

function loginPage(errorMessage = "") {
  const alert = errorMessage
    ? `<div class="alert" role="alert">${escapeHtml(errorMessage)}</div>`
    : "";

  return pageShell(
    "ADT Preview Release Manager",
    `<main class="login-shell">
      <section class="login-card">
        <div class="brand-mark" aria-hidden="true">ADT</div>
        <p class="eyebrow">Supporter previews</p>
        <h1>Preview Release Manager</h1>
        <p class="lede">Sign in with the existing master token to discover private R2 builds and create expiring supporter links.</p>
        ${alert}
        <form method="POST" action="/admin" class="stack">
          <input type="hidden" name="action" value="login">
          <label for="token">Master token</label>
          <input
            id="token"
            type="password"
            name="token"
            autocomplete="current-password"
            autofocus
            required
          >
          <button class="button primary" type="submit">Open Release Manager</button>
        </form>
        <p class="security-note">The token is checked by the Worker and is never placed in a generated link or saved in this page.</p>
      </section>
    </main>`
  );
}

function managerPage(discovery) {
  const summary = `${discovery.releaseCount} release${discovery.releaseCount === 1 ? "" : "s"} · ${discovery.supportedCount} build${discovery.supportedCount === 1 ? "" : "s"}`;
  const warning = discovery.incomplete
    ? `<div class="alert">Only the first ${MAX_DISCOVERED_OBJECTS.toLocaleString()} R2 objects were scanned.</div>`
    : "";
  const cards = discovery.releases.length
    ? discovery.releases
        .map((release, index) => releaseCard(release, index === 0))
        .join("")
    : `<section class="empty-state">
        <h2>No preview packages found</h2>
        <p>The private bucket was scanned, but it does not currently contain a Windows installer (.exe or .msi) or portable build (.zip).</p>
      </section>`;

  return pageShell(
    "ADT Preview Release Manager",
    `<header class="topbar">
      <div class="topbar-inner">
        <div class="brand-lockup">
          <span class="brand-mark small" aria-hidden="true">ADT</span>
          <div>
            <p class="eyebrow">Supporter previews</p>
            <h1>Preview Release Manager</h1>
          </div>
        </div>
        <form method="POST" action="/admin">
          <input type="hidden" name="action" value="logout">
          <button class="button ghost" type="submit">Sign out</button>
        </form>
      </div>
    </header>
    <main class="content-shell">
      <section class="manager-intro">
        <div>
          <p class="eyebrow">Private R2 bucket</p>
          <h2>Detected preview releases</h2>
          <p class="lede compact">Choose the installer, portable ZIP, or both. Every generated URL uses the existing HMAC signing flow and stops working at the selected expiration.</p>
        </div>
        <div class="summary-block">
          <span class="summary-pill">${escapeHtml(summary)}</span>
          <a class="button ghost" href="/admin">Refresh builds</a>
        </div>
      </section>
      ${warning}
      <div class="release-list">${cards}</div>
      <p class="scan-note">Scanned ${discovery.scannedCount.toLocaleString()} R2 object${discovery.scannedCount === 1 ? "" : "s"}. Only .exe, .msi, and .zip packages appear here.</p>
    </main>`
  );
}

function releaseCard(release, latest) {
  const assetRows = release.assets
    .map((asset) => {
      const uploaded = asset.uploadedAt
        ? new Date(asset.uploadedAt).toLocaleString("en-US", {
            dateStyle: "medium",
            timeStyle: "short",
            timeZone: "UTC",
          }) + " UTC"
        : "Upload time unavailable";

      return `<label class="asset-row">
        <input type="checkbox" name="objectKey" value="${escapeHtml(asset.key)}" checked>
        <span class="asset-copy">
          <span class="asset-heading">
            <span class="type-badge ${asset.kind}">${escapeHtml(asset.kindLabel)}</span>
            <strong>${escapeHtml(asset.fileName)}</strong>
          </span>
          <span class="asset-meta">${escapeHtml(formatBytes(asset.size))} · ${escapeHtml(uploaded)}</span>
          <span class="object-key">${escapeHtml(asset.key)}</span>
        </span>
      </label>`;
    })
    .join("");

  return `<form method="POST" action="/admin" class="release-card">
    <div class="release-heading">
      <div>
        <div class="release-title-line">
          <h3>${escapeHtml(release.label)}</h3>
          ${latest ? '<span class="latest-badge">Newest upload</span>' : ""}
        </div>
        <p>${release.assets.length} downloadable package${release.assets.length === 1 ? "" : "s"}</p>
      </div>
    </div>
    <fieldset>
      <legend class="sr-only">Files for ${escapeHtml(release.label)}</legend>
      <div class="asset-list">${assetRows}</div>
    </fieldset>
    <div class="release-actions">
      <label class="expiration-control">
        <span>Links expire after</span>
        <select name="hours">
          ${expirationOptions()}
        </select>
      </label>
      <div class="button-row">
        <button class="button secondary" type="submit" name="action" value="generate-links">Generate Download Links</button>
        <button class="button primary" type="submit" name="action" value="generate-discord">Generate Discord Post</button>
      </div>
    </div>
  </form>`;
}

function expirationOptions() {
  const options = [
    [1, "1 hour"],
    [6, "6 hours"],
    [12, "12 hours"],
    [24, "24 hours"],
    [48, "2 days"],
    [72, "3 days"],
    [168, "7 days"],
  ];

  return options
    .map(
      ([value, label]) =>
        `<option value="${value}"${value === DEFAULT_LINK_HOURS ? " selected" : ""}>${label}</option>`
    )
    .join("");
}

function resultPage({ discordPost, expires, hours, links, releaseLabel }) {
  const linkCards = links
    .map(
      (link, index) => `<article class="link-card">
        <div class="asset-heading">
          <span class="type-badge ${link.kind}">${escapeHtml(link.kindLabel)}</span>
          <strong>${escapeHtml(link.fileName)}</strong>
        </div>
        <textarea id="download-${index}" class="output-field short" readonly>${escapeHtml(link.url)}</textarea>
        <div class="button-row left">
          <button class="button secondary" type="button" data-copy-target="download-${index}">Copy link</button>
          <a class="button ghost" href="${escapeHtml(link.url)}">Test download</a>
        </div>
      </article>`
    )
    .join("");
  const discordSection = discordPost
    ? `<section class="discord-card">
        <p class="eyebrow">Ready for Discord</p>
        <h2>Supporter post</h2>
        <textarea id="discord-post" class="output-field discord-output" readonly>${escapeHtml(discordPost)}</textarea>
        <button class="button primary" type="button" data-copy-target="discord-post">Copy Discord Post</button>
      </section>`
    : "";
  const expiryDate = new Date(expires * 1000).toLocaleString("en-US", {
    dateStyle: "full",
    timeStyle: "long",
    timeZone: "UTC",
  });

  return pageShell(
    "ADT Preview Links",
    `<header class="topbar">
      <div class="topbar-inner">
        <div class="brand-lockup">
          <span class="brand-mark small" aria-hidden="true">ADT</span>
          <div>
            <p class="eyebrow">Supporter previews</p>
            <h1>Preview links created</h1>
          </div>
        </div>
        <a class="button ghost" href="/admin">Back to releases</a>
      </div>
    </header>
    <main class="content-shell result-shell">
      <section class="result-intro">
        <span class="success-dot" aria-hidden="true"></span>
        <div>
          <p class="eyebrow">${escapeHtml(releaseLabel)}</p>
          <h2>${links.length} secure link${links.length === 1 ? "" : "s"} ready</h2>
          <p>Expires in ${hours} hour${hours === 1 ? "" : "s"} · ${escapeHtml(expiryDate)}</p>
        </div>
      </section>
      <section class="generated-links">${linkCards}</section>
      ${discordSection}
      <div class="footer-actions">
        <a class="button secondary" href="/admin">Generate another release</a>
      </div>
    </main>`,
    copyScript()
  );
}

function adminErrorPage(message, status) {
  return htmlResponse(
    pageShell(
      "ADT Preview Manager Error",
      `<main class="login-shell">
        <section class="login-card">
          <div class="brand-mark" aria-hidden="true">ADT</div>
          <p class="eyebrow">Preview Release Manager</p>
          <h1>Could not create links</h1>
          <div class="alert" role="alert">${escapeHtml(message)}</div>
          <a class="button primary" href="/admin">Back to releases</a>
        </section>
      </main>`
    ),
    { status }
  );
}

function buildDiscordPost(releaseLabel, links, expires) {
  const heading = /^adt\b/i.test(releaseLabel)
    ? releaseLabel
    : `ADT ${releaseLabel}`;
  const lines = [
    `**${heading} supporter preview**`,
    "",
    "A new ADT preview build is ready to test.",
    "",
  ];

  for (const link of links) {
    lines.push(`**${link.kindLabel}:** ${link.url}`);
  }

  lines.push(
    "",
    `These links expire <t:${expires}:R>. Please keep them in the supporter channels and send me anything you notice while testing.`
  );

  return lines.join("\n");
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return "Size unavailable";
  }

  const units = ["B", "KB", "MB", "GB"];
  const unitIndex = Math.min(
    Math.floor(Math.log(bytes) / Math.log(1024)),
    units.length - 1
  );
  const value = bytes / 1024 ** unitIndex;
  const digits = unitIndex === 0 || value >= 10 ? 0 : 1;

  return `${value.toFixed(digits)} ${units[unitIndex]}`;
}

function redirectToAdmin(cookie) {
  return new Response(null, {
    status: 303,
    headers: {
      "Cache-Control": "no-store",
      Location: ADMIN_PATH,
      "Set-Cookie": cookie,
    },
  });
}

function textResponse(body, status = 200, extraHeaders = {}) {
  const headers = new Headers(extraHeaders);
  headers.set("Content-Type", "text/plain; charset=utf-8");
  headers.set("Cache-Control", "no-store");
  headers.set("X-Content-Type-Options", "nosniff");

  return new Response(body, { status, headers });
}

function htmlResponse(body, init = {}) {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "text/html; charset=utf-8");
  headers.set("Cache-Control", "no-store");
  headers.set("X-Content-Type-Options", "nosniff");
  headers.set("X-Frame-Options", "DENY");
  // Preserve the origin on same-origin form POSTs so CSRF checks accept them.
  headers.set("Referrer-Policy", "same-origin");
  headers.set(
    "Permissions-Policy",
    "camera=(), microphone=(), geolocation=(), payment=()"
  );
  headers.set(
    "Content-Security-Policy",
    "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"
  );

  return new Response(body, { ...init, headers });
}

function pageShell(title, body, script = "") {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="color-scheme" content="dark">
  <title>${escapeHtml(title)}</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #080b10;
      --panel: #11161e;
      --panel-soft: #171e28;
      --line: #273241;
      --text: #f4f7fb;
      --muted: #9aa8b9;
      --accent: #ff5b36;
      --accent-strong: #ff724f;
      --accent-soft: rgba(255, 91, 54, .13);
      --blue: #63a8ff;
      --blue-soft: rgba(99, 168, 255, .13);
      --green: #58d68d;
      --danger: #ff8d8d;
      --danger-soft: rgba(255, 91, 91, .12);
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    }

    * { box-sizing: border-box; }
    body { margin: 0; min-height: 100vh; background: radial-gradient(circle at top, #161c25 0, var(--bg) 38rem); color: var(--text); }
    button, input, select, textarea { font: inherit; }
    a { color: inherit; }
    h1, h2, h3, p { margin-top: 0; }
    h1 { margin-bottom: .25rem; font-size: clamp(1.35rem, 4vw, 1.85rem); letter-spacing: -.025em; }
    h2 { margin-bottom: .4rem; font-size: clamp(1.45rem, 4vw, 2rem); letter-spacing: -.025em; }
    h3 { margin: 0; font-size: 1.18rem; }
    fieldset { margin: 0; padding: 0; border: 0; }

    .topbar { border-bottom: 1px solid var(--line); background: rgba(8, 11, 16, .88); backdrop-filter: blur(16px); }
    .topbar-inner, .content-shell { width: min(1040px, calc(100% - 32px)); margin: 0 auto; }
    .topbar-inner { min-height: 84px; display: flex; align-items: center; justify-content: space-between; gap: 20px; }
    .brand-lockup { display: flex; align-items: center; gap: 14px; }
    .brand-mark { width: 64px; height: 64px; display: grid; place-items: center; border: 1px solid rgba(255, 114, 79, .45); border-radius: 18px; background: linear-gradient(145deg, var(--accent-strong), #bf2b16); color: white; font-weight: 900; letter-spacing: -.06em; box-shadow: 0 18px 42px rgba(255, 75, 40, .24); }
    .brand-mark.small { width: 48px; height: 48px; border-radius: 14px; font-size: .9rem; }
    .eyebrow { margin-bottom: .35rem; color: var(--accent-strong); font-size: .75rem; font-weight: 800; letter-spacing: .14em; text-transform: uppercase; }
    .lede { color: var(--muted); line-height: 1.65; }
    .lede.compact { max-width: 650px; margin-bottom: 0; }

    .login-shell { min-height: 100vh; display: grid; place-items: center; padding: 28px 16px; }
    .login-card { width: min(480px, 100%); padding: clamp(24px, 6vw, 42px); border: 1px solid var(--line); border-radius: 24px; background: rgba(17, 22, 30, .96); box-shadow: 0 30px 90px rgba(0, 0, 0, .42); }
    .login-card .brand-mark { margin-bottom: 26px; }
    .stack { display: grid; gap: 10px; margin-top: 26px; }
    .stack label, .expiration-control span { color: #dce4ee; font-size: .9rem; font-weight: 700; }
    input[type="password"], select, .output-field { width: 100%; border: 1px solid #344255; border-radius: 12px; background: #0b1017; color: var(--text); outline: none; }
    input[type="password"], select { min-height: 48px; padding: 0 14px; }
    input:focus, select:focus, textarea:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-soft); }
    .security-note, .scan-note { margin: 18px 0 0; color: var(--muted); font-size: .82rem; line-height: 1.55; }

    .content-shell { padding: 42px 0 64px; }
    .manager-intro { display: flex; justify-content: space-between; align-items: end; gap: 24px; margin-bottom: 26px; }
    .summary-block { display: flex; align-items: center; justify-content: flex-end; gap: 10px; flex-wrap: wrap; }
    .summary-pill, .latest-badge { display: inline-flex; align-items: center; border-radius: 999px; white-space: nowrap; }
    .summary-pill { min-height: 40px; padding: 0 14px; border: 1px solid var(--line); background: var(--panel); color: var(--muted); font-size: .86rem; }
    .release-list { display: grid; gap: 20px; }
    .release-card, .link-card, .discord-card, .empty-state, .result-intro { border: 1px solid var(--line); border-radius: 20px; background: linear-gradient(180deg, rgba(23, 30, 40, .96), rgba(15, 20, 27, .96)); box-shadow: 0 16px 44px rgba(0, 0, 0, .18); }
    .release-card { overflow: hidden; }
    .release-heading { padding: 22px 24px 18px; border-bottom: 1px solid var(--line); }
    .release-heading p { margin: 6px 0 0; color: var(--muted); font-size: .88rem; }
    .release-title-line { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .latest-badge { padding: 5px 9px; background: var(--accent-soft); color: #ff9b82; font-size: .69rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }
    .asset-list { display: grid; }
    .asset-row { display: flex; align-items: flex-start; gap: 14px; padding: 18px 24px; border-bottom: 1px solid var(--line); cursor: pointer; transition: background .15s ease; }
    .asset-row:hover { background: rgba(255, 255, 255, .025); }
    .asset-row input { width: 20px; height: 20px; margin: 3px 0 0; accent-color: var(--accent); }
    .asset-copy { min-width: 0; display: grid; gap: 7px; }
    .asset-heading { display: flex; align-items: center; gap: 9px; flex-wrap: wrap; }
    .asset-heading strong { overflow-wrap: anywhere; }
    .type-badge { display: inline-flex; padding: 4px 8px; border-radius: 7px; font-size: .68rem; font-weight: 850; letter-spacing: .07em; text-transform: uppercase; }
    .type-badge.installer { background: var(--accent-soft); color: #ff9b82; }
    .type-badge.portable { background: var(--blue-soft); color: #9ecaff; }
    .asset-meta, .object-key { color: var(--muted); font-size: .8rem; }
    .object-key { overflow-wrap: anywhere; opacity: .72; }
    .release-actions { display: flex; align-items: end; justify-content: space-between; gap: 18px; padding: 20px 24px 24px; }
    .expiration-control { width: min(220px, 100%); display: grid; gap: 7px; }
    .button-row { display: flex; justify-content: flex-end; gap: 10px; flex-wrap: wrap; }
    .button-row.left { justify-content: flex-start; }
    .button { min-height: 42px; display: inline-flex; align-items: center; justify-content: center; padding: 0 16px; border: 1px solid transparent; border-radius: 11px; color: var(--text); font-weight: 800; text-decoration: none; cursor: pointer; transition: transform .12s ease, border-color .12s ease, background .12s ease; }
    .button:hover { transform: translateY(-1px); }
    .button.primary { background: linear-gradient(135deg, var(--accent-strong), #dc3d20); box-shadow: 0 10px 24px rgba(255, 79, 44, .2); }
    .button.secondary { border-color: #45546a; background: #202a37; }
    .button.ghost { border-color: var(--line); background: transparent; color: #c4ceda; }
    .alert { margin: 20px 0; padding: 13px 15px; border: 1px solid rgba(255, 122, 122, .3); border-radius: 11px; background: var(--danger-soft); color: var(--danger); line-height: 1.5; }
    .empty-state { padding: 38px; text-align: center; }
    .empty-state p { margin-bottom: 0; color: var(--muted); line-height: 1.6; }

    .result-shell { width: min(840px, calc(100% - 32px)); }
    .result-intro { display: flex; align-items: flex-start; gap: 15px; padding: 23px; margin-bottom: 20px; }
    .result-intro h2 { margin-bottom: 5px; }
    .result-intro p:last-child { margin-bottom: 0; color: var(--muted); }
    .success-dot { flex: 0 0 auto; width: 12px; height: 12px; margin-top: 7px; border-radius: 999px; background: var(--green); box-shadow: 0 0 0 6px rgba(88, 214, 141, .12); }
    .generated-links { display: grid; gap: 14px; }
    .link-card, .discord-card { padding: 22px; }
    .output-field { min-height: 100px; margin: 15px 0 12px; padding: 13px; resize: vertical; line-height: 1.45; }
    .output-field.short { min-height: 92px; }
    .discord-card { margin-top: 20px; border-color: rgba(99, 168, 255, .35); }
    .discord-output { min-height: 260px; }
    .footer-actions { margin-top: 22px; }
    .copy-success { border-color: rgba(88, 214, 141, .5) !important; color: #8ceab3 !important; }
    .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }

    @media (max-width: 720px) {
      .topbar-inner { min-height: 76px; }
      .topbar .eyebrow { display: none; }
      .manager-intro, .release-actions { align-items: stretch; flex-direction: column; }
      .summary-block, .button-row { justify-content: stretch; }
      .summary-pill, .summary-block .button, .button-row .button, .expiration-control { width: 100%; }
      .release-heading, .asset-row, .release-actions { padding-left: 18px; padding-right: 18px; }
      .object-key { display: none; }
    }
  </style>
</head>
<body>
  ${body}
  ${script}
</body>
</html>`;
}

function copyScript() {
  return `<script>
    document.addEventListener('click', function (event) {
      var button = event.target.closest('[data-copy-target]');
      if (!button) return;
      var field = document.getElementById(button.getAttribute('data-copy-target'));
      if (!field) return;

      function showCopied() {
        var original = button.textContent;
        button.textContent = 'Copied';
        button.classList.add('copy-success');
        setTimeout(function () {
          button.textContent = original;
          button.classList.remove('copy-success');
        }, 1600);
      }

      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(field.value).then(showCopied);
        return;
      }

      field.focus();
      field.select();
      document.execCommand('copy');
      showCopied();
    });
  </script>`;
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}
