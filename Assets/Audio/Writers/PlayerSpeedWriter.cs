using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Reads Ellen's horizontal ground speed each frame and writes the
/// time-weighted rolling average over <see cref="windowDuration"/> seconds
/// to a FloatParameter asset.
///
/// Using an average rather than instantaneous speed means brief sprints or
/// stops have little effect; the value only rises when the player has been
/// moving consistently and only falls after sustained stillness.
///
/// The output is in world units/second (same scale as CharacterController
/// velocity). Ellen's max forward speed is ~8 u/s, so configure your
/// ContextPreset Curve with X from 0 to 8.
///
/// Buffer is pre-allocated for up to 120 fps × windowDuration seconds.
/// Attach to the Ellen root GameObject.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Player Speed Writer")]
[RequireComponent(typeof(CharacterController))]
public class PlayerSpeedWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter speedParameter;

    [Tooltip("Length of the rolling average window in seconds. "
           + "Higher values make the parameter change more slowly and ignore short bursts.")]
    [SerializeField, Min(1f)] private float windowDuration = 10f;

    // Pre-allocated circular buffer. 2048 entries covers 15 s at 136 fps.
    private const int BufferSize = 2048;
    private readonly float[] _speedSamples = new float[BufferSize];
    private readonly float[] _deltaSamples = new float[BufferSize];
    private readonly float[] _timeSamples  = new float[BufferSize];

    private int _head;  // index of oldest sample
    private int _count; // number of valid entries

    private CharacterController _cc;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (speedParameter == null || _cc == null) return;

        // --- sample ---
        Vector3 horizontal = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z);
        float   speed      = horizontal.magnitude;
        float   dt         = Time.deltaTime;
        float   now        = Time.time;

        int writeIdx = (_head + _count) % BufferSize;
        _speedSamples[writeIdx] = speed;
        _deltaSamples[writeIdx] = dt;
        _timeSamples[writeIdx]  = now;

        if (_count < BufferSize)
            _count++;
        else
            _head = (_head + 1) % BufferSize; // buffer full: drop oldest

        // --- evict samples older than windowDuration ---
        float cutoff = now - windowDuration;
        while (_count > 0 && _timeSamples[_head] < cutoff)
        {
            _head = (_head + 1) % BufferSize;
            _count--;
        }

        // --- time-weighted average ---
        // weight each sample by its deltaTime so variable frame rate
        // doesn't skew the result toward fast-frame bursts.
        float weightedSum = 0f;
        float totalTime   = 0f;
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head + i) % BufferSize;
            weightedSum += _speedSamples[idx] * _deltaSamples[idx];
            totalTime   += _deltaSamples[idx];
        }

        speedParameter.Value = totalTime > 0f ? weightedSum / totalTime : 0f;
    }
}
