# Atomic Share Codes — Phase 2A

This adds the short-code registry service only.

Phase 1 `AT1-...` codes stay the canonical portable payload. The registry stores that payload and returns a short public identifier such as:

`AT-7K4D2P`

## Safety boundary

The API stores and returns sanitized Share Code payloads only. It has no AZOM, SimHub, Assetto Corsa, telemetry, remote-control, or wheelbase write path.

## API

- `GET /health`
- `POST /v1/shares` with `{ "payload": "AT1-..." }`
- `GET /v1/shares/AT-XXXXXX`

The service validates the `atomic-share/v1` structure before accepting a payload, deduplicates identical payloads, writes its registry atomically, limits payload/request size, and applies basic per-IP rate limiting.

## Deployment choice

For the first test, bind it only to `127.0.0.1:8787` on the Oracle server. Do **not** open port 8787 to the Internet.

After local + Discord testing succeeds, put a normal HTTPS reverse proxy in front of it. The desktop app should not publish to a public HTTP endpoint.

## Local test

From `share-api`:

```bash
npm run check
npm run selftest
```

Expected self-test result resembles:

```text
PASS AT-XXXXXX: create, dedupe, retrieve
```

