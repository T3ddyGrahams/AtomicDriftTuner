"use strict";

const zlib = require("zlib");
const { EmbedBuilder } = require("discord.js");

const PORTABLE_PREFIX = "AT1-";
const MAX_PORTABLE_CHARS = 2000;
const MAX_COMPRESSED_BYTES = 32 * 1024;
const MAX_JSON_BYTES = 96 * 1024;

function decodeBase64Url(value) {
  const base64 = String(value || "").replace(/-/g, "+").replace(/_/g, "/");
  const padding = (4 - (base64.length % 4)) % 4;
  return Buffer.from(base64 + "=".repeat(padding), "base64");
}

function decodePortable(payload) {
  const compact = String(payload || "").replace(/\s+/g, "");

  if (!compact.startsWith(PORTABLE_PREFIX)) {
    throw new Error("Stored Atomic Share payload is not an AT1 code.");
  }

  if (compact.length > MAX_PORTABLE_CHARS) {
    throw new Error("Stored Atomic Share payload exceeds the AT1 size limit.");
  }

  const compressed = decodeBase64Url(compact.slice(PORTABLE_PREFIX.length));
  if (!compressed.length || compressed.length > MAX_COMPRESSED_BYTES) {
    throw new Error("Stored Atomic Share payload has an invalid compressed size.");
  }

  let raw;
  try {
    raw = zlib.gunzipSync(compressed, { maxOutputLength: MAX_JSON_BYTES });
  } catch {
    throw new Error("Stored Atomic Share payload could not be decompressed.");
  }

  if (!raw.length || raw.length > MAX_JSON_BYTES) {
    throw new Error("Stored Atomic Share payload has an invalid decoded size.");
  }

  let parsed;
  try {
    parsed = JSON.parse(raw.toString("utf8"));
  } catch {
    throw new Error("Stored Atomic Share payload contains invalid JSON.");
  }

  if (parsed?.schema !== "atomic-share/v1") {
    throw new Error("Unsupported Atomic Share schema.");
  }

  return parsed;
}

function text(value, fallback = "Unknown") {
  const result = String(value ?? "").trim();
  return result || fallback;
}

function number(value, fallback = 0) {
  const result = Number(value);
  return Number.isFinite(result) ? result : fallback;
}

function signed(value) {
  const n = Math.trunc(number(value));
  return n > 0 ? `+${n}` : String(n);
}

function clampField(value) {
  const result = String(value || "");
  return result.length <= 1024 ? result : `${result.slice(0, 1021)}...`;
}

function buildShareCreatedEmbed(result) {
  const summary = result?.summary || {};

  return new EmbedBuilder()
    .setTitle(`⚛️ ${text(result?.code, "Atomic Share Code")}`)
    .setDescription(
      result?.deduplicated
        ? "That tune was already in the Atomic Share registry, so the existing short code was reused."
        : "Short Atomic Share Code created."
    )
    .addFields(
      {
        name: "Car / Pack",
        value: `${text(summary.car)}\n${text(summary.pack)}`,
        inline: true,
      },
      {
        name: "Hardware",
        value: `${text(summary.wheelbase)}\n${text(summary.wheel)}`,
        inline: true,
      },
      {
        name: "Drift Target",
        value: text(summary.driftTarget),
        inline: true,
      },
      {
        name: "Scores",
        value:
          `Self-Steer **${number(summary.selfSteerScore)}** • ` +
          `Stability **${number(summary.stabilityScore)}** • ` +
          `Detail **${number(summary.detailScore)}**`,
        inline: false,
      }
    )
    .setFooter({
      text: "Copy the AT-XXXXXX code and use /atomic tune to view it.",
    });
}

function buildTuneEmbed(record) {
  const payload = decodePortable(record?.payload);
  const input = payload.input || {};
  const hw = input.hardware || {};
  const wheel = input.wheel || {};
  const pack = input.pack || {};
  const car = input.car || {};
  const intent = input.intent || {};
  const behavior = payload.behavior || {};
  const recommendation = payload.recommendation || {};
  const azom = recommendation.azom || {};
  const ac = recommendation.assettoCorsa || {};
  const notes = Array.isArray(recommendation.notes)
    ? recommendation.notes.filter(Boolean).slice(0, 4)
    : [];

  const hardwareText = [
    `${text(hw.manufacturer, "")} ${text(hw.model)}`.trim(),
    `${text(wheel.manufacturer, "")} ${text(wheel.model)}`.trim(),
    `${number(hw.peakTorqueNm)} Nm base • ${number(wheel.diameterMm)} mm wheel`,
  ].join("\n");

  const scoresText =
    `Self-Steer **${number(recommendation.selfSteerScore)}** • ` +
    `Stability **${number(recommendation.stabilityScore)}** • ` +
    `Detail **${number(recommendation.detailScore)}**\n` +
    `Est. peak wheel torque: **${number(recommendation.estimatedPeakWheelTorqueNm).toFixed(1)} Nm**`;

  const azomText = [
    `Rotation: **${number(azom.wheelRotationAngleDeg)}°**`,
    `Game FFB: **${number(azom.gameFfbStrengthPct)}%**`,
    `Base Torque: **${number(azom.baseTorqueOutputPct)}%**`,
    `Max Wheel Speed: **${number(azom.maximumWheelSpeedPct)}%**`,
    `Interpolation: **${number(azom.interpolation)}**`,
    `Damper / Friction: **${number(azom.wheelDamperPct)}% / ${number(azom.wheelFrictionPct)}%**`,
    `Natural Inertia: **${number(azom.naturalInertia)}**`,
    `High-Speed Damping: **${number(azom.highSpeedDampingPct)}% @ ${number(azom.highSpeedTriggerKph)} kph**`,
  ].join("\n");

  const acText = [
    `Gain: **${number(ac.gainPct)}%**`,
    `Filter: **${number(ac.filterPct)}%**`,
    `Minimum Force: **${number(ac.minimumForcePct)}%**`,
    `Kerb / Road / Slip / ABS: **${number(ac.kerbPct)} / ${number(ac.roadPct)} / ${number(ac.slipPct)} / ${number(ac.absPct)}%**`,
  ].join("\n");

  const behaviorText = [
    `Front Bite ${signed(behavior.frontEndBite)} • Rear Grip ${signed(behavior.rearGrip)}`,
    `Self-Steer ${signed(behavior.selfSteerSpeed)} • Transition ${signed(behavior.transitionSpeed)}`,
    `Stability ${signed(behavior.angleStability)} • Throttle ${signed(behavior.throttleSteering)}`,
    `Initiation ${signed(behavior.initiationSharpness)}`,
  ].join("\n");

  const embed = new EmbedBuilder()
    .setTitle(`⚛️ Atomic Tune ${text(record?.code)}`)
    .setDescription(
      `**${text(car.displayName)}** • ${text(pack.name)}\n` +
      `Drift Target: **${text(intent.name)}**`
    )
    .addFields(
      { name: "Hardware", value: clampField(hardwareText), inline: false },
      { name: "Generated Scores", value: clampField(scoresText), inline: false },
      { name: "AZOM Snapshot", value: clampField(azomText), inline: true },
      { name: "Assetto Corsa FFB", value: clampField(acText), inline: true },
      { name: "Desired Behavior", value: clampField(behaviorText), inline: false }
    )
    .setFooter({
      text:
        `Created with Atomic ${text(payload.atomicVersion, "unknown")} • ` +
        "Snapshot only — importing regenerates locally and does not directly apply hardware settings.",
    });

  if (notes.length) {
    embed.addFields({
      name: "Tune Notes",
      value: clampField(notes.map((note) => `• ${text(note, "")}`).join("\n")),
      inline: false,
    });
  }

  const stamp = record?.createdAt || payload.createdUtc;
  const date = stamp ? new Date(stamp) : null;
  if (date && Number.isFinite(date.getTime())) {
    embed.setTimestamp(date);
  }

  return embed;
}

module.exports = {
  buildShareCreatedEmbed,
  buildTuneEmbed,
};
