using Gamekit3D;
using Gamekit3D.Message;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Writes a [0..1] float that spikes to 1 whenever Ellen takes damage,
/// then decays smoothly to 0 over <see cref="decayTime"/> seconds.
///
/// Attach to the Ellen root GameObject.
/// Then drag this component into the Damageable's "On Damage Message Receivers" list
/// in the Inspector — the Damageable will call OnReceiveMessage directly on each hit.
///
/// Create a FloatParameter asset (e.g. PlayerUnderAttack.asset) and assign it below,
/// then reference it in your ContextPreset axis bindings.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Player Under Attack Writer")]
[RequireComponent(typeof(Damageable))]
public class PlayerUnderAttackWriter : MonoBehaviour, IMessageReceiver
{
    [SerializeField] private FloatParameter underAttackParameter;

    [Tooltip("Seconds for the value to decay from 1 back to 0 after the last hit. "
           + "Set higher to keep combat music playing longer after danger passes.")]
    [SerializeField] private float decayTime = 3f;

    private float _lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        if (underAttackParameter != null)
            underAttackParameter.Value = 0f;
    }

    private void Update()
    {
        if (underAttackParameter == null) return;

        float elapsed = Time.time - _lastHitTime;
        underAttackParameter.Value = Mathf.Clamp01(1f - elapsed / decayTime);
    }

    public void OnReceiveMessage(MessageType type, object sender, object msg)
    {
        if (type == MessageType.DAMAGED)
            _lastHitTime = Time.time;
    }
}
