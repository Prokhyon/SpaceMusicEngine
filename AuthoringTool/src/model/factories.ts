import type {
  AxisBinding,
  AxisDefinition,
  AxisFilter,
  ContextPresetDefinition,
  CurveData,
  LayerDefinition,
  MappingRule,
  MetricInterval,
  MetricPosition,
  ProjectFile,
  SpaceDefinition,
  ThresholdEntry,
  TransitionDefinition,
  TransitionVariant,
  VolumeEnvelope,
} from "./types";

let counter = 0;
export function uid(prefix: string): string {
  counter = (counter + 1) % 0xffff;
  return `${prefix}-${Date.now().toString(36)}${counter.toString(36)}`;
}

export const positionOne = (): MetricPosition => ({ bar: 1, beat: 1 });
export const intervalZero = (): MetricInterval => ({ bars: 0, beats: 0 });

export function createAxis(index: number): AxisDefinition {
  return { axisId: `axis${index + 1}`, levelCount: 3 };
}

export function createAxisFilter(): AxisFilter {
  return { enabled: false, minLevel: 0, maxLevel: 0 };
}

export function createVolumeEnvelope(): VolumeEnvelope {
  return {
    axisId: "",
    fadeInEnabled: true,
    fadeInStart: 0,
    fadeInEnd: 0.5,
    fadeOutEnabled: false,
    fadeOutStart: 0.5,
    fadeOutEnd: 1,
  };
}

export function createLayer(space: SpaceDefinition): LayerDefinition {
  return {
    layerId: `layer${space.layers.length + 1}`,
    clipPath: "",
    baseVolume: 1,
    volumeMode: "Stepped",
    volumeEnvelope: createVolumeEnvelope(),
    axisFilters: space.axes.map(() => createAxisFilter()),
    entryQuantization: "NextBar",
    loopTailEnabled: false,
    loopTailLength: intervalZero(),
  };
}

export function createSpace(existing: SpaceDefinition[]): SpaceDefinition {
  const n = existing.length + 1;
  return {
    spaceId: uid("space"),
    name: `Space ${n}`,
    bpm: 120,
    beatsPerBar: 4,
    barsPerLoop: 4,
    axes: [createAxis(0)],
    layers: [],
  };
}

export function createVariant(): TransitionVariant {
  return { departures: [positionOne()], trackStart: positionOne(), arrival: positionOne() };
}

export function createTransition(fromSpaceId: string, toSpaceId: string): TransitionDefinition {
  return {
    transitionId: uid("transition"),
    fromSpaceId,
    toSpaceId,
    hasTrack: false,
    clipPath: "",
    bpm: 120,
    beatsPerBar: 4,
    lengthBars: 1,
    useCustomTransitionLength: false,
    transitionLength: { bars: 1, beats: 0 },
    sourceExit: "FadeOut",
    keepSourcePlaying: false,
    keepSourceUntilEndOfTrack: true,
    keepSourceUntil: positionOne(),
    triggerMode: "Variants",
    variants: [createVariant()],
    frequency: { bars: 1, beats: 0 },
    defaultTrackStart: positionOne(),
    arrivalMode: "PerVariant",
    resumeOffsetBars: 0,
    fixedArrival: positionOne(),
    playTail: false,
    tailLength: intervalZero(),
    tailTempo: "Transition",
  };
}

export function createLinearCurve(): CurveData {
  // Équivalent d'AnimationCurve.Linear(0,0,1,1) côté Unity.
  return {
    keys: [
      { time: 0, value: 0, inSlope: 1, outSlope: 1, inWeight: 0, outWeight: 0, weightedMode: 0 },
      { time: 1, value: 1, inSlope: 1, outSlope: 1, inWeight: 0, outWeight: 0, weightedMode: 0 },
    ],
    preInfinity: 2,
    postInfinity: 2,
  };
}

export function createThreshold(): ThresholdEntry {
  return { atLeast: 0, result: 1 };
}

export function createMappingRule(): MappingRule {
  return {
    parameterGuid: "",
    parameterName: "",
    mode: "Threshold",
    thresholds: [createThreshold()],
    curve: createLinearCurve(),
    weight: 1,
  };
}

export function createAxisBinding(): AxisBinding {
  return { axisName: "", combinationMode: "HighestWins", rules: [createMappingRule()] };
}

export function createPreset(existing: ContextPresetDefinition[]): ContextPresetDefinition {
  return {
    presetId: uid("preset"),
    name: `Preset ${existing.length + 1}`,
    axisBindings: [],
  };
}

export function createProject(): ProjectFile {
  return {
    formatVersion: 3,
    name: "Nouveau projet",
    defaultFadeInSeconds: 0.5,
    defaultFadeOutSeconds: 0.5,
    spaces: [],
    transitions: [],
    presets: [],
    layout: { nodePositions: {} },
  };
}
