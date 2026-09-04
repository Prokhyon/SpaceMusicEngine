using System.Collections.Generic;
using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Binary [0/1] parameter that becomes 1 once a referenced boss has acquired the
/// player as its target (i.e. the player has been detected / engaged), and stays
/// at 1 for <see cref="holdSeconds"/> seconds after the boss last had a target
/// (smooths brief losses of sight).
///
/// Drives "combat progression once detected". Assign the Grenadier(s) below.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Boss Detected Writer")]
public class BossDetectedWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter parameter;

    [Tooltip("Boss(es) whose detection of the player drives this parameter.")]
    [SerializeField] private List<GrenadierBehaviour> bosses = new List<GrenadierBehaviour>();

    [Tooltip("Seconds to keep the value at 1 after the boss last had a target.")]
    [SerializeField] private float holdSeconds = 2f;

    private float _lastDetectedTime = float.NegativeInfinity;

    private void Awake()
    {
        if (parameter != null) parameter.Value = 0f;
    }

    private void Update()
    {
        if (parameter == null) return;

        if (AnyBossHasTarget())
            _lastDetectedTime = Time.time;

        parameter.Value = (Time.time - _lastDetectedTime) <= holdSeconds ? 1f : 0f;
    }

    private bool AnyBossHasTarget()
    {
        foreach (var b in bosses)
            if (b != null && b.target != null) return true;
        return false;
    }
}
