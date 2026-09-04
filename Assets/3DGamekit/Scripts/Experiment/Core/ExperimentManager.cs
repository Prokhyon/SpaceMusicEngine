using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Gamekit3D.Experiment
{
    public class ExperimentManager : MonoBehaviour
    {
        public static ExperimentManager Instance { get; private set; }

        const string StartSceneName = "Start";
        const string InterstitialSceneName = "Interstitial";
        const string SceneLevel1 = "Level1";
        const string SceneLevel1Static = "Level1_Static";
        const string SceneLevel2 = "Level2";
        const string SceneLevel2Static = "Level2_Static";

        public GroupSelectPanel groupSelectPanel;
        public QuestionnairePanel questionnairePanel;
        public StroopPanel stroopPanel;
        public TimeEstimatePanel timeEstimatePanel;
        public PausePanel pausePanel;
        public EndScreenPanel endScreenPanel;
        public InfoPanel infoPanel;
        public StartOverlayPanel startOverlayPanel;

        public QuestionnaireDefinition preQuestionnaire;
        public QuestionnaireDefinition emotionQuestionnaire;
        public QuestionnaireDefinition ieqQuestionnaire;
        public QuestionnaireDefinition finalQuestionnaire;

        ExperimentSession m_Session;
        ExperimentGroup m_Group;
        int m_CurrentSessionIndex;
        bool m_AwaitingLevelCompletion;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Standalone builds used to default to the "Balanced" quality tier (shadowDistance 30,
            // lodBias 0.5) regardless of what's tested in the Editor, causing visible pop-in/short
            // draw distance for shadows, LODs and textures a few meters out. Force the best tier
            // ("Fantastic", index 2) explicitly so this can't silently regress again.
            QualitySettings.SetQualityLevel(2, true);

#if !UNITY_EDITOR
            // "Default is native resolution" only applies at first launch; a resolution cached from
            // a previous run (e.g. on a laptop's non-16:9 screen) otherwise persists when the build
            // is later run on a different monitor, producing pillarboxing. Force it every boot.
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
#endif
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Defensive cleanup: if any scene ever brings in its own EventSystem, it would collide
            // with the persistent one living under this DontDestroyOnLoad root and silently break
            // all UI input. Only ours should ever remain.
            var ourEventSystem = GetComponentInChildren<EventSystem>(true);
            var allEventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var es in allEventSystems)
            {
                if (es != ourEventSystem)
                {
                    Debug.LogWarning("ExperimentManager: EventSystem en trop détecté sur " + es.gameObject.name + ", suppression.");
                    Destroy(es.gameObject);
                }
            }

            // Gameplay locks and hides the cursor (see StartUI) for mouse-look. Every non-gameplay
            // scene (the UI-only Experiment scene, and the neutral Interstitial scene between
            // levels) must explicitly restore it, since Unity never resets cursor state on its own
            // across scene loads — otherwise the participant is stuck unable to click any button.
            bool isGameplayLevel = scene.name == SceneLevel1 || scene.name == SceneLevel1Static ||
                scene.name == SceneLevel2 || scene.name == SceneLevel2Static;
            if (!isGameplayLevel)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // The level's own exit portal (a native TransitionPoint, redirected to point here)
            // is what actually detects "player reached the end of the level" — not a custom
            // trigger of ours. We just react once we land back in the neutral interstitial scene.
            if (scene.name == InterstitialSceneName)
            {
                if (m_AwaitingLevelCompletion)
                {
                    m_AwaitingLevelCompletion = false;
                    HandleLevelFinished();
                }
                else
                {
                    // Reached here outside the controlled protocol (e.g. a level played directly via
                    // the debug level-select on the Start screen). Nothing else knows what to do
                    // next, so return to the start screen instead of leaving a black dead end.
                    SceneManager.LoadScene(StartSceneName, LoadSceneMode.Single);
                }
            }
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            m_Session = new ExperimentSession();
            startOverlayPanel.Open(ShowGroupSelect);
        }

        void ShowGroupSelect()
        {
            groupSelectPanel.Open(OnGroupSelected);
        }

        void OnGroupSelected(string participantId, ExperimentGroup group)
        {
            m_Session.participant_id = participantId;
            m_Session.group = group.ToString();
            m_Group = group;
            questionnairePanel.Open(preQuestionnaire, OnPreQuestionnaireSubmitted);
        }

        void OnPreQuestionnaireSubmitted(System.Collections.Generic.List<QuestionAnswer> answers)
        {
            m_Session.pre_questionnaire = answers;
            infoPanel.Open(
                "Test de concentration",
                "Vous allez maintenant réaliser un court test de Stroop. Un mot de couleur s'affiche dans une couleur qui peut être différente du mot lui-même. Cliquez le plus vite possible sur la couleur du mot, pas sur le mot. Un bref entraînement précède le test proprement dit.",
                StartStroopBaseline);
        }

        void StartStroopBaseline()
        {
            stroopPanel.Open(true, OnStroopBaselineComplete);
        }

        void OnStroopBaselineComplete(StroopResult result)
        {
            m_Session.stroop_baseline = result;
            infoPanel.Open(
                "Niveau 1",
                "Le test de concentration est terminé. Vous allez maintenant jouer un niveau du jeu. Jouez normalement : vous serez automatiquement arrêté à la fin du niveau.",
                () => LoadLevel(1));
        }

        void LoadLevel(int sessionIndex)
        {
            m_CurrentSessionIndex = sessionIndex;

            var block = CurrentBlock();
            block.level_id = sessionIndex == 1 ? "Level1" : "Level2";
            block.music_condition = GetMusicConditionForSession(sessionIndex).ToString();
            block.session_start_datetime = DateTime.Now.ToString("o");
            block.gameplay_log = new SessionLog
            {
                participant_id = m_Session.participant_id,
                session_number = sessionIndex,
                level_id = block.level_id,
                music_condition = block.music_condition,
                group = m_Session.group,
                session_start_datetime = block.session_start_datetime,
            };

            m_AwaitingLevelCompletion = true;
            SceneManager.LoadScene(GetSceneNameForSession(sessionIndex), LoadSceneMode.Single);
        }

        string GetSceneNameForSession(int sessionIndex)
        {
            if (sessionIndex == 1)
                return m_Group == ExperimentGroup.A ? SceneLevel1 : SceneLevel1Static;
            return m_Group == ExperimentGroup.A ? SceneLevel2Static : SceneLevel2;
        }

        MusicCondition GetMusicConditionForSession(int sessionIndex)
        {
            if (sessionIndex == 1)
                return m_Group == ExperimentGroup.A ? MusicCondition.Adaptive : MusicCondition.Static;
            return m_Group == ExperimentGroup.A ? MusicCondition.Static : MusicCondition.Adaptive;
        }

        SessionBlock CurrentBlock()
        {
            return m_CurrentSessionIndex == 1 ? m_Session.session_1 : m_Session.session_2;
        }

        void HandleLevelFinished()
        {
            CurrentBlock().session_end_datetime = DateTime.Now.ToString("o");

            infoPanel.Open(
                "Niveau terminé",
                "Bravo, vous avez terminé ce niveau ! Nous allons maintenant vous poser quelques questions, à commencer par un nouveau test de Stroop (sans entraînement cette fois).",
                StartPostStroop);
        }

        void StartPostStroop()
        {
            stroopPanel.Open(false, OnPostStroopComplete);
        }

        void OnPostStroopComplete(StroopResult result)
        {
            CurrentBlock().stroop_post = result;
            timeEstimatePanel.Open(OnTimeEstimateSubmitted);
        }

        void OnTimeEstimateSubmitted(float minutes)
        {
            CurrentBlock().time_estimate_minutes = minutes;
            questionnairePanel.Open(emotionQuestionnaire, OnEmotionSubmitted);
        }

        void OnEmotionSubmitted(System.Collections.Generic.List<QuestionAnswer> answers)
        {
            CurrentBlock().emotion = answers;
            questionnairePanel.Open(ieqQuestionnaire, OnIEQSubmitted);
        }

        void OnIEQSubmitted(System.Collections.Generic.List<QuestionAnswer> answers)
        {
            CurrentBlock().ieq = answers;

            if (m_CurrentSessionIndex == 1)
            {
                pausePanel.Open(OnPauseContinue);
            }
            else
            {
                questionnairePanel.Open(finalQuestionnaire, OnFinalQuestionsSubmitted);
            }
        }

        void OnPauseContinue()
        {
            infoPanel.Open(
                "Niveau 2",
                "La pause est terminée. Vous allez maintenant jouer le second niveau du jeu.",
                () => LoadLevel(2));
        }

        void OnFinalQuestionsSubmitted(System.Collections.Generic.List<QuestionAnswer> answers)
        {
            m_Session.final_questionnaire = answers;
            m_Session.export_datetime = DateTime.Now.ToString("o");

            string path = ExperimentDataExporter.Export(m_Session);
            endScreenPanel.Open(path, RestartForNewParticipant);
        }

        void RestartForNewParticipant()
        {
            Instance = null;
            Destroy(gameObject);
            SceneManager.LoadScene(StartSceneName, LoadSceneMode.Single);
        }

        // ---------------------------------------------------------------
        // Gameplay log API — called by GameplayLogger/LevelCompletionLogger living inside the
        // currently loaded level scene. This class owns the actual storage (CurrentBlock().gameplay_log)
        // since it survives scene unloads and the level-scene components don't.
        // ---------------------------------------------------------------

        public void LogCheckpoint(string checkpointId, float sessionTimeS, float healthAtCheckpoint)
        {
            CurrentBlock().gameplay_log?.checkpoints.Add(new CheckpointEvent
            {
                checkpoint_id = checkpointId,
                session_time_s = sessionTimeS,
                player_health_at_checkpoint = healthAtCheckpoint,
            });
        }

        public void LogLevelCompleted(float sessionTimeS, string completionStatus)
        {
            var log = CurrentBlock().gameplay_log;
            if (log == null) return;
            log.level_completed = new LevelCompletedEvent { session_time_s = sessionTimeS, completion_status = completionStatus };
            log.session_duration_s = sessionTimeS;
        }

        public void LogDamage(float sessionTimeS, float amount, string sourceEnemyId, float healthAfter)
        {
            CurrentBlock().gameplay_log?.damage_events.Add(new DamageEvent
            {
                session_time_s = sessionTimeS,
                damage_amount = amount,
                source_enemy_id = sourceEnemyId,
                player_health_after = healthAfter,
            });
        }

        public void LogDeath(float sessionTimeS, Vector3 pos, int deathCountTotal)
        {
            CurrentBlock().gameplay_log?.deaths.Add(new DeathEvent
            {
                session_time_s = sessionTimeS,
                pos_x = pos.x,
                pos_y = pos.y,
                pos_z = pos.z,
                death_count_total = deathCountTotal,
            });
        }

        public void LogRespawn(float sessionTimeS, string respawnCheckpointId)
        {
            CurrentBlock().gameplay_log?.respawns.Add(new RespawnEvent
            {
                session_time_s = sessionTimeS,
                respawn_checkpoint_id = respawnCheckpointId,
            });
        }

        public void LogAttack(float sessionTimeS, string attackType, bool hitSuccess)
        {
            CurrentBlock().gameplay_log?.attacks.Add(new AttackEvent
            {
                session_time_s = sessionTimeS,
                attack_type = attackType,
                hit_success = hitSuccess,
            });
        }

        public void LogEnemyEliminated(float sessionTimeS, string enemyType, string enemyId, Vector3 pos)
        {
            CurrentBlock().gameplay_log?.enemies_eliminated.Add(new EnemyEliminatedEvent
            {
                session_time_s = sessionTimeS,
                enemy_type = enemyType,
                enemy_id = enemyId,
                pos_x = pos.x,
                pos_y = pos.y,
                pos_z = pos.z,
            });
        }

        public void LogMovementSample(float sessionTimeS, Vector3 pos, float velocityMagnitude)
        {
            CurrentBlock().gameplay_log?.movement_samples.Add(new MovementSample
            {
                session_time_s = sessionTimeS,
                pos_x = pos.x,
                pos_y = pos.y,
                pos_z = pos.z,
                velocity_magnitude = velocityMagnitude,
            });
        }

        public void LogMusicStateChange(float sessionTimeS, string previousState, string newState, string triggerType)
        {
            CurrentBlock().gameplay_log?.music_state_changes.Add(new MusicStateChangeEvent
            {
                session_time_s = sessionTimeS,
                previous_state = previousState,
                new_state = newState,
                trigger_type = triggerType,
            });
        }
    }
}
