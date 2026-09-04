using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Loris.AudioEngine
{
    [CreateAssetMenu(fileName = "MusicProject", menuName = "SpaceMusicEngine/Music Project")]
    public class MusicProject : ScriptableObject
    {
        [Header("Output")]
        [Tooltip("Audio Mixer Group all music layers route to, so the engine's output " +
                 "appears in the Unity Audio Mixer. Leave empty to play on the default output.")]
        public AudioMixerGroup output;

        [Header("Global fades")]
        public float defaultFadeInSeconds = 0.5f;
        public float defaultFadeOutSeconds = 0.5f;

        [Header("Spaces")]
        public List<MusicSpaceDefinition> spaces = new();

        [Header("Transitions")]
        public List<TransitionDefinition> transitions = new();

        public MusicSpaceDefinition GetSpace(string spaceId)
        {
            for (int i = 0; i < spaces.Count; i++)
                if (spaces[i].spaceId == spaceId)
                    return spaces[i];
            return null;
        }

        /// <summary>
        /// Output effectif d'un espace : l'override de l'espace s'il est défini,
        /// sinon l'output global du projet.
        /// </summary>
        public AudioMixerGroup ResolveOutput(MusicSpaceDefinition space)
            => space != null && space.output != null ? space.output : output;

        public AudioMixerGroup ResolveOutput(string spaceId)
            => ResolveOutput(GetSpace(spaceId));

        /// <summary>
        /// Cherche une transition reliant fromSpaceId → toSpaceId.
        /// Une chaîne vide représente le silence (début/fin).
        /// Retourne null si aucune transition n'est définie (le moteur retombe
        /// alors sur son crossfade implicite).
        /// </summary>
        public TransitionDefinition GetTransition(string fromSpaceId, string toSpaceId)
        {
            string from = fromSpaceId ?? "";
            string to = toSpaceId ?? "";

            for (int i = 0; i < transitions.Count; i++)
            {
                TransitionDefinition t = transitions[i];
                if (t == null) continue;
                if ((t.fromSpaceId ?? "") == from && (t.toSpaceId ?? "") == to)
                    return t;
            }
            return null;
        }
    }

    [Serializable]
    public class AxisDefinition
    {
        public string axisId;

        [Min(1)]
        public int levelCount = 3;
    }

    /// <summary>
    /// Per-axis filter on a layer.
    /// When enabled is false the layer passes regardless of the axis value.
    /// </summary>
    [Serializable]
    public class AxisFilter
    {
        public bool enabled = false;

        [Min(0)] public int minLevel = 0;
        [Min(0)] public int maxLevel = 0;

        public bool PassesFor(int value)
        {
            if (!enabled) return true;
            int lo = Mathf.Min(minLevel, maxLevel);
            int hi = Mathf.Max(minLevel, maxLevel);
            return value >= lo && value <= hi;
        }
    }

    [Serializable]
    public class MusicSpaceDefinition
    {
        public string spaceId;

        [Header("Output")]
        [Tooltip("Optional. Overrides the MusicProject output for this space only. " +
                 "Leave empty to use the project-wide output.")]
        public AudioMixerGroup output;

        [Header("Transport")]
        public float bpm = 120f;
        public int beatsPerBar = 4;
        public int barsPerLoop = 4;

        [Header("Context")]
        [Tooltip("Optional. Drives axes automatically when a DefaultMusicBrain is in the scene.")]
        public ContextPreset contextPreset;

        [Header("Axes")]
        public List<AxisDefinition> axes = new();

        [Header("Layers")]
        public List<MusicLayerDefinition> layers = new();

        public int ClampAxisValue(int axisIndex, int value)
        {
            if (axisIndex < 0 || axisIndex >= axes.Count) return 0;
            return Mathf.Clamp(value, 0, axes[axisIndex].levelCount);
        }
    }

    /// <summary>
    /// How a layer's presence/volume is driven.
    /// Stepped: on/off by axis level thresholds, applied at quantized boundaries (legacy behaviour).
    /// VolumeEnvelope: the layer is always present and phase-locked while the space is audible;
    /// only its volume is driven continuously by a single axis via a trapezoidal envelope.
    /// </summary>
    public enum LayerVolumeMode { Stepped, VolumeEnvelope }

    /// <summary>
    /// Trapezoidal volume envelope driven by one axis' normalised value [0..1].
    ///
    /// Two independent, optional edges:
    ///  - Fade-in (low side):  below fadeInStart → 0, ramps up to full at fadeInEnd.
    ///    Disabled ⇒ the layer is already full at the bottom.
    ///  - Fade-out (high side): full until fadeOutStart, ramps down to 0 at fadeOutEnd.
    ///    Disabled ⇒ the layer stays full above (no upper cut-off).
    ///
    /// Both disabled ⇒ constant full volume.
    /// </summary>
    [Serializable]
    public class VolumeEnvelope
    {
        [Tooltip("axisId (in this space) whose normalised [0..1] value drives the volume.")]
        public string axisId;

        [Header("Fade-in edge (low side)")]
        public bool fadeInEnabled = true;
        [Range(0f, 1f)] [Tooltip("Below this axis value the layer is silent.")]
        public float fadeInStart = 0f;
        [Range(0f, 1f)] [Tooltip("At this axis value the layer reaches full volume.")]
        public float fadeInEnd = 0.5f;

        [Header("Fade-out edge (high side)")]
        public bool fadeOutEnabled = false;
        [Range(0f, 1f)] [Tooltip("Full volume is held up to this axis value.")]
        public float fadeOutStart = 0.5f;
        [Range(0f, 1f)] [Tooltip("At/above this axis value the layer is silent again.")]
        public float fadeOutEnd = 1f;

        /// <summary>Normalised volume multiplier [0..1] for a normalised axis value.</summary>
        public float Evaluate(float axisValue)
        {
            float v = Mathf.Clamp01(axisValue);
            float vol = 1f;

            if (fadeInEnabled)
            {
                if (v <= fadeInStart) return 0f;
                if (v < fadeInEnd)
                {
                    float t = (v - fadeInStart) / Mathf.Max(1e-6f, fadeInEnd - fadeInStart);
                    vol = Mathf.Min(vol, Mathf.Clamp01(t));
                }
            }

            if (fadeOutEnabled)
            {
                if (v >= fadeOutEnd) return 0f;
                if (v > fadeOutStart)
                {
                    float t = (v - fadeOutStart) / Mathf.Max(1e-6f, fadeOutEnd - fadeOutStart);
                    vol = Mathf.Min(vol, Mathf.Clamp01(1f - t));
                }
            }

            return Mathf.Clamp01(vol);
        }
    }

    [Serializable]
    public class MusicLayerDefinition
    {
        public string layerId;
        public AudioClip clip;

        [Range(0f, 1f)]
        public float baseVolume = 1f;

        [Header("Volume mode")]
        public LayerVolumeMode volumeMode = LayerVolumeMode.Stepped;

        [Tooltip("Used only when Volume Mode is VolumeEnvelope.")]
        public VolumeEnvelope volumeEnvelope = new VolumeEnvelope();

        /// <summary>
        /// One entry per axis in the parent space (parallel list).
        /// A disabled filter passes all values for that axis.
        /// Used only when Volume Mode is Stepped.
        /// </summary>
        [Header("Axis filters")]
        public List<AxisFilter> axisFilters = new();

        [Header("Entry policy")]
        public QuantizationMode entryQuantization = QuantizationMode.NextBar;

        [Header("Loop tail (résonance de fin de boucle)")]
        [Tooltip("Laisse sonner la fin du fichier (résonance / reverb) par-dessus le début de " +
                 "la boucle suivante, au lieu de la couper net au point de boucle. Le fichier " +
                 "doit contenir cette résonance APRÈS la longueur musicale de la boucle.")]
        public bool loopTailEnabled = false;

        [Tooltip("Durée de la résonance jouée après le point de boucle (grille de l'espace). " +
                 "À garder inférieure à la longueur de la boucle.")]
        public MetricInterval loopTailLength = new MetricInterval(0, 0);

        public bool IsActiveForCoordinate(int[] coordinate)
        {
            if (coordinate == null || axisFilters.Count == 0)
                return true;

            int count = Mathf.Min(axisFilters.Count, coordinate.Length);
            for (int i = 0; i < count; i++)
            {
                if (!axisFilters[i].PassesFor(coordinate[i]))
                    return false;
            }
            return true;
        }
    }
}
