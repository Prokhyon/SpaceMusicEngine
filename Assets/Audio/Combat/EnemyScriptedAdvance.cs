using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace Gamekit3D.GameCommands
{
    /// <summary>
    /// Scripted forward advance for an enemy, played BEFORE its combat AI takes over.
    ///
    /// The Kit's enemies move via root motion driven by a combat state machine that only
    /// pursues the player, so there is no clean "walk to a point" through the AI — and its
    /// SceneLinkedSMB keeps running and yanks the animator back to Idle. To avoid that,
    /// this plays the walk clip through a PlayableGraph, which bypasses the Animator
    /// Controller entirely (no state machine to fight, guaranteed loop), and uses the
    /// clip's ROOT MOTION to move the enemy (no foot-sliding).
    ///
    /// Sequence: suspend combat behaviour + EnemyController → play walk clip and steer
    /// toward the destination → on arrival, restore the graph/AI (combat starts) and fire
    /// OnArrived (wire the door half-close there).
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine Demo/Combat/Enemy Scripted Advance")]
    public class EnemyScriptedAdvance : GameCommandHandler
    {
        [Header("Refs")]
        [Tooltip("EnemyController — suspended during the advance, re-enabled after (combat resumes).")]
        public EnemyController enemyController;
        [Tooltip("Combat behaviour to suspend during the advance (e.g. GrenadierBehaviour).")]
        public MonoBehaviour combatBehaviour;

        [Header("Move")]
        [Tooltip("Target position. If empty, advances 'Forward Meters' along the enemy's forward.")]
        public Transform destination;
        [Tooltip("Used ONLY when Destination is empty.")]
        public float forwardMeters = 4f;
        public float turnSpeedDeg = 540f;
        public float arriveThreshold = 0.3f;

        [Header("Walk animation")]
        [Tooltip("The walk clip (drag the GrenadierWalk clip from its .fbx). Played looped via a PlayableGraph.")]
        public AnimationClip walkClip;
        [Tooltip("Root motion of the walk clip drives the movement (no sliding). " +
                 "Disable to move at a fixed speed instead (the clip only animates the legs).")]
        public bool useRootMotion = true;
        [Tooltip("Move speed when Use Root Motion is OFF.")]
        public float speed = 2.5f;

        [Header("On arrived")]
        [Tooltip("Fires once the advance is done — wire the door half-close here.")]
        public UnityEvent onArrived;
        public SendGameCommand onArrivedCommand;

        private bool _advancing;
        private Vector3 _target;
        private Animator _animator;
        private NavMeshAgent _agent;
        private bool _prevRootMotion;

        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;

        public override void PerformInteraction()
        {
            if (_advancing) return;
            _advancing = true;

            _target = destination != null
                ? destination.position
                : transform.position + transform.forward * forwardMeters;

            // enemyController's cached refs are null if it was never enabled — fall back to GetComponent.
            _animator = enemyController != null ? enemyController.animator : null;
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _agent = enemyController != null ? enemyController.navmeshAgent : null;
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();

            // Suspend the AI so neither its code nor its state machine drives movement.
            if (combatBehaviour != null) combatBehaviour.enabled = false;
            if (enemyController != null) enemyController.enabled = false;
            if (_agent != null) _agent.enabled = false;

            if (_animator != null && walkClip != null)
            {
                _prevRootMotion = _animator.applyRootMotion;
                _animator.applyRootMotion = useRootMotion;
                _animator.speed = 1f;

                // Play the walk clip directly, bypassing the Animator Controller (and its SMBs).
                _graph = PlayableGraph.Create("EnemyScriptedAdvance");
                var output = AnimationPlayableOutput.Create(_graph, "Anim", _animator);
                _clipPlayable = AnimationClipPlayable.Create(_graph, walkClip);
                _clipPlayable.SetApplyFootIK(false);
                output.SetSourcePlayable(_clipPlayable);
                _graph.Play();
            }
        }

        private void Update()
        {
            if (!_advancing) return;

            // Steer toward the target (root motion / MoveTowards then carries us forward).
            Vector3 flatTarget = new Vector3(_target.x, transform.position.y, _target.z);
            Vector3 to = flatTarget - transform.position;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeedDeg * Time.deltaTime);
            }

            if (!useRootMotion)
                transform.position = Vector3.MoveTowards(transform.position, flatTarget, speed * Time.deltaTime);

            // NB: no manual time wrap — the clip loops natively (Loop Time), which keeps the
            // root-motion delta continuous across the seam. Resetting the time here would make
            // deltaPosition jump backward one cycle ("moonwalk").

            if ((transform.position - flatTarget).sqrMagnitude <= arriveThreshold * arriveThreshold)
                Arrive();
        }

        // Applies the walk clip's root motion to the transform (bypassed EnemyController's own).
        private void OnAnimatorMove()
        {
            if (!_advancing || !useRootMotion || _animator == null) return;
            transform.position += _animator.deltaPosition;
        }

        private void Arrive()
        {
            _advancing = false;
            TeardownGraph();

            // Hand movement back to the AI. Re-enabling EnemyController re-inits the agent
            // (OnEnable: updatePosition = false, follow = true, rigidbody synced).
            if (_animator != null) _animator.applyRootMotion = _prevRootMotion;
            if (_agent != null)
            {
                _agent.enabled = true;
                _agent.Warp(transform.position);
            }
            if (enemyController != null) enemyController.enabled = true;
            if (combatBehaviour != null) combatBehaviour.enabled = true;

            if (onArrived != null) onArrived.Invoke();
            if (onArrivedCommand != null) onArrivedCommand.Send();
        }

        private void TeardownGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        private void OnDisable()
        {
            TeardownGraph();
        }
    }
}
