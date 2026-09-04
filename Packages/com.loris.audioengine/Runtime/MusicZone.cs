using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Optional initial coordinate override applied immediately after SetSpace.
    /// axisName must match an axisId defined in the target space.
    /// normalizedValue [0..1] is converted to an integer level automatically.
    /// </summary>
    [Serializable]
    public class AxisInitialValue
    {
        [Tooltip("Must match an axisId in the target space.")]
        public string axisName;

        [Range(0f, 1f)]
        [Tooltip("Normalised value [0..1] converted to an integer level on entry.")]
        public float normalizedValue;
    }

    /// <summary>
    /// Trigger volume that activates a music space when a tagged object enters.
    ///
    /// Add this component to any GameObject with a Collider set to Is Trigger.
    /// Assign the AudioEngineBehaviour and type the Space ID matching a space
    /// defined in your MusicProject asset.
    ///
    /// Use Initial Axis Values to force a starting coordinate (e.g. always begin
    /// a combat zone at full Intensity) before the DefaultMusicBrain takes over.
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine/Music Zone")]
    [RequireComponent(typeof(Collider))]
    public class MusicZone : MonoBehaviour
    {
        [SerializeField] private AudioEngineBehaviour audioEngine;

        [Tooltip("Must match a spaceId in your MusicProject asset.")]
        [SerializeField] private string spaceId;

        [Tooltip("Tag of the object that triggers the zone (usually the player).")]
        [SerializeField] private string triggerTag = "Player";

        [SerializeField] private QuantizationMode entryQuantization = QuantizationMode.NextBar;

        [Tooltip("Optional axis values applied right after SetSpace. Leave empty to let the brain drive from zero.")]
        [SerializeField] private List<AxisInitialValue> initialAxisValues = new List<AxisInitialValue>();

        /// <summary>
        /// When true the zone ignores trigger entries and will not impose its space.
        /// Used by higher-priority systems (e.g. CombatMusicController) to lock zones out.
        /// Note: toggling the component's <c>enabled</c> flag does NOT stop OnTriggerEnter,
        /// so this explicit gate is required.
        /// </summary>
        public bool Suppressed { get; set; }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Suppressed) return;
            if (audioEngine?.Engine == null) return;
            if (!other.CompareTag(triggerTag)) return;

            audioEngine.Engine.SetSpace(spaceId, entryQuantization);

            foreach (var initial in initialAxisValues)
                audioEngine.Engine.SetAxisByName(initial.axisName, initial.normalizedValue, entryQuantization);
        }
    }
}
