using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Drives an IntParameter like a counter. Wire the switch/objective UnityEvent to
/// <see cref="Increment"/> so each activation adds to the parameter — a ContextPreset
/// MappingRule then turns that value into an axis level.
///
/// Because a UnityEvent can't do "value += 1" directly on the asset, this component
/// exposes ready-to-wire methods: Increment / Decrement / Add / SetValue / ResetValue.
///
/// Setup:
///   1. Create an IntParameter asset and assign it below.
///   2. On each switch, add its activation UnityEvent → this component → Increment().
///   3. Bind the IntParameter to an axis in your ContextPreset (Threshold or Curve),
///      e.g. 0→0, 1→0.5, 3→1 for a 3-switch objective.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Int Parameter Counter Writer")]
public class IntParameterCounterWriter : MonoBehaviour
{
    [SerializeField] private IntParameter parameter;

    [Tooltip("Amount added by Increment() (and subtracted by Decrement()).")]
    [SerializeField] private int step = 1;

    [Tooltip("Value written on Awake and by ResetValue().")]
    [SerializeField] private int initialValue = 0;

    [Header("Clamp (optional)")]
    [SerializeField] private bool clamp = false;
    [SerializeField] private int minValue = 0;
    [SerializeField] private int maxValue = 3;

    private void Awake()
    {
        if (parameter != null) parameter.Value = Clamped(initialValue);
    }

    /// <summary>Wire this to the switch activation UnityEvent.</summary>
    public void Increment() => Add(step);

    public void Decrement() => Add(-step);

    public void Add(int amount)
    {
        if (parameter == null) return;
        parameter.Value = Clamped(parameter.Value + amount);
    }

    public void SetValue(int value)
    {
        if (parameter == null) return;
        parameter.Value = Clamped(value);
    }

    public void ResetValue()
    {
        if (parameter != null) parameter.Value = Clamped(initialValue);
    }

    private int Clamped(int v) => clamp ? Mathf.Clamp(v, minValue, maxValue) : v;
}
