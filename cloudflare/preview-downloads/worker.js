// ADT preview download Worker

const encoder = new TextEncoder();

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!env.DOWNLOAD_TOKEN) {
      return new Response("Download service is not configured.", {
        status: 503,
      });
    }

    // Private link-generator page.
    if (url.pathname === "/admin") {
      return handleAdmin(request, env);
    }

    // Signed supporter downloads.
    if (url.pathname.startsWith("/download/")) {
      return handleDownload(request, env, url);
    }

    return new Response("Not found", { status: 404 });
  },
};

async function handleAdmin(request, env) {
  if (request.method === "GET") {
    return new Response(adminPage(), {
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
      },
    });
  }

  if (request.method !== "POST") {
    return new Response("Method not allowed", {
      status: 405,
      headers: { Allow: "GET, POST" },
    });
  }

  const form = await request.formData();

  const token = String(form.get("token") || "");
  const objectKey = String(form.get("objectKey") || "").trim();
  const hoursRaw = Number(form.get("hours") || 24);

  if (token !== env.DOWNLOAD_TOKEN) {
    return new Response("Unauthorized", { status: 401 });
  }

  if (!objectKey) {
    return new Response("Object key is required.", { status: 400 });
  }

  const hours = Math.min(Math.max(hoursRaw, 1), 168);
  const expires = Math.floor(Date.now() / 1000) + hours * 60 * 60;

  const signature = await sign(
    env.DOWNLOAD_TOKEN,
    objectKey,
    expires
  );

  const encodedPath = objectKey
    .split("/")
    .map(encodeURIComponent)
    .join("/");

  const origin = new URL(request.url).origin;

  const downloadUrl =
    `${origin}/download/${encodedPath}` +
    `?expires=${expires}&sig=${signature}`;

  return new Response(resultPage(downloadUrl, hours), {
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store",
    },
  });
}

async function handleDownload(request, env, url) {
  if (request.method !== "GET" && request.method !== "HEAD") {
    return new Response("Method not allowed", {
      status: 405,
      headers: { Allow: "GET, HEAD" },
    });
  }

  const expires = Number(url.searchParams.get("expires"));
  const suppliedSignature = url.searchParams.get("sig");

  if (!expires || !suppliedSignature) {
    return new Response("Unauthorized", { status: 401 });
  }

  const now = Math.floor(Date.now() / 1000);

  if (expires <= now) {
    return new Response("Link expired", { status: 403 });
  }

  const objectKey = decodeURIComponent(
    url.pathname.substring("/download/".length)
  );

  if (!objectKey) {
    return new Response("File not specified", { status: 400 });
  }

  const expectedSignature = await sign(
    env.DOWNLOAD_TOKEN,
    objectKey,
    expires
  );

  if (!safeEqual(suppliedSignature, expectedSignature)) {
    return new Response("Unauthorized", { status: 401 });
  }

  const object = await env.PREVIEW_BUILDS.get(objectKey);

  if (object === null) {
    return new Response("File not found", { status: 404 });
  }

  const headers = new Headers();

  object.writeHttpMetadata(headers);

  headers.set("etag", object.httpEtag);
  headers.set("Cache-Control", "private, no-store");

  const fileName =
    objectKey.split("/").pop() || "ADT-preview-download";

  headers.set(
    "Content-Disposition",
    `attachment; filename="${fileName.replace(/"/g, "")}"`
  );

  if (request.method === "HEAD") {
    return new Response(null, {
      status: 200,
      headers,
    });
  }

  return new Response(object.body, {
    status: 200,
    headers,
  });
}

async function sign(secret, objectKey, expires) {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    {
      name: "HMAC",
      hash: "SHA-256",
    },
    false,
    ["sign"]
  );

  const payload = `${objectKey}\n${expires}`;

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

  for (let i = 0; i < a.length; i++) {
    result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }

  return result === 0;
}

function adminPage() {
  return `<!doctype html>
<html>
<head>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ADT Preview Link Generator</title>
</head>
<body style="font-family:system-ui;max-width:700px;margin:40px auto;padding:20px;">
  <h1>ADT Preview Link Generator</h1>

  <form method="POST">
    <p>
      <label>Master token</label><br>
      <input
        type="password"
        name="token"
        required
        style="width:100%;padding:12px;"
      >
    </p>

    <p>
      <label>R2 object key</label><br>
      <input
        name="objectKey"
        required
        placeholder="ADT 0.9.0-preview.8/AtomicDriftTuner-0.9.0-preview.8-setup.exe"
        style="width:100%;padding:12px;"
      >
    </p>

    <p>
      <label>Expires after</label><br>
      <select name="hours" style="width:100%;padding:12px;">
        <option value="1">1 hour</option>
        <option value="6">6 hours</option>
        <option value="24" selected>24 hours</option>
        <option value="72">3 days</option>
        <option value="168">7 days</option>
      </select>
    </p>

    <button
      type="submit"
      style="padding:14px 20px;font-size:16px;"
    >
      Generate Download Link
    </button>
  </form>
</body>
</html>`;
}

function resultPage(downloadUrl, hours) {
  const escaped = escapeHtml(downloadUrl);

  return `<!doctype html>
<html>
<head>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ADT Preview Link</title>
</head>
<body style="font-family:system-ui;max-width:700px;margin:40px auto;padding:20px;">
  <h1>ADT Preview Link Created</h1>

  <p>This link expires in ${hours} hour(s).</p>

  <textarea
    readonly
    style="width:100%;height:160px;padding:12px;"
  >${escaped}</textarea>

  <p>
    <a href="${escaped}">Test download</a>
  </p>

  <p>
    <a href="/admin">Generate another link</a>
  </p>
</body>
</html>`;
}

function escapeHtml(value) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}