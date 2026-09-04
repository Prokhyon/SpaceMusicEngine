#!/usr/bin/env node
/**
 * Import ponctuel : convertit un MusicProject .asset (ScriptableObject Unity, YAML)
 * en fichier projet de l'outil (*.laproj, JSON formatVersion 3).
 *
 * Usage :
 *   node scripts/import-unity.mjs <chemin/vers/MonProjet.asset> [sortie.laproj]
 *
 * - Les GUID de clips sont résolus en chemins via les .meta ; le GUID reste canonique.
 * - Les ContextPresets référencés par les espaces sont INLINÉS dans le JSON
 *   (ils deviennent la source de vérité — les .asset presets pourront être supprimés).
 * - Les fileID Unity (64 bits) sont préservés en chaînes.
 */
import fs from "node:fs";
import path from "node:path";
import { parse } from "yaml";

const QUANTIZATION = ["Immediate", "NextBeat", "NextBar", "NextLoop"];
const VOLUME_MODE = ["Stepped", "VolumeEnvelope"];
const SOURCE_EXIT = ["FadeOut", "Cut"];
const TRIGGER_MODE = ["Variants", "Frequency"];
const ARRIVAL_MODE = ["PerVariant", "KeepSourcePosition", "Fixed"];
const TAIL_TEMPO = ["Transition", "TargetSpace"];
const MAPPING_MODE = ["Threshold", "Curve"];
const COMBINATION_MODE = ["HighestWins", "WeightedSum"];

const assetPath = process.argv[2];
if (!assetPath) {
  console.error("Usage: node scripts/import-unity.mjs <MusicProject.asset> [sortie.laproj]");
  process.exit(1);
}

// Localise le dossier Assets/ englobant pour résoudre les GUID.
function findAssetsRoot(start) {
  let dir = path.resolve(start);
  while (dir !== path.dirname(dir)) {
    if (path.basename(dir) === "Assets") return dir;
    dir = path.dirname(dir);
  }
  return null;
}

function buildGuidMap(assetsRoot) {
  const map = new Map();
  if (!assetsRoot) return map;
  const stack = [assetsRoot];
  while (stack.length) {
    const dir = stack.pop();
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, entry.name);
      if (entry.isDirectory()) stack.push(p);
      else if (entry.name.endsWith(".meta")) {
        const m = fs.readFileSync(p, "utf8").match(/^guid: ([0-9a-f]{32})/m);
        if (m) map.set(m[1], p.slice(0, -".meta".length));
      }
    }
  }
  return map;
}

/** Parse un .asset Unity (MonoBehaviour) en objet JS. fileID en BigInt → num()/str(). */
function parseUnityAsset(filePath) {
  const raw = fs.readFileSync(filePath, "utf8");
  const cleaned = raw
    .split("\n")
    .filter((l) => !l.startsWith("%"))
    .join("\n")
    .replace(/^--- .*$/m, "---");
  return parse(cleaned, { intAsBigInt: true })?.MonoBehaviour;
}

const num = (v, d = 0) => (v === undefined || v === null ? d : Number(v));
const bool = (v) => v === 1 || v === 1n || v === true;
const position = (p) => ({ bar: num(p?.bar, 1), beat: num(p?.beat, 1) });
const interval = (i) => ({ bars: num(i?.bars, 0), beats: num(i?.beats, 0) });

const assetsRoot = findAssetsRoot(path.dirname(path.resolve(assetPath)));
const guidMap = buildGuidMap(assetsRoot);
const warnings = [];

function clipRef(clip, context) {
  const guid = clip?.guid;
  if (!guid || num(clip.fileID) === 0) return { clipGuid: undefined, clipPath: "" };
  const p = guidMap.get(guid);
  if (!p) warnings.push(`${context} : clip GUID ${guid} introuvable dans Assets/`);
  return { clipGuid: guid, clipPath: p ? path.relative(assetsRoot, p) : "" };
}

function outputRef(ref) {
  if (!ref?.guid || num(ref.fileID) === 0) return {};
  return { outputGuid: ref.guid, outputFileId: String(ref.fileID) };
}

const mono = parseUnityAsset(assetPath);
if (!mono || !Array.isArray(mono.spaces)) {
  console.error("Ce fichier ne ressemble pas à un MusicProject (pas de MonoBehaviour.spaces).");
  process.exit(1);
}

// ---- Presets : inline les ContextPresets référencés par les espaces ----
const presetByGuid = new Map();

function parameterName(guid) {
  const p = guidMap.get(guid);
  return p ? path.basename(p, ".asset") : "";
}

function importPreset(guid) {
  if (presetByGuid.has(guid)) return presetByGuid.get(guid);
  const presetPath = guidMap.get(guid);
  if (!presetPath) {
    warnings.push(`ContextPreset GUID ${guid} introuvable dans Assets/ — référence externe conservée`);
    return null;
  }
  const p = parseUnityAsset(presetPath);
  if (!p || !Array.isArray(p.axisBindings)) {
    warnings.push(`${path.basename(presetPath)} : pas un ContextPreset lisible — référence externe conservée`);
    return null;
  }
  const preset = {
    presetId: `preset-${guid.slice(0, 8)}`,
    name: p.m_Name ?? path.basename(presetPath, ".asset"),
    axisBindings: (p.axisBindings ?? []).map((b) => ({
      axisName: b.axisName ?? "",
      combinationMode: COMBINATION_MODE[num(b.combinationMode)] ?? "HighestWins",
      rules: (b.rules ?? []).map((r) => ({
        parameterGuid: r.source?.guid ?? "",
        parameterName: r.source?.guid ? parameterName(r.source.guid) : "",
        mode: MAPPING_MODE[num(r.mode)] ?? "Threshold",
        thresholds: (r.thresholds ?? []).map((t) => ({ atLeast: num(t.atLeast), result: num(t.result) })),
        curve: {
          keys: (r.curve?.m_Curve ?? []).map((k) => ({
            time: num(k.time),
            value: num(k.value),
            inSlope: num(k.inSlope),
            outSlope: num(k.outSlope),
            inWeight: num(k.inWeight),
            outWeight: num(k.outWeight),
            weightedMode: num(k.weightedMode),
          })),
          preInfinity: num(r.curve?.m_PreInfinity, 2),
          postInfinity: num(r.curve?.m_PostInfinity, 2),
        },
        weight: num(r.weight, 1),
      })),
    })),
  };
  presetByGuid.set(guid, preset);
  return preset;
}

// ---- Espaces ----
const spaces = mono.spaces.map((s) => {
  const axes = (s.axes ?? []).map((a) => ({ axisId: a.axisId ?? "", levelCount: num(a.levelCount, 1) }));
  const layers = (s.layers ?? []).map((l) => {
    const env = l.volumeEnvelope ?? {};
    const filters = axes.map((axis, i) => {
      const f = (l.axisFilters ?? [])[i];
      return f
        ? {
            enabled: bool(f.enabled),
            minLevel: Math.min(num(f.minLevel), axis.levelCount),
            maxLevel: Math.min(num(f.maxLevel), axis.levelCount),
          }
        : { enabled: false, minLevel: 0, maxLevel: 0 };
    });
    return {
      layerId: l.layerId ?? "",
      ...clipRef(l.clip, `${s.spaceId}/${l.layerId}`),
      baseVolume: num(l.baseVolume, 1),
      volumeMode: VOLUME_MODE[num(l.volumeMode)] ?? "Stepped",
      volumeEnvelope: {
        axisId: env.axisId ?? "",
        fadeInEnabled: bool(env.fadeInEnabled ?? 1),
        fadeInStart: num(env.fadeInStart, 0),
        fadeInEnd: num(env.fadeInEnd, 0.5),
        fadeOutEnabled: bool(env.fadeOutEnabled),
        fadeOutStart: num(env.fadeOutStart, 0.5),
        fadeOutEnd: num(env.fadeOutEnd, 1),
      },
      axisFilters: filters,
      entryQuantization: QUANTIZATION[num(l.entryQuantization)] ?? "NextBar",
      loopTailEnabled: bool(l.loopTailEnabled),
      loopTailLength: interval(l.loopTailLength),
    };
  });

  const presetGuid = s.contextPreset?.guid && num(s.contextPreset.fileID) !== 0 ? s.contextPreset.guid : undefined;
  const inlined = presetGuid ? importPreset(presetGuid) : null;

  return {
    spaceId: s.spaceId ?? "",
    name: s.spaceId ?? "",
    bpm: num(s.bpm, 120),
    beatsPerBar: num(s.beatsPerBar, 4),
    barsPerLoop: num(s.barsPerLoop, 4),
    axes,
    layers,
    ...outputRef(s.output),
    presetId: inlined?.presetId,
    contextPresetGuid: inlined ? undefined : presetGuid,
  };
});

// ---- Transitions ----
const transitions = (mono.transitions ?? []).map((t) => ({
  transitionId: t.transitionId || `${t.fromSpaceId || "silence"}->${t.toSpaceId || "silence"}`,
  fromSpaceId: t.fromSpaceId ?? "",
  toSpaceId: t.toSpaceId ?? "",
  hasTrack: bool(t.hasTrack),
  ...clipRef(t.clip, `Transition ${t.transitionId}`),
  ...outputRef(t.output),
  bpm: num(t.bpm, 120),
  beatsPerBar: num(t.beatsPerBar, 4),
  lengthBars: num(t.lengthBars, 1),
  useCustomTransitionLength: bool(t.useCustomTransitionLength),
  transitionLength: interval(t.transitionLength),
  sourceExit: SOURCE_EXIT[num(t.sourceExit)] ?? "FadeOut",
  keepSourcePlaying: bool(t.keepSourcePlaying),
  keepSourceUntilEndOfTrack: bool(t.keepSourceUntilEndOfTrack ?? 1),
  keepSourceUntil: position(t.keepSourceUntil),
  triggerMode: TRIGGER_MODE[num(t.triggerMode)] ?? "Variants",
  variants: (t.variants ?? []).map((v) => ({
    departures: (v.departures ?? []).map(position),
    trackStart: position(v.trackStart),
    arrival: position(v.arrival),
  })),
  frequency: interval(t.frequency),
  defaultTrackStart: position(t.defaultTrackStart),
  arrivalMode: ARRIVAL_MODE[num(t.arrivalMode)] ?? "PerVariant",
  resumeOffsetBars: num(t.resumeOffsetBars, 0),
  fixedArrival: position(t.fixedArrival),
  playTail: bool(t.playTail),
  tailLength: interval(t.tailLength),
  tailTempo: TAIL_TEMPO[num(t.tailTempo)] ?? "Transition",
}));

// Placement initial des nœuds en grille ; l'utilisateur réorganise dans la vue graphe.
const nodePositions = { __silence__: { x: -280, y: 0 } };
spaces.forEach((s, i) => {
  nodePositions[s.spaceId] = { x: (i % 3) * 260, y: Math.floor(i / 3) * 140 };
});

const project = {
  formatVersion: 3,
  name: path.basename(assetPath, ".asset"),
  ...outputRef(mono.output),
  defaultFadeInSeconds: num(mono.defaultFadeInSeconds, 0.5),
  defaultFadeOutSeconds: num(mono.defaultFadeOutSeconds, 0.5),
  spaces,
  transitions,
  presets: [...presetByGuid.values()],
  layout: { nodePositions },
};

const outPath = process.argv[3] ?? path.join(process.cwd(), "projects", `${project.name}.laproj`);
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(project, null, 2));

console.log(`✓ ${outPath}`);
console.log(`  ${spaces.length} espaces, ${transitions.length} transitions, ${project.presets.length} presets inlinés`);
for (const w of warnings) console.warn(`  ⚠ ${w}`);
