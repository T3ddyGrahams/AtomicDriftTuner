"use strict";

const assert = require("assert");
const zlib = require("zlib");
const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");
const os = require("os");

function portable(payload) {
  const zipped = zlib.gzipSync(Buffer.from(JSON.stringify(payload), "utf8"));
  return "AT1-" + zipped.toString("base64url");
}

const sample = {
  schema: "atomic-share/v1",
  atomicVersion: "0.8.1-beta.1",
  createdUtc: new Date().toISOString(),
  input: {
    hardware: { id: "moza-r12", manufacturer: "MOZA", model: "R12" },
    wheel: { id: "moza-ks", manufacturer: "MOZA", model: "KS" },
    pack: { id: "vdc", name: "VDC" },
    car: { id: "test-car", packId: "vdc", displayName: "Test Car" },
    intent: { kind: 0, name: "Tandem" }
  },
  behavior: {
    frontEndBite: 0, rearGrip: 0, selfSteerSpeed: 1, transitionSpeed: 1,
    angleStability: 1, throttleSteering: 0, initiationSharpness: 0
  },
  recommendation: {
    azom: {},
    assettoCorsa: {},
    estimatedPeakWheelTorqueNm: 8.2,
    selfSteerScore: 88,
    stabilityScore: 84,
    detailScore: 79,
    notes: []
  }
};

async function main() {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "atomic-share-api-"));
  const data = path.join(temp, "shares.json");
  const port = 18787;
  const child = spawn(process.execPath, [path.join(__dirname, "index.js")], {
    env: { ...process.env, ATOMIC_SHARE_PORT: String(port), ATOMIC_SHARE_DATA: data },
    stdio: ["ignore", "pipe", "pipe"]
  });

  try {
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("API start timeout")), 5000);
      child.stdout.on("data", (data) => {
        if (String(data).includes("Atomic Share API listening")) {
          clearTimeout(timer);
          resolve();
        }
      });
      child.once("exit", (code) => reject(new Error(`API exited early: ${code}`)));
    });

    const p = portable(sample);
    assert(p.length < 2000);

    const create = await fetch(`http://127.0.0.1:${port}/v1/shares`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ payload: p })
    });
    assert.equal(create.status, 201);
    const created = await create.json();
    assert.match(created.code, /^AT-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$/);

    const duplicate = await fetch(`http://127.0.0.1:${port}/v1/shares`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ payload: p })
    });
    assert.equal(duplicate.status, 200);
    const dup = await duplicate.json();
    assert.equal(dup.code, created.code);
    assert.equal(dup.deduplicated, true);

    const read = await fetch(`http://127.0.0.1:${port}/v1/shares/${created.code}`);
    assert.equal(read.status, 200);
    const fetched = await read.json();
    assert.equal(fetched.payload, p);
    assert.equal(fetched.summary.car, "Test Car");
    assert.equal(fetched.summary.wheelbase, "R12");

    console.log(`PASS ${created.code}: create, dedupe, retrieve`);
  } finally {
    child.kill("SIGTERM");
    fs.rmSync(temp, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
