using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Writes a [0..1] float that rises to 1 the moment any nearby enemy
/// enters pursuit/awareness state (i.e. they have spotted the player
/// and are moving to engage), then decays to 0 over <see cref="decayTime"/>
/// seconds after all nearby enemies lose interest.
///
/// Chomper + Grenadier use the "InPursuit" animator bool.
/// Spitter uses "HaveTarget". Both are detected automatically.
///
/// Attach to the Ellen root GameObject.
/// Create a FloatParameter asset (e.g. EnemyAwareness.asset) and assign it below.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Enemy Awareness Writer")]
public class EnemyAwarenessWriter : MonoBehaviour
{
    [SerializeField] private FloatParameter awarenessParameter;

    [Tooltip("Radius within which enemies are watched for pursuit state.")]
    [SerializeField] private float detectionRadius = 40f;

    [Tooltip("Seconds to keep the value at 1 (then decay) after all enemies lose the player. "
           + "Keeps combat music going even if the player briefly breaks line of sight.")]
    [SerializeField] private float decayTime = 5f;

    [Tooltip("How often the enemy list is rescanned. Increase for performance.")]
    [SerializeField] private float cacheRefreshInterval = 1f;

    // All pursuit-related animator bool parameter hashes across the three enemy types.
    private static readonly int[] s_PursuitHashes =
    {
        Animator.StringToHash("InPursuit"),  // Chomper, Grenadier
        Animator.StringToHash("HaveTarget"), // Spitter
    };

    private struct EnemyEntry
    {
        public EnemyController Controller;
        public int             PursuitHash; // -1 if this enemy type has no matching parameter
    }

    private EnemyEntry[] _entries = System.Array.Empty<EnemyEntry>();
    private float        _lastAwareTime = float.NegativeInfinity;
    private float        _nextRefresh;

    private void Awake()
    {
        if (awarenessParameter != null)
            awarenessParameter.Value = 0f;
    }

    private void Start() => RefreshCache();

    private void Update()
    {
        if (awarenessParameter == null) return;

        if (Time.time >= _nextRefresh)
            RefreshCache();

        if (AnyEnemyInPursuit())
            _lastAwareTime = Time.time;

        float elapsed = Time.time - _lastAwareTime;
        awarenessParameter.Value = Mathf.Clamp01(1f - elapsed / decayTime);
    }

    private bool AnyEnemyInPursuit()
    {
        Vector3 origin   = transform.position;
        float   radiusSq = detectionRadius * detectionRadius;

        for (int i = 0; i < _entries.Length; i++)
        {
            EnemyEntry entry = _entries[i];
            if (entry.Controller == null || entry.PursuitHash == -1) continue;
            if ((entry.Controller.transform.position - origin).sqrMagnitude > radiusSq) continue;
            if (entry.Controller.animator.GetBool(entry.PursuitHash)) return true;
        }
        return false;
    }

    private void RefreshCache()
    {
        _nextRefresh = Time.time + cacheRefreshInterval;

        var all = FindObjectsByType<EnemyController>();
        _entries = new EnemyEntry[all.Length];

        for (int i = 0; i < all.Length; i++)
        {
            _entries[i] = new EnemyEntry
            {
                Controller  = all[i],
                PursuitHash = ResolvePursuitHash(all[i]),
            };
        }
    }

    // Iterates the animator's parameter list once per enemy to find which
    // pursuit hash this enemy type exposes. Result is cached until next refresh.
    private static int ResolvePursuitHash(EnemyController ec)
    {
        if (ec?.animator == null) return -1;

        foreach (var param in ec.animator.parameters)
        {
            if (param.type != AnimatorControllerParameterType.Bool) continue;
            foreach (int hash in s_PursuitHashes)
                if (param.nameHash == hash) return hash;
        }
        return -1;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
