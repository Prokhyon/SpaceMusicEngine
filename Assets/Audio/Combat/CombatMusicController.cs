using System.Collections.Generic;
using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Drives a dedicated combat/boss music space with priority over the normal
/// exploration spaces.
///
/// Start: call <see cref="StartCombat"/> from the door-open UnityEvent (after the
/// three switches). While active it forces the combat space and disables the
/// exploration MusicZones so they cannot re-impose their space.
///
/// End: when all referenced bosses are dead, OR the player escapes detection for
/// <see cref="escapeGraceSeconds"/> after having engaged, OR <see cref="EndCombat"/>
/// is called manually. On end it re-enables the zones and reverts to exploration.
///
/// Progression of the combat layers is handled separately by the parameter writers
/// (BossDetectedWriter, EnemyHealthWriter, ...) feeding the combat space's axes.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Combat/Combat Music Controller")]
public class CombatMusicController : MonoBehaviour
{
    [SerializeField] private AudioEngineBehaviour audioEngine;

    [Header("Spaces")]
    [SerializeField] private string combatSpaceId = "Combat";
    [SerializeField] private string explorationSpaceId = "Exploration";
    [SerializeField] private QuantizationMode quantization = QuantizationMode.NextBar;

    [Header("Lockout")]
    [Tooltip("Exploration MusicZones disabled while combat is active so they can't re-impose their space.")]
    [SerializeField] private List<MusicZone> explorationZones = new List<MusicZone>();

    [Header("Bosses")]
    [Tooltip("Bosses that define this encounter. Combat ends when all are defeated.")]
    [SerializeField] private List<GrenadierBehaviour> bosses = new List<GrenadierBehaviour>();

    [Tooltip("If enabled, combat ends when the player escapes the boss's detection long enough. "
           + "If disabled, the combat space stays active regardless of proximity/detection — "
           + "it then only ends when all bosses are defeated (or EndCombat is called).")]
    [SerializeField] private bool endOnPlayerEscape = true;

    [Tooltip("Seconds the player can stay undetected (after first engaging) before combat ends. "
           + "Used only when 'End On Player Escape' is enabled.")]
    [SerializeField] private float escapeGraceSeconds = 8f;

    [Header("Debug")]
    [Tooltip("Log every StartCombat/EndCombat to the Console, to verify the trigger fires.")]
    [SerializeField] private bool verboseLogging = true;

    [Tooltip("Press to force-start combat, bypassing the command system (testing only).")]
    [SerializeField] private KeyCode debugStartKey = KeyCode.None;

    [Tooltip("Press to force-end combat (testing only).")]
    [SerializeField] private KeyCode debugEndKey = KeyCode.None;

    private bool _active;
    private bool _hasEngaged;
    private float _lastEngagedTime;

    private AudioEngineFacade Engine => audioEngine != null ? audioEngine.Engine : null;

    /// <summary>Wire this to the door-open UnityEvent (after the 3 switches).</summary>
    public void StartCombat()
    {
        if (verboseLogging)
            Debug.Log($"[CombatMusic] StartCombat() called. active={_active}, engine={(Engine != null ? "OK" : "NULL")}, current space='{Engine?.GetCurrentSpace()}'", this);

        if (_active) return;
        _active = true;
        _hasEngaged = false;
        _lastEngagedTime = Time.time;

        SetZonesEnabled(false);

        if (Engine == null)
        {
            Debug.LogWarning("[CombatMusic] StartCombat: AudioEngine is not assigned — nothing will play.", this);
            return;
        }
        Engine.SetSpace(combatSpaceId, quantization);
    }

    /// <summary>Optional manual end (e.g. from a cutscene).</summary>
    public void EndCombat()
    {
        if (verboseLogging)
            Debug.Log($"[CombatMusic] EndCombat() called. active={_active}", this);

        if (!_active) return;
        _active = false;

        SetZonesEnabled(true);
        // The player is usually still inside the exploration zone, which won't
        // re-trigger OnTriggerEnter, so we revert to exploration explicitly.
        Engine?.SetSpace(explorationSpaceId, quantization);
    }

    private void Update()
    {
        if (debugStartKey != KeyCode.None && Input.GetKeyDown(debugStartKey))
            StartCombat();
        if (debugEndKey != KeyCode.None && Input.GetKeyDown(debugEndKey))
            EndCombat();

        if (!_active) return;

        if (AllBossesDead())
        {
            EndCombat();
            return;
        }

        // Escape-by-distance/detection is optional. When disabled, combat stays active
        // regardless of proximity/detection — only AllBossesDead (or EndCombat) ends it.
        if (!endOnPlayerEscape) return;

        if (AnyBossEngaged())
        {
            _hasEngaged = true;
            _lastEngagedTime = Time.time;
        }
        else if (_hasEngaged && Time.time - _lastEngagedTime > escapeGraceSeconds)
        {
            EndCombat();
        }
    }

    private bool AnyBossEngaged()
    {
        foreach (var b in bosses)
            if (b != null && b.damageable != null && b.damageable.currentHitPoints > 0 && b.target != null)
                return true;
        return false;
    }

    // True only if at least one boss was assigned and none are still alive.
    private bool AllBossesDead()
    {
        bool anyAssigned = false;
        foreach (var b in bosses)
        {
            if (b == null) continue;
            anyAssigned = true;
            if (b.damageable != null && b.damageable.currentHitPoints > 0)
                return false;
        }
        return anyAssigned;
    }

    // Suppress (not disable) the zones: toggling MonoBehaviour.enabled does NOT stop
    // OnTriggerEnter from firing, so we use MusicZone's explicit Suppressed gate instead.
    private void SetZonesEnabled(bool enabled)
    {
        foreach (var z in explorationZones)
            if (z != null) z.Suppressed = !enabled;
    }
}
