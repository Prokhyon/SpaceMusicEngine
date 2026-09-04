using System.Collections.Generic;
using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Binary [0/1] parameter that snaps to 1 when the player damages one of the
/// referenced enemies, and stays at 1 for <see cref="holdSeconds"/> seconds.
///
/// In this game enemies only take damage from the player, so any of their
/// OnReceiveDamage / OnDeath events means "the player dealt damage".
///
/// Attach to the Ellen root (or any persistent object) and drag the enemy
/// Damageable(s) into the list. For level 2, add the extra enemies.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Player Dealt Damage Writer")]
public class PlayerDealtDamageWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter parameter;

    [Tooltip("Enemies whose damage counts as 'player dealt damage'.")]
    [SerializeField] private List<Damageable> enemies = new List<Damageable>();

    [Tooltip("Seconds the value stays at 1 after the last hit before snapping back to 0.")]
    [SerializeField] private float holdSeconds = 10f;

    private float _lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        if (parameter != null) parameter.Value = 0f;
    }

    private void OnEnable()
    {
        foreach (var d in enemies)
        {
            if (d == null) continue;
            d.OnReceiveDamage.AddListener(OnEnemyDamaged);
            d.OnDeath.AddListener(OnEnemyDamaged);
        }
    }

    private void OnDisable()
    {
        foreach (var d in enemies)
        {
            if (d == null) continue;
            d.OnReceiveDamage.RemoveListener(OnEnemyDamaged);
            d.OnDeath.RemoveListener(OnEnemyDamaged);
        }
    }

    private void OnEnemyDamaged() => _lastHitTime = Time.time;

    private void Update()
    {
        if (parameter == null) return;
        parameter.Value = (Time.time - _lastHitTime) <= holdSeconds ? 1f : 0f;
    }
}
