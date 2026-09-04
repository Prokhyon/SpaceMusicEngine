using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    public enum MappingMode { Threshold, Curve }
    public enum CombinationMode { HighestWins, WeightedSum }

    /// <summary>
    /// One step in a threshold table.
    /// The rule fires when the source RawValue >= atLeast.
    /// Sort entries ascending by atLeast for intuitive behaviour —
    /// the entry with the highest matching atLeast wins.
    /// </summary>
    [Serializable]
    public class ThresholdEntry
    {
        [Tooltip("Rule applies when source RawValue >= this value.")]
        public float atLeast;

        [Range(0f, 1f)]
        [Tooltip("Normalised result returned when this threshold is the best match.")]
        public float result;
    }

    /// <summary>
    /// Maps one ParameterDefinition to a normalised float [0..1].
    /// Two modes: a stepped Threshold table or a continuous AnimationCurve.
    /// </summary>
    [Serializable]
    public class MappingRule
    {
        public ParameterDefinition source;

        public MappingMode mode = MappingMode.Threshold;

        [Tooltip("Threshold table. The entry with the highest atLeast that is still <= source RawValue wins.")]
        public List<ThresholdEntry> thresholds = new List<ThresholdEntry>();

        [Tooltip("Maps source RawValue (X-axis) to a normalised result (Y-axis). Used in Curve mode.")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Min(0f)]
        [Tooltip("Relative weight used when the parent AxisBinding is in WeightedSum mode.")]
        public float weight = 1f;

        public float Evaluate()
        {
            if (source == null) return 0f;

            float raw = source.RawValue;

            if (mode == MappingMode.Curve)
                return Mathf.Clamp01(curve.Evaluate(raw));

            // Threshold: find the entry with the highest atLeast that raw still satisfies.
            float result = 0f;
            float bestAtLeast = float.NegativeInfinity;
            foreach (var entry in thresholds)
            {
                if (raw >= entry.atLeast && entry.atLeast >= bestAtLeast)
                {
                    result = entry.result;
                    bestAtLeast = entry.atLeast;
                }
            }
            return Mathf.Clamp01(result);
        }
    }

    /// <summary>
    /// Binds one or more MappingRules to a named axis.
    /// Multiple rules are combined via HighestWins or WeightedSum.
    /// </summary>
    [Serializable]
    public class AxisBinding
    {
        [Tooltip("Must match the axisId of an axis defined in the active Space.")]
        public string axisName;

        public CombinationMode combinationMode = CombinationMode.HighestWins;

        public List<MappingRule> rules = new List<MappingRule>();

        public float Evaluate()
        {
            if (rules.Count == 0) return 0f;

            if (combinationMode == CombinationMode.HighestWins)
            {
                float highest = 0f;
                foreach (var rule in rules)
                    highest = Mathf.Max(highest, rule.Evaluate());
                return highest;
            }

            // WeightedSum — normalised by total weight so the result stays in [0..1].
            float weightedSum = 0f;
            float totalWeight = 0f;
            foreach (var rule in rules)
            {
                float w = Mathf.Max(0f, rule.weight);
                weightedSum += rule.Evaluate() * w;
                totalWeight += w;
            }
            return totalWeight > 0f ? Mathf.Clamp01(weightedSum / totalWeight) : 0f;
        }
    }

    /// <summary>
    /// Reusable mapping configuration that translates game parameters into
    /// normalised axis values. Assign to a MusicSpaceDefinition in MusicProject.
    ///
    /// Evaluate() fills a dictionary: axis name → normalised float [0..1].
    /// DefaultMusicBrain converts these to integer levels and calls SetAxis.
    /// </summary>
    [CreateAssetMenu(fileName = "ContextPreset", menuName = "SpaceMusicEngine/Context Preset")]
    public class ContextPreset : ScriptableObject
    {
        public List<AxisBinding> axisBindings = new List<AxisBinding>();

        /// <summary>
        /// Fills <paramref name="output"/> with the current normalised value for each bound axis.
        /// Pass a pre-allocated dictionary to avoid per-frame allocation.
        /// </summary>
        public void Evaluate(Dictionary<string, float> output)
        {
            output.Clear();
            foreach (var binding in axisBindings)
                if (!string.IsNullOrEmpty(binding.axisName))
                    output[binding.axisName] = binding.Evaluate();
        }
    }
}
