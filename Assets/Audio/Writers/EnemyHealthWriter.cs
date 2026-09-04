using System.Collections.Generic;
using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Writes the normalised health [0..1] of one or more referenced enemies to a
/// FloatParameter. Drag the boss Damageable(s) into the list in the Inspector
/// (selectable visually) — for level 2, add the second Grenadier.
///
/// Aggregation decides how multiple enemies combine into a single value.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Enemy Health Writer")]
public class EnemyHealthWriter : MonoBehaviour
{
    public enum Aggregation { Average, Min, Max }

    [SerializeField] private FloatParameter parameter;

    [Tooltip("Enemy/boss Damageable(s). For one boss, just assign one.")]
    [SerializeField] private List<Damageable> enemies = new List<Damageable>();

    [SerializeField] private Aggregation aggregation = Aggregation.Average;

    [Tooltip("Value written when no enemy is alive/assigned (e.g. boss defeated or destroyed).")]
    [SerializeField] private float valueWhenNone = 0f;

    private void Awake()
    {
        if (parameter != null) parameter.Value = 1f;
    }

    private void Update()
    {
        if (parameter == null) return;

        float agg = 0f;
        int count = 0;

        foreach (var d in enemies)
        {
            if (d == null) continue;

            float h = d.maxHitPoints > 0
                ? Mathf.Clamp01((float)d.currentHitPoints / d.maxHitPoints)
                : 0f;

            if (count == 0) agg = h;
            else if (aggregation == Aggregation.Min) agg = Mathf.Min(agg, h);
            else if (aggregation == Aggregation.Max) agg = Mathf.Max(agg, h);
            else agg += h; // Average accumulates

            count++;
        }

        if (count == 0) { parameter.Value = valueWhenNone; return; }
        parameter.Value = aggregation == Aggregation.Average ? agg / count : agg;
    }
}
