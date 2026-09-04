import { open, save } from "@tauri-apps/plugin-dialog";
import { readTextFile, writeTextFile } from "@tauri-apps/plugin-fs";
import type { LayerDefinition, ProjectFile, TransitionDefinition } from "../model/types";
import { createLayer, createTransition, createVolumeEnvelope, intervalZero } from "../model/factories";
import { useProjectStore } from "../store/useProjectStore";
import { refreshParameterCatalog } from "./parameterCatalog";

const FILTERS = [{ name: "Loris Audio Project", extensions: ["laproj", "laproj.json", "json"] }];

/** Sauvegarde vers le fichier courant, ou demande un chemin s'il n'y en a pas. */
export async function saveProject(forceDialog = false): Promise<void> {
  const { project, filePath, markSaved } = useProjectStore.getState();

  let target = forceDialog ? null : filePath;
  if (!target) {
    target = await save({
      filters: FILTERS,
      defaultPath: `${project.name || "projet"}.laproj`,
    });
    if (!target) return; // annulé par l'utilisateur
  }

  await writeTextFile(target, JSON.stringify(project, null, 2));
  markSaved(target);
}

export async function loadProject(): Promise<void> {
  const selected = await open({ multiple: false, filters: FILTERS });
  if (typeof selected !== "string") return; // annulé

  const raw = await readTextFile(selected);
  const parsed = JSON.parse(raw) as ProjectFile & { formatVersion: number };
  if (![1, 2, 3].includes(parsed.formatVersion) || !Array.isArray(parsed.spaces)) {
    throw new Error("Fichier de projet invalide ou version non supportée.");
  }

  useProjectStore.getState().replaceProject(migrate(parsed), selected);
  // Catalogue des paramètres du projet Unity englobant (si le fichier est dans Assets/).
  void refreshParameterCatalog(selected).catch(() => {});
}

/**
 * Amène un projet v1/v2 (ou partiel) au format v3 en complétant les champs
 * manquants par leurs valeurs par défaut. Le champ v1 `quantization` des
 * transitions (sans équivalent Unity) est abandonné.
 */
function migrate(p: ProjectFile): ProjectFile {
  p.formatVersion = 3;
  p.presets ??= [];
  p.transitions ??= [];
  p.layout ??= { nodePositions: {} };
  p.layout.nodePositions ??= {};
  p.layout.orbitAngles ??= {};

  for (const space of p.spaces) {
    space.layers ??= [];
    space.axes ??= [];
    for (const layer of space.layers) {
      fillDefaults(layer, {
        ...createLayer(space),
        layerId: layer.layerId,
      });
      layer.volumeEnvelope = { ...createVolumeEnvelope(), ...layer.volumeEnvelope };
      layer.loopTailLength = { ...intervalZero(), ...layer.loopTailLength };
    }
  }

  p.transitions = p.transitions.map((t) => {
    const full: TransitionDefinition = {
      ...createTransition(t.fromSpaceId ?? "", t.toSpaceId ?? ""),
      ...stripUnknown(t),
      transitionId: t.transitionId,
    };
    return full;
  });

  return p;
}

function fillDefaults(target: LayerDefinition, defaults: LayerDefinition) {
  for (const key of Object.keys(defaults) as (keyof LayerDefinition)[]) {
    if (target[key] === undefined) (target as unknown as Record<string, unknown>)[key] = defaults[key];
  }
}

/** Retire les champs v1 sans équivalent v2 (ex. `quantization`). */
function stripUnknown(t: TransitionDefinition): Partial<TransitionDefinition> {
  const { ...rest } = t as TransitionDefinition & { quantization?: unknown };
  delete (rest as { quantization?: unknown }).quantization;
  return rest;
}
