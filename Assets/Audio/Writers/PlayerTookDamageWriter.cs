using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Binary [0/1] parameter that snaps to 1 when Ellen takes damage and stays at 1
/// for <see cref="holdSeconds"/> seconds, then drops back to 0.
///
/// Unlike PlayerUnderAttackWriter (which decays smoothly), this is a hard latch —
/// use it when a layer should be fully on or off for a fixed window after a hit.
///
/// Attach to the Ellen (player) root GameObject and assign a FloatParameter asset.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Player Took Damage Writer")]
[RequireComponent(typeof(Damageable))]
public class PlayerTookDamageWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter parameter;

    [Tooltip("Seconds the value stays at 1 after the last hit before snapping back to 0.")]
    [SerializeField] private float holdSeconds = 10f;

    private Damageable _damageable;
    private float _lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        _damageable = GetComponent<Damageable>();
        if (parameter != null) parameter.Value = 0f;
    }

    private void OnEnable()
    {
        if (_damageable != null) _damageable.OnReceiveDamage.AddListener(OnHit);
    }

    private void OnDisable()
    {
        if (_damageable != null) _damageable.OnReceiveDamage.RemoveListener(OnHit);
    }

    private void OnHit() => _lastHitTime = Time.time;

    private void Update()
    {
        if (parameter == null) return;
        parameter.Value = (Time.time - _lastHitTime) <= holdSeconds ? 1f : 0f;
    }
}
