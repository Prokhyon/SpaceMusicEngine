using System.Collections.Generic;
using Gamekit3D;
using Loris.AudioEngine;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyDetectionMode
{
    /// <summary>Simple radius check — counts all enemies within detectionRadius.</summary>
    Distance,

    /// <summary>Counts enemies the player can see (unobstructed raycast).</summary>
    LineOfSight,

    /// <summary>Counts enemies reachable via NavMesh within detectionRadius path length.</summary>
    NavMeshReachable,

    /// <summary>Counts enemies that are EITHER visible OR NavMesh-reachable.</summary>
    LineOfSightOrNavMeshReachable,
}

/// <summary>
/// Counts nearby EnemyController objects and writes the result to an IntParameter.
///
/// Detection modes:
///   Distance              — classic radius check.
///   LineOfSight           — raycasts to each enemy; blocked by environment geometry.
///   NavMeshReachable      — NavMesh path exists and its length is within detectionRadius.
///   LineOfSightOrNavMeshReachable — either condition counts.
///
/// The EnemyController list is cached and refreshed every cacheRefreshInterval seconds.
/// NavMesh paths are also computed at refresh time (not every frame).
/// Attach to Ellen (player) root GameObject.
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Writers/Enemy Count Writer")]
public class EnemyCountWriter : MonoBehaviour
{
    [SerializeField] private IntParameter enemyCountParameter;

    [Tooltip("Radius in world units. For NavMesh mode this is the maximum path length, not straight-line distance.")]
    [SerializeField] private float detectionRadius = 30f;

    [SerializeField] private EnemyDetectionMode detectionMode = EnemyDetectionMode.Distance;

    [Header("Line of Sight")]
    [Tooltip("Height above the player's pivot used as the ray origin (approximate eye level).")]
    [SerializeField] private float eyeHeightOffset = 1.5f;

    [Tooltip("Height above an enemy's pivot used as the ray target (approximate torso center).")]
    [SerializeField] private float enemyCenterOffset = 1.0f;

    [Tooltip("Layers that block vision. Should include Environment, Terrain, Level geometry. "
           + "Exclude Player (9), Characters (17), Enemy (23).")]
    [SerializeField] private LayerMask visionBlockingLayers;

    [Header("Cache")]
    [Tooltip("How often (seconds) to refresh the full enemy list and NavMesh paths.")]
    [SerializeField] private float cacheRefreshInterval = 1f;

    private EnemyController[] _allEnemies = new EnemyController[0];
    private readonly HashSet<EnemyController> _navMeshReachable = new();
    private NavMeshPath _pathBuffer;
    private float _nextRefresh;

    private void Awake() => _pathBuffer = new NavMeshPath();

    private void Start() => RefreshCache();

    private void Update()
    {
        if (enemyCountParameter == null) return;

        if (Time.time >= _nextRefresh)
            RefreshCache();

        Vector3 origin  = transform.position;
        float   radiusSq = detectionRadius * detectionRadius;
        int     count   = 0;

        foreach (var ec in _allEnemies)
        {
            if (ec == null) continue;

            // Always apply a straight-line distance pre-filter to skip obviously far enemies.
            if ((ec.transform.position - origin).sqrMagnitude > radiusSq) continue;

            if (IsDetected(ec))
                count++;
        }

        enemyCountParameter.Value = count;
    }

    private bool IsDetected(EnemyController ec)
    {
        return detectionMode switch
        {
            EnemyDetectionMode.LineOfSight                  => HasLineOfSight(ec),
            EnemyDetectionMode.NavMeshReachable             => _navMeshReachable.Contains(ec),
            EnemyDetectionMode.LineOfSightOrNavMeshReachable => HasLineOfSight(ec) || _navMeshReachable.Contains(ec),
            _                                                => true, // Distance
        };
    }

    private bool HasLineOfSight(EnemyController ec)
    {
        Vector3 from = transform.position + Vector3.up * eyeHeightOffset;
        Vector3 to   = ec.transform.position + Vector3.up * enemyCenterOffset;
        Vector3 dir  = to - from;
        float   dist = dir.magnitude;
        if (dist < 0.01f) return true;
        return !Physics.Raycast(from, dir / dist, dist, visionBlockingLayers);
    }

    private void RefreshCache()
    {
        _allEnemies  = FindObjectsByType<EnemyController>();
        _nextRefresh = Time.time + cacheRefreshInterval;

        bool needNavMesh = detectionMode == EnemyDetectionMode.NavMeshReachable
                        || detectionMode == EnemyDetectionMode.LineOfSightOrNavMeshReachable;
        if (needNavMesh)
            RefreshNavMeshReachability();
    }

    private void RefreshNavMeshReachability()
    {
        _navMeshReachable.Clear();
        Vector3 from     = transform.position;
        float   maxDistSq = detectionRadius * detectionRadius * 4f; // generous pre-filter

        foreach (var ec in _allEnemies)
        {
            if (ec == null) continue;
            if ((ec.transform.position - from).sqrMagnitude > maxDistSq) continue;

            NavMesh.CalculatePath(from, ec.transform.position, NavMesh.AllAreas, _pathBuffer);

            if (_pathBuffer.status != NavMeshPathStatus.PathComplete) continue;

            if (GetPathLength(_pathBuffer) <= detectionRadius)
                _navMeshReachable.Add(ec);
        }
    }

    private static float GetPathLength(NavMeshPath path)
    {
        float    length  = 0f;
        Vector3[] corners = path.corners;
        for (int i = 1; i < corners.Length; i++)
            length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        // Sensible default: block vision against solid world geometry.
        // Excludes Player (9), NavMesh (15), Characters (17), Effects (19),
        // Ragdoll (20), Enemy (23), Cameras (24).
        visionBlockingLayers = (1 << 0)  // Default
                             | (1 << 11) // SetDressing
                             | (1 << 16) // Environment
                             | (1 << 18) // Vegetation
                             | (1 << 22) // Terrain
                             | (1 << 25) // Props
                             | (1 << 28) // Collider
                             | (1 << 31);// Level
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
