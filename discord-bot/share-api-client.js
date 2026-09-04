"use strict";

const DEFAULT_BASE_URL = "http://127.0.0.1:8787";
const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "::1"]);

function normalizeBaseUrl(value) {
  const raw = String(value || DEFAULT_BASE_URL).trim();
  const url = new URL(raw);

  if (url.protocol !== "http:") {
    throw new Error("Phase 2B Share API must use local HTTP behind the Oracle host.");
  }

  if (!LOOPBACK_HOSTS.has(url.hostname)) {
    throw new Error(
      "Phase 2B Share API URL must stay on loopback (127.0.0.1/localhost/::1)."
    );
  }

  return url.toString().replace(/\/+$/, "");
}

async function readJsonResponse(response) {
  let body = null;

  try {
    body = await response.json();
  } catch {
    body = null;
  }

  if (!response.ok) {
    const message =
      body && typeof body.error === "string"
        ? body.error
        : `Atomic Share API returned HTTP ${response.status}.`;
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }

  if (!body || typeof body !== "object") {
    throw new Error("Atomic Share API returned an invalid response.");
  }

  return body;
}

function createShareApiClient(baseUrl = DEFAULT_BASE_URL) {
  const base = normalizeBaseUrl(baseUrl);

  async function request(path, options = {}) {
    const response = await fetch(`${base}${path}`, {
      ...options,
      headers: {
        Accept: "application/json",
        ...(options.headers || {}),
      },
      signal: AbortSignal.timeout(5000),
    });

    return readJsonResponse(response);
  }

  return {
    baseUrl: base,

    health() {
      return request("/health");
    },

    publish(payload) {
      return request("/v1/shares", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ payload }),
      });
    },

    get(code) {
      const value = encodeURIComponent(String(code || "").trim());
      return request(`/v1/shares/${value}`);
    },
  };
}

module.exports = {
  createShareApiClient,
};
