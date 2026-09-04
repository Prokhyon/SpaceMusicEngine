using UnityEngine;

namespace Gamekit3D.Experiment
{
    // Sits on the same GameObject as the level's exit TransitionPoint (which itself now points to
    // the neutral Interstitial scene). Purely observational: logs "level completed" with an accurate
    // session_time_s, without touching the actual scene transition.
    [RequireComponent(typeof(Collider))]
    public class LevelCompletionLogger : MonoBehaviour
    {
        public GameObject transitioningGameObject;

        bool m_Fired;

        void OnTriggerEnter(Collider other)
        {
            if (m_Fired) return;
            if (other.gameObject != transitioningGameObject) return;

            m_Fired = true;
            GameplayLogger.Current?.NotifyLevelCompleted();
        }
    }
}
