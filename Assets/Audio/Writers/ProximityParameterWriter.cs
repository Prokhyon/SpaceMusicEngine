using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Writes a normalised PROXIMITY value [0..1] between a target — usually the
/// player — and the SURFACE of this object's Collider to a FloatParameter each
/// frame. 1 = closest (at or within Near Distance), 0 = farthest (at or beyond
/// Far Distance), linearly interpolated in between.
///
/// Distance is measured from the collider's edges via <see cref="Collider.ClosestPoint"/>,
/// not from the pivot: it is 0 while the target is inside/touching the collider and
/// grows with the gap to the nearest face. This works for any object or zone shape
/// built from a primitive or convex collider (Box, Sphere, Capsule, convex Mesh).
///
/// The Near/Far band IS the range restriction: only distances inside it drive the
/// value, everything nearer saturates at 1 and everything farther at 0. Because the
/// output is already normalised and inverted (near = high), you can bind it straight
/// to an axis with no curve, or add a Curve in the ContextPreset only to shape the
/// falloff. A VolumeEnvelope layer can then fade with that axis.
///
/// Setup:
///   1. Add this component to the object or zone GameObject (it needs a Collider).
///   2. Create a FloatParameter asset and assign it below.
///   3. Leave Target empty to auto-find the object tagged Player Tag, or assign one.
///   4. Set Near/Far Distance to the band you want to react over.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Proximity Parameter Writer")]
[RequireComponent(typeof(Collider))]
public class ProximityParameterWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter parameter;

    [Tooltip("Object to measure the distance to. Leave empty to use the object tagged Player Tag.")]
    [SerializeField] private Transform target;

    [Tooltip("Tag used to auto-find the target when Target is empty.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Surface distance at/below which proximity is 1 (closest).")]
    [SerializeField, Min(0f)] private float nearDistance = 0f;

    [Tooltip("Surface distance at/beyond which proximity is 0 (farthest).")]
    [SerializeField, Min(0f)] private float farDistance = 15f;

    private Collider _col;

    private void Awake()
    {
        _col = GetComponent<Collider>();
    }

    private void Update()
    {
        if (parameter == null || _col == null) return;

        if (target == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag(playerTag);
            if (found == null) return;
            target = found.transform;
        }

        // Distance from the collider surface (0 when the target is inside/touching it).
        Vector3 closest = _col.ClosestPoint(target.position);
        float distance = Vector3.Distance(target.position, closest);

        // Normalise and invert: near = 1, far = 0 (InverseLerp clamps to [0..1]).
        parameter.Value = Mathf.InverseLerp(farDistance, nearDistance, distance);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Vector3 center = col.bounds.center;
        float baseReach = col.bounds.extents.magnitude;

        // Far band (value → 0) and near band (value → 1) around the collider bounds.
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(center, baseReach + farDistance);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(center, baseReach + nearDistance);
    }
#endif
}
