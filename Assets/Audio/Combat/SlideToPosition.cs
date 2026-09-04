using System.Collections;
using UnityEngine;

namespace Gamekit3D.GameCommands
{
    /// <summary>
    /// Drives a door's SimpleTranslator to a partial position (e.g. half-closed) and
    /// leaves it there — so the door stays impassable. Reuses the translator's
    /// start/end/rigidbody/curve, so it slides along the exact same path.
    ///
    /// Trigger it once the boss has advanced (wire the advance's OnArrived here, either
    /// as a UnityEvent target or via a GameCommandReceiver + this handler's Interaction Type).
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine Demo/Combat/Slide To Position")]
    public class SlideToPosition : GameCommandHandler
    {
        [Tooltip("The door's SimpleTranslator: its start/end/rigidbody define the travel.")]
        public SimpleTranslator translator;

        [Range(0f, 1f)] [Tooltip("Assumed current normalised position (1 = fully open).")]
        public float fromPosition = 1f;

        [Range(0f, 1f)] [Tooltip("Target normalised position (0.5 = half). The door stays here.")]
        public float toPosition = 0.5f;

        [Min(0.01f)] public float duration = 1f;

        public override void PerformInteraction()
        {
            if (translator == null || translator.rigidbody == null) return;

            // Stop the opener from fighting us over the same rigidbody.
            translator.activate = false;
            translator.enabled = false;

            StopAllCoroutines();
            StartCoroutine(Slide());
        }

        private IEnumerator Slide()
        {
            Rigidbody rb = translator.rigidbody;
            Transform tr = translator.transform;
            AnimationCurve curve = translator.accelCurve;
            bool hasCurve = curve != null && curve.length > 0;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float p = Mathf.Lerp(fromPosition, toPosition, Mathf.Clamp01(t));
                float cp = hasCurve ? curve.Evaluate(p) : p;
                Vector3 pos = tr.TransformPoint(Vector3.Lerp(translator.start, translator.end, cp));
                rb.MovePosition(pos);
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
