using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Concrete MusicBrain that drives axes from the active Space's ContextPreset.
    ///
    /// No subclassing or custom code required for common cases:
    ///   1. Assign ParameterDefinition assets to your ContextPreset in the Inspector.
    ///   2. Game systems write to those assets each frame.
    ///   3. DefaultMusicBrain reads the active preset, evaluates it, and calls SetAxis.
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine/Default Music Brain")]
    public class DefaultMusicBrain : MusicBrain
    {
        [Tooltip("How quickly axis changes are applied. Immediate = next frame; NextBar = next bar boundary.")]
        [SerializeField] private QuantizationMode axisUpdateQuantization = QuantizationMode.Immediate;

        // Pre-allocated to avoid Dictionary allocation every frame.
        private readonly Dictionary<string, float> _evaluationCache = new Dictionary<string, float>();

        protected override void EvaluateState()
        {
            ContextPreset preset = Engine.GetCurrentContextPreset();
            if (preset == null) return;

            preset.Evaluate(_evaluationCache);

            foreach (var kv in _evaluationCache)
                SetAxisByName(kv.Key, kv.Value, axisUpdateQuantization);
        }
    }
}
