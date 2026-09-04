using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Trigger volume that writes a fixed value to a FloatParameter when the
/// player enters, and restores another value when they leave.
///
/// Use this to push a specific axis to a particular level inside a zone
/// (e.g. a dungeon entrance that forces Tension to max regardless of combat).
///
/// Setup:
///   1. Add this component to any GameObject — it forces the Collider to isTrigger.
///   2. Create a FloatParameter asset for the zone effect and assign it below.
///   3. Set Inside Value to the normalised level you want [0..1].
///      For a 3-level axis: 0 = level 0, 0.5 = level 1, 1.0 = level 2.
///   4. Bind that parameter to an axis in your ContextPreset as usual.
///   5. Make sure Ellen's tag matches Trigger Tag (default "Player").
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Zone Parameter Writer")]
[RequireComponent(typeof(Collider))]
public class ZoneParameterWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter parameter;

    [Tooltip("Value written to the parameter while the player is inside the zone.")]
    [Range(0f, 1f)]
    [SerializeField] private float insideValue = 1f;

    [Tooltip("Value written to the parameter when the player leaves the zone.")]
    [Range(0f, 1f)]
    [SerializeField] private float outsideValue = 0f;

    [Tooltip("Tag of the object that triggers the zone. Must match Ellen's tag.")]
    [SerializeField] private string triggerTag = "Player";

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(triggerTag) || parameter == null) return;
        parameter.Value = insideValue;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(triggerTag) || parameter == null) return;
        parameter.Value = outsideValue;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sphere)
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        if (col is BoxCollider box2)
            Gizmos.DrawWireCube(box2.center, box2.size);
        else if (col is SphereCollider sphere2)
            Gizmos.DrawWireSphere(sphere2.center, sphere2.radius);
    }
#endif
}
