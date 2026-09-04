using System.Collections;
using Gamekit3D;
using Gamekit3D.Message;
using Loris.AudioEngine;
using UnityEngine;

namespace Gamekit3D.Experiment
{
    // Lives inside a single level scene instance (one per Level1/Level1_Static/Level2/Level2_Static
    // load) and forwards gameplay events to ExperimentManager, which owns the actual data storage.
    public class GameplayLogger : MonoBehaviour, IMessageReceiver
    {
        const float MovementSampleInterval = 1.5f;
        const float AttackHitWindowSeconds = 0.4f;

        public static GameplayLogger Current { get; private set; }

        float m_StartTime;
        float m_LastMovementSample;
        int m_DeathCountTotal;
        string m_LastCheckpointId = string.Empty;
        bool m_PendingAttackHit;
        bool m_WasRespawning;

        PlayerController m_Player;
        Damageable m_PlayerDamageable;
        CharacterController m_PlayerCharCtrl;
        AudioEngineFacade m_Engine;
        string m_LastMusicSpace;

        float SessionTime => Time.time - m_StartTime;

        void Awake()
        {
            Current = this;
            m_StartTime = Time.time;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (m_Player != null) m_Player.OnMeleeAttackStart -= OnMeleeAttackStart;
        }

        void Start()
        {
            m_Player = PlayerController.instance;
            if (m_Player == null)
            {
                Debug.LogWarning("GameplayLogger: PlayerController introuvable, logs de gameplay désactivés pour cette scène.");
                enabled = false;
                return;
            }

            m_PlayerDamageable = m_Player.GetComponent<Damageable>();
            m_PlayerCharCtrl = m_Player.GetComponent<CharacterController>();

            if (m_PlayerDamageable != null && !m_PlayerDamageable.onDamageMessageReceivers.Contains(this))
            {
                m_PlayerDamageable.onDamageMessageReceivers.Add(this);
            }

            foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsInactive.Include))
            {
                var damageable = enemy.GetComponent<Damageable>();
                if (damageable != null && !damageable.onDamageMessageReceivers.Contains(this))
                {
                    damageable.onDamageMessageReceivers.Add(this);
                }
            }

            foreach (var checkpoint in FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                var relay = checkpoint.gameObject.AddComponent<CheckpointRelay>();
                relay.checkpointId = checkpoint.gameObject.name;
                relay.player = m_Player.gameObject;
                relay.logger = this;
            }

            var audioEngineBehaviour = FindAnyObjectByType<AudioEngineBehaviour>();
            if (audioEngineBehaviour != null)
            {
                m_Engine = audioEngineBehaviour.Engine;
            }

            m_Player.OnMeleeAttackStart += OnMeleeAttackStart;
        }

        void Update()
        {
            if (m_Player == null) return;

            if (SessionTime - m_LastMovementSample >= MovementSampleInterval)
            {
                m_LastMovementSample = SessionTime;
                float speed = m_PlayerCharCtrl != null ? m_PlayerCharCtrl.velocity.magnitude : 0f;
                ExperimentManager.Instance.LogMovementSample(SessionTime, m_Player.transform.position, speed);
            }

            if (m_WasRespawning && !m_Player.respawning)
            {
                ExperimentManager.Instance.LogRespawn(SessionTime, m_LastCheckpointId);
            }
            m_WasRespawning = m_Player.respawning;

            if (m_Engine != null)
            {
                string current = m_Engine.GetCurrentSpace();
                if (!string.IsNullOrEmpty(current) && current != m_LastMusicSpace)
                {
                    if (!string.IsNullOrEmpty(m_LastMusicSpace))
                    {
                        ExperimentManager.Instance.LogMusicStateChange(SessionTime, m_LastMusicSpace, current, "unknown");
                    }
                    m_LastMusicSpace = current;
                }
            }
        }

        void OnMeleeAttackStart(bool throwing)
        {
            m_PendingAttackHit = false;
            StartCoroutine(ResolveAttackAfterDelay(throwing, SessionTime));
        }

        IEnumerator ResolveAttackAfterDelay(bool throwing, float sessionTimeAtStart)
        {
            yield return new WaitForSeconds(AttackHitWindowSeconds);
            ExperimentManager.Instance.LogAttack(sessionTimeAtStart, throwing ? "throw" : "melee", m_PendingAttackHit);
        }

        public void NotifyCheckpointReached(string checkpointId)
        {
            m_LastCheckpointId = checkpointId;
            float health = m_PlayerDamageable != null ? m_PlayerDamageable.currentHitPoints : -1f;
            ExperimentManager.Instance.LogCheckpoint(checkpointId, SessionTime, health);
        }

        public void NotifyLevelCompleted()
        {
            ExperimentManager.Instance.LogLevelCompleted(SessionTime, "succès");
        }

        public void OnReceiveMessage(MessageType type, object sender, object msg)
        {
            var damageable = sender as Damageable;
            if (damageable == null) return;

            var data = (Damageable.DamageMessage)msg;
            bool isPlayer = damageable == m_PlayerDamageable;

            if (type == MessageType.DAMAGED)
            {
                if (isPlayer)
                {
                    ExperimentManager.Instance.LogDamage(SessionTime, data.amount, GetDamagerId(data.damager), damageable.currentHitPoints);
                }
                else if (data.damager is MeleeWeapon)
                {
                    m_PendingAttackHit = true;
                }
            }
            else if (type == MessageType.DEAD)
            {
                if (isPlayer)
                {
                    m_DeathCountTotal++;
                    ExperimentManager.Instance.LogDeath(SessionTime, damageable.transform.position, m_DeathCountTotal);
                }
                else
                {
                    ExperimentManager.Instance.LogEnemyEliminated(SessionTime, GetEnemyType(damageable),
                        damageable.gameObject.GetInstanceID().ToString(), damageable.transform.position);
                }
            }
        }

        static string GetDamagerId(MonoBehaviour damager)
        {
            return damager != null ? damager.gameObject.name : "unknown";
        }

        static string GetEnemyType(Damageable damageable)
        {
            return damageable.gameObject.name.Replace("(Clone)", string.Empty).Trim();
        }

        class CheckpointRelay : MonoBehaviour
        {
            public string checkpointId;
            public GameObject player;
            public GameplayLogger logger;

            void OnTriggerEnter(Collider other)
            {
                if (other.gameObject == player)
                {
                    logger.NotifyCheckpointReached(checkpointId);
                }
            }
        }
    }
}
