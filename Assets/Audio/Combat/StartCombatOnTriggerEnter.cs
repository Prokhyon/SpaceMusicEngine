using UnityEngine;

/// <summary>
/// Starts a CombatMusicController when the player enters this trigger volume.
///
/// Put on a GameObject with a Collider (forced to Is Trigger). Combat must go through
/// CombatMusicController.StartCombat — it locks the exploration MusicZones, imposes the
/// combat space and tracks the bosses — so this calls that rather than setting a space.
///
/// Setup:
///   1. Add to a GameObject, size its Collider over the encounter area.
///   2. Assign the Combat controller.
///   3. Make sure the player's tag matches Trigger Tag (default "Player").
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Combat/Start Combat On Trigger Enter")]
[RequireComponent(typeof(Collider))]
public class StartCombatOnTriggerEnter : MonoBehaviour
{
    [SerializeField] private CombatMusicController combat;

    [Tooltip("Tag of the object that starts combat (usually the player).")]
    [SerializeField] private string triggerTag = "Player";

    [Tooltip("Start combat only the first time the player enters.")]
    [SerializeField] private bool once = true;

    private bool _fired;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (once && _fired) return;
        if (!other.CompareTag(triggerTag)) return;
        if (combat == null) return;

        _fired = true;
        combat.StartCombat();
    }
}
