using Loris.AudioEngine;
using UnityEngine;

namespace Gamekit3D.GameCommands
{
    /// <summary>
    /// GameCommandHandler action that bumps an IntParameter by a fixed step each time it
    /// is performed — so a music axis rises with a gameplay counter.
    ///
    /// Wiring on a GameplayCounter:
    ///   - Direct: assign this component to the counter's "On Increment Perform Action".
    ///   - Via command: set the counter's "On Increment Send Command" to a GameCommandType,
    ///     put this handler on a GameObject with a GameCommandReceiver, and set its
    ///     Interaction Type to the same command.
    ///
    /// Then bind the IntParameter to an axis in your ContextPreset (Threshold/Curve),
    /// e.g. 0→0, 1→0.5, 3→1 for a 3-switch objective.
    ///
    /// PerformInteraction is what the counter calls; the RequireComponent(GameCommandReceiver)
    /// is inherited from GameCommandHandler (Unity adds it automatically).
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine Demo/Writers/Increment Int Parameter")]
    public class IncrementIntParameter : GameCommandHandler
    {
        [SerializeField] private IntParameter parameter;

        [Tooltip("Amount added on each increment.")]
        [SerializeField] private int step = 1;

        [Header("Reset")]
        [Tooltip("Reset the parameter to Initial Value on start. Needed because IntParameter is a "
               + "ScriptableObject: unlike per-frame writers, this only writes on increment, so the "
               + "last count would otherwise persist between play sessions.")]
        [SerializeField] private bool resetOnAwake = true;
        [SerializeField] private int initialValue = 0;

        [Header("Clamp (optional)")]
        [SerializeField] private bool clamp = false;
        [SerializeField] private int minValue = 0;
        [SerializeField] private int maxValue = 3;

        protected override void Awake()
        {
            base.Awake();
            if (resetOnAwake && parameter != null) parameter.Value = initialValue;
        }

        public override void PerformInteraction()
        {
            if (parameter == null) return;
            int next = parameter.Value + step;
            parameter.Value = clamp ? Mathf.Clamp(next, minValue, maxValue) : next;
        }
    }
}
