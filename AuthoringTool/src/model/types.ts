/**
 * Modèle de données de l'outil d'authoring.
 *
 * Miroir du modèle C# du package `com.loris.audioengine`
 * (MusicProject / MusicSpaceDefinition / MusicLayerDefinition /
 * TransitionDefinition). La synchronisation avec Unity est hors scope
 * de cette phase ; ce fichier fait foi pour le format JSON côté outil.
 *
 * Les références à des assets Unity non modélisés ici (AudioMixerGroup,
 * ContextPreset) sont conservées sous forme de GUID opaques pour ne rien
 * perdre au futur aller-retour.
 */

export type QuantizationMode = "Immediate" | "NextBeat" | "NextBar" | "NextLoop";
export const QUANTIZATION_MODES: QuantizationMode[] = ["Immediate", "NextBeat", "NextBar", "NextLoop"];

export type LayerVolumeMode = "Stepped" | "VolumeEnvelope";
export const LAYER_VOLUME_MODES: LayerVolumeMode[] = ["Stepped", "VolumeEnvelope"];

export type SourceExit = "FadeOut" | "Cut";
export const SOURCE_EXITS: SourceExit[] = ["FadeOut", "Cut"];

export type TriggerMode = "Variants" | "Frequency";
export const TRIGGER_MODES: TriggerMode[] = ["Variants", "Frequency"];

export type ArrivalMode = "PerVariant" | "KeepSourcePosition" | "Fixed";
export const ARRIVAL_MODES: ArrivalMode[] = ["PerVariant", "KeepSourcePosition", "Fixed"];

export type TailTempo = "Transition" | "TargetSpace";
export const TAIL_TEMPOS: TailTempo[] = ["Transition", "TargetSpace"];

export type MappingMode = "Threshold" | "Curve";
export const MAPPING_MODES: MappingMode[] = ["Threshold", "Curve"];

export type CombinationMode = "HighestWins" | "WeightedSum";
export const COMBINATION_MODES: CombinationMode[] = ["HighestWins", "WeightedSum"];

/** Position métrique 1-based (mesure, temps) — cf. MetricPosition côté Unity. */
export interface MetricPosition {
  bar: number;
  beat: number;
}

/** Intervalle métrique (mesures, temps) — cf. MetricInterval côté Unity. */
export interface MetricInterval {
  bars: number;
  beats: number;
}

export interface AxisDefinition {
  axisId: string;
  /**
   * Comme côté Unity (ClampAxisValue), les valeurs d'axe vont de 0 à
   * levelCount INCLUS — un axe avec levelCount = 3 a donc 4 paliers.
   */
  levelCount: number;
}

/** Filtre par axe d'une couche (mode Stepped). Désactivé = passe pour toutes les valeurs. */
export interface AxisFilter {
  enabled: boolean;
  minLevel: number;
  maxLevel: number;
}

/**
 * Enveloppe de volume trapézoïdale pilotée par la valeur normalisée [0..1]
 * d'un axe (mode VolumeEnvelope) — cf. VolumeEnvelope côté Unity.
 */
export interface VolumeEnvelope {
  axisId: string;
  fadeInEnabled: boolean;
  fadeInStart: number;
  fadeInEnd: number;
  fadeOutEnabled: boolean;
  fadeOutStart: number;
  fadeOutEnd: number;
}

export interface LayerDefinition {
  layerId: string;
  /** GUID Unity du clip (canonique — survit aux renommages). */
  clipGuid?: string;
  /** Chemin du clip relatif à Assets/ (affichage/confort ; rafraîchi depuis les .meta). */
  clipPath: string;
  baseVolume: number;
  volumeMode: LayerVolumeMode;
  /** Utilisé seulement en mode VolumeEnvelope. */
  volumeEnvelope: VolumeEnvelope;
  /** Liste parallèle aux axes de l'espace parent. Utilisée seulement en mode Stepped. */
  axisFilters: AxisFilter[];
  entryQuantization: QuantizationMode;
  /** Résonance de fin de boucle jouée par-dessus le début de la boucle suivante. */
  loopTailEnabled: boolean;
  loopTailLength: MetricInterval;
}

export interface SpaceDefinition {
  spaceId: string;
  name: string;
  bpm: number;
  beatsPerBar: number;
  barsPerLoop: number;
  axes: AxisDefinition[];
  layers: LayerDefinition[];
  /** GUID Unity de l'AudioMixerGroup override (opaque, préservé pour l'aller-retour). */
  outputGuid?: string;
  /** fileID Unity du groupe dans le mixer (entier 64 bits → stocké en chaîne). */
  outputFileId?: string;
  /** Référence vers un preset embarqué dans ce projet (presets[]). Prioritaire. */
  presetId?: string;
  /** GUID Unity d'un ContextPreset externe (hérité — utilisé si presetId est vide). */
  contextPresetGuid?: string;
}

/** Un palier d'une table de seuils : s'applique quand RawValue >= atLeast. */
export interface ThresholdEntry {
  atLeast: number;
  result: number;
}

/** Clé d'AnimationCurve Unity (sérialisation fidèle pour l'aller-retour). */
export interface CurveKey {
  time: number;
  value: number;
  inSlope: number;
  outSlope: number;
  inWeight: number;
  outWeight: number;
  weightedMode: number;
}

export interface CurveData {
  keys: CurveKey[];
  preInfinity: number;
  postInfinity: number;
}

/** Mappe un paramètre de jeu (asset Unity Int/FloatParameter) vers [0..1]. */
export interface MappingRule {
  /** GUID Unity de l'asset ParameterDefinition source. */
  parameterGuid: string;
  /** Nom du paramètre (cache d'affichage, rafraîchi depuis le catalogue). */
  parameterName: string;
  mode: MappingMode;
  thresholds: ThresholdEntry[];
  curve: CurveData;
  weight: number;
}

export interface AxisBinding {
  /** Doit correspondre à un axisId de l'espace actif. */
  axisName: string;
  combinationMode: CombinationMode;
  rules: MappingRule[];
}

/** ContextPreset embarqué dans le projet (généré côté Unity par l'importeur). */
export interface ContextPresetDefinition {
  presetId: string;
  name: string;
  axisBindings: AxisBinding[];
}

/** Paramètre de jeu lisible depuis Unity (catalogue scanné dans Assets/). */
export interface ParameterInfo {
  guid: string;
  name: string;
  type: "Int" | "Float";
  assetPath: string;
}

/** Une variante de transition : départs (grille source) partageant trackStart/arrival. */
export interface TransitionVariant {
  departures: MetricPosition[];
  trackStart: MetricPosition;
  arrival: MetricPosition;
}

/**
 * Transition complète — cf. TransitionDefinition côté Unity.
 * fromSpaceId / toSpaceId vides = depuis / vers le silence.
 */
export interface TransitionDefinition {
  transitionId: string;
  fromSpaceId: string;
  toSpaceId: string;

  // Piste
  hasTrack: boolean;
  clipGuid?: string;
  clipPath: string;
  /** GUID Unity du mixer group de la piste (opaque). */
  outputGuid?: string;
  outputFileId?: string;
  bpm: number;
  beatsPerBar: number;
  lengthBars: number;
  useCustomTransitionLength: boolean;
  transitionLength: MetricInterval;

  // Sortie source
  sourceExit: SourceExit;
  keepSourcePlaying: boolean;
  keepSourceUntilEndOfTrack: boolean;
  keepSourceUntil: MetricPosition;

  // Déclenchement
  triggerMode: TriggerMode;
  variants: TransitionVariant[];
  frequency: MetricInterval;
  defaultTrackStart: MetricPosition;

  // Arrivée
  arrivalMode: ArrivalMode;
  resumeOffsetBars: number;
  fixedArrival: MetricPosition;

  // Tail
  playTail: boolean;
  tailLength: MetricInterval;
  tailTempo: TailTempo;
}

export interface NodePosition {
  x: number;
  y: number;
}

export interface ProjectFile {
  formatVersion: 3;
  name: string;
  /** GUID Unity de l'AudioMixerGroup global (opaque). */
  outputGuid?: string;
  outputFileId?: string;
  defaultFadeInSeconds: number;
  defaultFadeOutSeconds: number;
  spaces: SpaceDefinition[];
  transitions: TransitionDefinition[];
  /** ContextPresets embarqués — générés en sous-assets par l'importeur Unity. */
  presets: ContextPresetDefinition[];
  /** Layout de la vue graphe. */
  layout: {
    /** Hérité (layout libre pré-orbital) — conservé pour compat, non utilisé. */
    nodePositions: Record<string, NodePosition>;
    /**
     * Angle (radians, y vers le bas) de chaque espace sur l'orbite de son
     * parent dans la Space Map orbitale. Absent = angle par défaut.
     */
    orbitAngles?: Record<string, number>;
  };
}

/** Coordonnée complète : une valeur de palier par axe de l'espace. */
export type Coordinate = number[];

export function axisLevels(axis: AxisDefinition): number[] {
  const levels: number[] = [];
  for (let v = 0; v <= axis.levelCount; v++) levels.push(v);
  return levels;
}

export function filterPasses(filter: AxisFilter | undefined, value: number): boolean {
  if (!filter || !filter.enabled) return true;
  const lo = Math.min(filter.minLevel, filter.maxLevel);
  const hi = Math.max(filter.minLevel, filter.maxLevel);
  return value >= lo && value <= hi;
}

/** Équivalent de VolumeEnvelope.Evaluate côté Unity (valeur d'axe normalisée [0..1]). */
export function evaluateEnvelope(env: VolumeEnvelope, axisValue: number): number {
  const v = Math.min(1, Math.max(0, axisValue));
  let vol = 1;
  if (env.fadeInEnabled) {
    if (v <= env.fadeInStart) return 0;
    if (v < env.fadeInEnd) {
      const t = (v - env.fadeInStart) / Math.max(1e-6, env.fadeInEnd - env.fadeInStart);
      vol = Math.min(vol, Math.min(1, Math.max(0, t)));
    }
  }
  if (env.fadeOutEnabled) {
    if (v >= env.fadeOutEnd) return 0;
    if (v > env.fadeOutStart) {
      const t = (v - env.fadeOutStart) / Math.max(1e-6, env.fadeOutEnd - env.fadeOutStart);
      vol = Math.min(vol, Math.min(1, Math.max(0, 1 - t)));
    }
  }
  return Math.min(1, Math.max(0, vol));
}

/** Volume relatif [0..1] d'une couche pour une coordonnée (0 = inactive). */
export function layerVolumeAt(layer: LayerDefinition, space: SpaceDefinition, coordinate: Coordinate): number {
  if (layer.volumeMode === "VolumeEnvelope") {
    const axisIndex = space.axes.findIndex((a) => a.axisId === layer.volumeEnvelope.axisId);
    if (axisIndex < 0) return 1; // pas d'axe pilote → toujours plein volume
    const axis = space.axes[axisIndex];
    const normalized = axis.levelCount > 0 ? (coordinate[axisIndex] ?? 0) / axis.levelCount : 0;
    return evaluateEnvelope(layer.volumeEnvelope, normalized);
  }
  const count = Math.min(layer.axisFilters.length, coordinate.length);
  for (let i = 0; i < count; i++) {
    if (!filterPasses(layer.axisFilters[i], coordinate[i])) return 0;
  }
  return 1;
}

/** Une couche « compte » pour une coordonnée si son volume y est non nul. */
export function layerActiveAt(layer: LayerDefinition, space: SpaceDefinition, coordinate: Coordinate): boolean {
  return layerVolumeAt(layer, space, coordinate) > 0;
}

/** Toutes les combinaisons de paliers pour une liste d'axes (ordre lexicographique). */
export function enumerateCoordinates(axes: AxisDefinition[]): Coordinate[] {
  if (axes.length === 0) return [[]];
  const result: Coordinate[] = [];
  const current: number[] = new Array(axes.length).fill(0);
  const walk = (i: number) => {
    if (i === axes.length) {
      result.push([...current]);
      return;
    }
    for (let v = 0; v <= axes[i].levelCount; v++) {
      current[i] = v;
      walk(i + 1);
    }
  };
  walk(0);
  return result;
}
