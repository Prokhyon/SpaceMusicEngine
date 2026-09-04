import { create } from "zustand";
import type {
  AxisDefinition,
  ContextPresetDefinition,
  LayerDefinition,
  NodePosition,
  ParameterInfo,
  ProjectFile,
  SpaceDefinition,
  TransitionDefinition,
} from "../model/types";
import {
  createAxis,
  createAxisFilter,
  createLayer,
  createPreset,
  createProject,
  createSpace,
  createTransition,
} from "../model/factories";

export type View = { kind: "graph" } | { kind: "space-detail"; spaceId: string } | { kind: "presets" };

interface ProjectStore {
  project: ProjectFile;
  filePath: string | null;
  dirty: boolean;
  view: View;

  /** Catalogue des paramètres de jeu scannés dans Assets/ (non persisté). */
  parameterCatalog: ParameterInfo[];
  setParameterCatalog: (catalog: ParameterInfo[]) => void;

  // Navigation
  openGraph: () => void;
  openSpaceDetail: (spaceId: string) => void;
  openPresets: () => void;

  // Cycle de vie du projet
  newProject: () => void;
  replaceProject: (project: ProjectFile, filePath: string | null) => void;
  markSaved: (filePath: string) => void;

  // Projet
  setProjectName: (name: string) => void;

  // Espaces
  addSpace: () => SpaceDefinition;
  removeSpace: (spaceId: string) => void;
  updateSpace: (spaceId: string, patch: Partial<Omit<SpaceDefinition, "spaceId" | "axes" | "layers">>) => void;
  /** Change l'angle d'un espace sur l'orbite de son parent (Space Map). */
  setOrbitAngle: (spaceId: string, angle: number) => void;
  /** Position monde d'une racine de composante (Space Map). */
  setRootPosition: (spaceId: string, position: NodePosition) => void;

  // Axes (les filtres des couches restent des listes parallèles aux axes)
  addAxis: (spaceId: string) => void;
  removeAxis: (spaceId: string, axisIndex: number) => void;
  updateAxis: (spaceId: string, axisIndex: number, patch: Partial<AxisDefinition>) => void;

  // Couches
  addLayer: (spaceId: string) => void;
  removeLayer: (spaceId: string, layerId: string) => void;
  updateLayer: (spaceId: string, layerId: string, patch: Partial<Omit<LayerDefinition, "axisFilters">>) => void;
  updateAxisFilter: (
    spaceId: string,
    layerId: string,
    axisIndex: number,
    patch: Partial<LayerDefinition["axisFilters"][number]>,
  ) => void;

  // Transitions ("" comme fromSpaceId/toSpaceId = silence)
  addTransition: (fromSpaceId: string, toSpaceId: string) => void;
  removeTransition: (transitionId: string) => void;
  updateTransition: (transitionId: string, patch: Partial<Omit<TransitionDefinition, "transitionId">>) => void;

  // Presets
  addPreset: () => void;
  removePreset: (presetId: string) => void;
  updatePreset: (presetId: string, patch: Partial<Omit<ContextPresetDefinition, "presetId">>) => void;
}

export const useProjectStore = create<ProjectStore>((set, get) => {
  /** Clone le projet, applique la mutation, marque le projet modifié. */
  function update(mutate: (project: ProjectFile) => void) {
    const project = structuredClone(get().project);
    mutate(project);
    set({ project, dirty: true });
  }

  function findSpace(project: ProjectFile, spaceId: string): SpaceDefinition | undefined {
    return project.spaces.find((s) => s.spaceId === spaceId);
  }

  return {
    project: createProject(),
    filePath: null,
    dirty: false,
    view: { kind: "graph" },

    parameterCatalog: [],
    setParameterCatalog: (parameterCatalog) => set({ parameterCatalog }),

    openGraph: () => set({ view: { kind: "graph" } }),
    openSpaceDetail: (spaceId) => set({ view: { kind: "space-detail", spaceId } }),
    openPresets: () => set({ view: { kind: "presets" } }),

    newProject: () => set({ project: createProject(), filePath: null, dirty: false, view: { kind: "graph" } }),
    replaceProject: (project, filePath) => set({ project, filePath, dirty: false, view: { kind: "graph" } }),
    markSaved: (filePath) => set({ filePath, dirty: false }),

    setProjectName: (name) => update((p) => void (p.name = name)),

    addSpace: () => {
      const space = createSpace(get().project.spaces);
      update((p) => {
        p.spaces.push(space);
      });
      return space;
    },

    removeSpace: (spaceId) =>
      update((p) => {
        p.spaces = p.spaces.filter((s) => s.spaceId !== spaceId);
        p.transitions = p.transitions.filter((t) => t.fromSpaceId !== spaceId && t.toSpaceId !== spaceId);
        delete p.layout.nodePositions[spaceId];
        if (p.layout.orbitAngles) delete p.layout.orbitAngles[spaceId];
      }),

    updateSpace: (spaceId, patch) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        if (space) Object.assign(space, patch);
      }),

    setOrbitAngle: (spaceId, angle) =>
      update((p) => {
        (p.layout.orbitAngles ??= {})[spaceId] = angle;
      }),

    setRootPosition: (spaceId, position) =>
      update((p) => {
        p.layout.nodePositions[spaceId] = position;
      }),

    addAxis: (spaceId) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        if (!space) return;
        space.axes.push(createAxis(space.axes.length));
        for (const layer of space.layers) layer.axisFilters.push(createAxisFilter());
      }),

    removeAxis: (spaceId, axisIndex) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        if (!space || axisIndex < 0 || axisIndex >= space.axes.length) return;
        space.axes.splice(axisIndex, 1);
        for (const layer of space.layers) layer.axisFilters.splice(axisIndex, 1);
      }),

    updateAxis: (spaceId, axisIndex, patch) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        const axis = space?.axes[axisIndex];
        if (!axis) return;
        Object.assign(axis, patch);
        // Reborne les filtres existants sur la nouvelle plage de l'axe.
        if (patch.levelCount !== undefined && space) {
          for (const layer of space.layers) {
            const f = layer.axisFilters[axisIndex];
            if (!f) continue;
            f.minLevel = Math.min(f.minLevel, axis.levelCount);
            f.maxLevel = Math.min(f.maxLevel, axis.levelCount);
          }
        }
      }),

    addLayer: (spaceId) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        if (space) space.layers.push(createLayer(space));
      }),

    removeLayer: (spaceId, layerId) =>
      update((p) => {
        const space = findSpace(p, spaceId);
        if (space) space.layers = space.layers.filter((l) => l.layerId !== layerId);
      }),

    updateLayer: (spaceId, layerId, patch) =>
      update((p) => {
        const layer = findSpace(p, spaceId)?.layers.find((l) => l.layerId === layerId);
        if (layer) Object.assign(layer, patch);
      }),

    updateAxisFilter: (spaceId, layerId, axisIndex, patch) =>
      update((p) => {
        const layer = findSpace(p, spaceId)?.layers.find((l) => l.layerId === layerId);
        const filter = layer?.axisFilters[axisIndex];
        if (filter) Object.assign(filter, patch);
      }),

    addTransition: (fromSpaceId, toSpaceId) =>
      update((p) => {
        const exists = p.transitions.some((t) => t.fromSpaceId === fromSpaceId && t.toSpaceId === toSpaceId);
        if (exists) return;
        p.transitions.push(createTransition(fromSpaceId, toSpaceId));
      }),

    removeTransition: (transitionId) =>
      update((p) => {
        p.transitions = p.transitions.filter((t) => t.transitionId !== transitionId);
      }),

    updateTransition: (transitionId, patch) =>
      update((p) => {
        const transition = p.transitions.find((t) => t.transitionId === transitionId);
        if (transition) Object.assign(transition, patch);
      }),

    addPreset: () =>
      update((p) => {
        p.presets.push(createPreset(p.presets));
      }),

    removePreset: (presetId) =>
      update((p) => {
        p.presets = p.presets.filter((pr) => pr.presetId !== presetId);
        for (const space of p.spaces) {
          if (space.presetId === presetId) space.presetId = undefined;
        }
      }),

    updatePreset: (presetId, patch) =>
      update((p) => {
        const preset = p.presets.find((pr) => pr.presetId === presetId);
        if (preset) Object.assign(preset, patch);
      }),
  };
});

export function useActiveSpace(): SpaceDefinition | undefined {
  return useProjectStore((s) => {
    const view = s.view;
    return view.kind === "space-detail" ? s.project.spaces.find((sp) => sp.spaceId === view.spaceId) : undefined;
  });
}

export type { ProjectFile, SpaceDefinition, TransitionDefinition };
