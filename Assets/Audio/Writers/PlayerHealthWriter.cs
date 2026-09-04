using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Reads Ellen's health from the Damageable component and writes a
/// normalised value [0..1] to a FloatParameter asset each frame.
///
/// Attach to the Ellen (player) root GameObject.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Player Health Writer")]
[RequireComponent(typeof(Damageable))]
public class PlayerHealthWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter healthParameter;

    private Damageable _damageable;

    private void Awake()
    {
        _damageable = GetComponent<Damageable>();
        // Initialise to full health so the preset starts in a calm state.
        if (healthParameter != null)
            healthParameter.Value = 1f;
    }

    private void Update()
    {
        if (healthParameter == null || _damageable == null) return;

        healthParameter.Value = _damageable.maxHitPoints > 0
            ? Mathf.Clamp01((float)_damageable.currentHitPoints / _damageable.maxHitPoints)
            : 0f;
    }
}
