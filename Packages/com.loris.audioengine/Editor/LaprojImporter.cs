using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Audio;

namespace Loris.AudioEngine.Editor
{
    /// <summary>
    /// Importe un fichier .laproj (JSON produit par l'outil d'authoring externe)
    /// et génère le MusicProject + ses ContextPresets en sous-assets.
    ///
    /// Le .laproj est la SOURCE DE VÉRITÉ : les assets générés sont recréés à
    /// chaque import et ne doivent pas être édités dans l'inspecteur.
    /// Schéma : AuthoringTool/src/model/types.ts (formatVersion 3).
    /// </summary>
    [ScriptedImporter(1, "laproj")]
    public class LaprojImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json = System.IO.File.ReadAllText(ctx.assetPath);
            LaprojFile file = JsonUtility.FromJson<LaprojFile>(json);
            if (file == null || file.formatVersion < 3)
            {
                ctx.LogImportError($"{ctx.assetPath}: formatVersion {file?.formatVersion ?? 0} non supporté (>= 3 requis). Ré-enregistrez le fichier avec l'outil d'authoring.");
                return;
            }

            var project = ScriptableObject.CreateInstance<MusicProject>();
            project.name = string.IsNullOrEmpty(file.name) ? "MusicProject" : file.name;
            project.defaultFadeInSeconds = file.defaultFadeInSeconds;
            project.defaultFadeOutSeconds = file.defaultFadeOutSeconds;
            project.output = ResolveMixerGroup(file.outputGuid, file.outputFileId);

            // Presets embarqués → sous-assets ContextPreset.
            var presetsById = new Dictionary<string, ContextPreset>();
            foreach (LaprojPreset p in file.presets)
            {
                var preset = ScriptableObject.CreateInstance<ContextPreset>();
                preset.name = string.IsNullOrEmpty(p.name) ? p.presetId : p.name;
                foreach (LaprojAxisBinding b in p.axisBindings)
                {
                    var binding = new AxisBinding
                    {
                        axisName = b.axisName,
                        combinationMode = ParseEnum(b.combinationMode, CombinationMode.HighestWins),
                    };
                    foreach (LaprojMappingRule r in b.rules)
                    {
                        var rule = new MappingRule
                        {
                            source = LoadByGuid<ParameterDefinition>(r.parameterGuid),
                            mode = ParseEnum(r.mode, MappingMode.Threshold),
                            weight = r.weight,
                            curve = BuildCurve(r.curve),
                        };
                        rule.thresholds = new List<ThresholdEntry>();
                        foreach (LaprojThreshold t in r.thresholds)
                            rule.thresholds.Add(new ThresholdEntry { atLeast = t.atLeast, result = t.result });
                        binding.rules.Add(rule);
                    }
                    preset.axisBindings.Add(binding);
                }
                presetsById[p.presetId] = preset;
                ctx.AddObjectToAsset($"preset:{p.presetId}", preset);
            }

            foreach (LaprojSpace s in file.spaces)
            {
                var space = new MusicSpaceDefinition
                {
                    spaceId = s.spaceId,
                    bpm = s.bpm,
                    beatsPerBar = s.beatsPerBar,
                    barsPerLoop = s.barsPerLoop,
                    output = ResolveMixerGroup(s.outputGuid, s.outputFileId),
                };

                if (!string.IsNullOrEmpty(s.presetId) && presetsById.TryGetValue(s.presetId, out ContextPreset inline))
                    space.contextPreset = inline;
                else if (!string.IsNullOrEmpty(s.contextPresetGuid))
                    space.contextPreset = LoadByGuid<ContextPreset>(s.contextPresetGuid);

                foreach (LaprojAxis a in s.axes)
                    space.axes.Add(new AxisDefinition { axisId = a.axisId, levelCount = a.levelCount });

                foreach (LaprojLayer l in s.layers)
                {
                    var layer = new MusicLayerDefinition
                    {
                        layerId = l.layerId,
                        clip = LoadByGuid<AudioClip>(l.clipGuid),
                        baseVolume = l.baseVolume,
                        volumeMode = ParseEnum(l.volumeMode, LayerVolumeMode.Stepped),
                        entryQuantization = ParseEnum(l.entryQuantization, QuantizationMode.NextBar),
                        loopTailEnabled = l.loopTailEnabled,
                        loopTailLength = new MetricInterval(l.loopTailLength.bars, l.loopTailLength.beats),
                        volumeEnvelope = new VolumeEnvelope
                        {
                            axisId = l.volumeEnvelope.axisId,
                            fadeInEnabled = l.volumeEnvelope.fadeInEnabled,
                            fadeInStart = l.volumeEnvelope.fadeInStart,
                            fadeInEnd = l.volumeEnvelope.fadeInEnd,
                            fadeOutEnabled = l.volumeEnvelope.fadeOutEnabled,
                            fadeOutStart = l.volumeEnvelope.fadeOutStart,
                            fadeOutEnd = l.volumeEnvelope.fadeOutEnd,
                        },
                    };
                    foreach (LaprojAxisFilter f in l.axisFilters)
                        layer.axisFilters.Add(new AxisFilter { enabled = f.enabled, minLevel = f.minLevel, maxLevel = f.maxLevel });
                    space.layers.Add(layer);
                }

                project.spaces.Add(space);
            }

            foreach (LaprojTransition t in file.transitions)
            {
                var transition = new TransitionDefinition
                {
                    transitionId = t.transitionId,
                    fromSpaceId = t.fromSpaceId,
                    toSpaceId = t.toSpaceId,
                    hasTrack = t.hasTrack,
                    clip = LoadByGuid<AudioClip>(t.clipGuid),
                    output = ResolveMixerGroup(t.outputGuid, t.outputFileId),
                    bpm = t.bpm,
                    beatsPerBar = t.beatsPerBar,
                    lengthBars = t.lengthBars,
                    useCustomTransitionLength = t.useCustomTransitionLength,
                    transitionLength = new MetricInterval(t.transitionLength.bars, t.transitionLength.beats),
                    sourceExit = ParseEnum(t.sourceExit, SourceExit.FadeOut),
                    keepSourcePlaying = t.keepSourcePlaying,
                    keepSourceUntilEndOfTrack = t.keepSourceUntilEndOfTrack,
                    keepSourceUntil = new MetricPosition(t.keepSourceUntil.bar, t.keepSourceUntil.beat),
                    triggerMode = ParseEnum(t.triggerMode, TriggerMode.Variants),
                    frequency = new MetricInterval(t.frequency.bars, t.frequency.beats),
                    defaultTrackStart = new MetricPosition(t.defaultTrackStart.bar, t.defaultTrackStart.beat),
                    arrivalMode = ParseEnum(t.arrivalMode, ArrivalMode.PerVariant),
                    resumeOffsetBars = t.resumeOffsetBars,
                    fixedArrival = new MetricPosition(t.fixedArrival.bar, t.fixedArrival.beat),
                    playTail = t.playTail,
                    tailLength = new MetricInterval(t.tailLength.bars, t.tailLength.beats),
                    tailTempo = ParseEnum(t.tailTempo, TailTempo.Transition),
                };
                transition.variants = new List<TransitionVariant>();
                foreach (LaprojVariant v in t.variants)
                {
                    var variant = new TransitionVariant
                    {
                        trackStart = new MetricPosition(v.trackStart.bar, v.trackStart.beat),
                        arrival = new MetricPosition(v.arrival.bar, v.arrival.beat),
                        departures = new List<MetricPosition>(),
                    };
                    foreach (LaprojPosition d in v.departures)
                        variant.departures.Add(new MetricPosition(d.bar, d.beat));
                    transition.variants.Add(variant);
                }
                project.transitions.Add(transition);
            }

            ctx.AddObjectToAsset("project", project);
            ctx.SetMainObject(project);
        }

        static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>Résout un AudioMixerGroup précis (guid du .mixer + fileID local du groupe).</summary>
        static AudioMixerGroup ResolveMixerGroup(string guid, string fileId)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            if (long.TryParse(fileId, out long localId))
            {
                foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj is AudioMixerGroup group
                        && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(group, out _, out long id)
                        && id == localId)
                        return group;
                }
            }
            // Repli : premier groupe trouvé (typiquement Master).
            return AssetDatabase.LoadAssetAtPath<AudioMixerGroup>(path);
        }

        static AnimationCurve BuildCurve(LaprojCurve c)
        {
            if (c?.keys == null || c.keys.Count == 0)
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var keys = new Keyframe[c.keys.Count];
            for (int i = 0; i < c.keys.Count; i++)
            {
                LaprojCurveKey k = c.keys[i];
                keys[i] = new Keyframe(k.time, k.value, k.inSlope, k.outSlope, k.inWeight, k.outWeight)
                {
                    weightedMode = (WeightedMode)k.weightedMode,
                };
            }
            return new AnimationCurve(keys);
        }

        static T ParseEnum<T>(string value, T fallback) where T : struct
            => Enum.TryParse(value, out T parsed) ? parsed : fallback;

        // ---- DTOs JSON (miroir de AuthoringTool/src/model/types.ts) ----
        // JsonUtility : les enums sont déclarés en string et convertis à la main ;
        // les champs inconnus du JSON (layout, clipPath…) sont ignorés.
#pragma warning disable 0649
        [Serializable] class LaprojFile
        {
            public int formatVersion;
            public string name;
            public string outputGuid;
            public string outputFileId;
            public float defaultFadeInSeconds;
            public float defaultFadeOutSeconds;
            public List<LaprojSpace> spaces = new();
            public List<LaprojTransition> transitions = new();
            public List<LaprojPreset> presets = new();
        }

        [Serializable] class LaprojSpace
        {
            public string spaceId;
            public float bpm;
            public int beatsPerBar;
            public int barsPerLoop;
            public string outputGuid;
            public string outputFileId;
            public string presetId;
            public string contextPresetGuid;
            public List<LaprojAxis> axes = new();
            public List<LaprojLayer> layers = new();
        }

        [Serializable] class LaprojAxis
        {
            public string axisId;
            public int levelCount;
        }

        [Serializable] class LaprojLayer
        {
            public string layerId;
            public string clipGuid;
            public float baseVolume;
            public string volumeMode;
            public LaprojEnvelope volumeEnvelope = new();
            public List<LaprojAxisFilter> axisFilters = new();
            public string entryQuantization;
            public bool loopTailEnabled;
            public LaprojInterval loopTailLength = new();
        }

        [Serializable] class LaprojEnvelope
        {
            public string axisId;
            public bool fadeInEnabled;
            public float fadeInStart;
            public float fadeInEnd;
            public bool fadeOutEnabled;
            public float fadeOutStart;
            public float fadeOutEnd;
        }

        [Serializable] class LaprojAxisFilter
        {
            public bool enabled;
            public int minLevel;
            public int maxLevel;
        }

        [Serializable] class LaprojTransition
        {
            public string transitionId;
            public string fromSpaceId;
            public string toSpaceId;
            public bool hasTrack;
            public string clipGuid;
            public string outputGuid;
            public string outputFileId;
            public float bpm;
            public int beatsPerBar;
            public int lengthBars;
            public bool useCustomTransitionLength;
            public LaprojInterval transitionLength = new();
            public string sourceExit;
            public bool keepSourcePlaying;
            public bool keepSourceUntilEndOfTrack;
            public LaprojPosition keepSourceUntil = new();
            public string triggerMode;
            public List<LaprojVariant> variants = new();
            public LaprojInterval frequency = new();
            public LaprojPosition defaultTrackStart = new();
            public string arrivalMode;
            public int resumeOffsetBars;
            public LaprojPosition fixedArrival = new();
            public bool playTail;
            public LaprojInterval tailLength = new();
            public string tailTempo;
        }

        [Serializable] class LaprojVariant
        {
            public List<LaprojPosition> departures = new();
            public LaprojPosition trackStart = new();
            public LaprojPosition arrival = new();
        }

        [Serializable] class LaprojPosition
        {
            public int bar = 1;
            public int beat = 1;
        }

        [Serializable] class LaprojInterval
        {
            public int bars;
            public int beats;
        }

        [Serializable] class LaprojPreset
        {
            public string presetId;
            public string name;
            public List<LaprojAxisBinding> axisBindings = new();
        }

        [Serializable] class LaprojAxisBinding
        {
            public string axisName;
            public string combinationMode;
            public List<LaprojMappingRule> rules = new();
        }

        [Serializable] class LaprojMappingRule
        {
            public string parameterGuid;
            public string mode;
            public List<LaprojThreshold> thresholds = new();
            public LaprojCurve curve = new();
            public float weight = 1f;
        }

        [Serializable] class LaprojThreshold
        {
            public float atLeast;
            public float result;
        }

        [Serializable] class LaprojCurve
        {
            public List<LaprojCurveKey> keys = new();
        }

        [Serializable] class LaprojCurveKey
        {
            public float time;
            public float value;
            public float inSlope;
            public float outSlope;
            public float inWeight;
            public float outWeight;
            public int weightedMode;
        }
#pragma warning restore 0649
    }

    /// <summary>
    /// Inspecteur lecture seule pour les MusicProject générés depuis un .laproj :
    /// la source de vérité est le fichier JSON, édité par l'outil d'authoring.
    /// </summary>
    [CustomEditor(typeof(MusicProject))]
    public class MusicProjectReadOnlyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            string path = AssetDatabase.GetAssetPath(target);
            bool imported = path != null && path.EndsWith(".laproj", StringComparison.OrdinalIgnoreCase);

            if (imported)
            {
                EditorGUILayout.HelpBox(
                    "Généré depuis un fichier .laproj — la source de vérité est le JSON, édité avec l'outil d'authoring externe. Les modifications faites ici seraient écrasées au prochain import.",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(true))
                    DrawDefaultInspector();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "MusicProject historique (.asset). Le paramétrage se fait désormais dans l'outil d'authoring externe : convertissez ce fichier en .laproj (AuthoringTool/scripts/import-unity.mjs).",
                    MessageType.Warning);
                DrawDefaultInspector();
            }
        }
    }
}
