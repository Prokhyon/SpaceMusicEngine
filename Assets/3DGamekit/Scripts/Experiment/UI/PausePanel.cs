using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class PausePanel : ExperimentPanelBase
    {
        public TextMeshProUGUI timerText;
        public Button continueButton;

        public float minSeconds = 300f;
        public float maxSeconds = 600f;

        Action m_OnContinue;
        float m_Elapsed;
        bool m_Running;

        public void Open(Action onContinue)
        {
            m_OnContinue = onContinue;
            m_Elapsed = 0f;
            m_Running = true;
            // TODO: re-enable the minimum-wait gate (continueButton.interactable = false until
            // minSeconds elapses) once testing is done — left always clickable for now.
            continueButton.interactable = true;
            UpdateTimerText();
            Show();
        }

        void Update()
        {
            if (!m_Running) return;

            m_Elapsed += Time.unscaledDeltaTime;
            UpdateTimerText();

            if (m_Elapsed >= maxSeconds)
            {
                OnContinueClicked();
            }
        }

        void UpdateTimerText()
        {
            int remaining = Mathf.Max(0, Mathf.CeilToInt(maxSeconds - m_Elapsed));
            var span = TimeSpan.FromSeconds(remaining);
            timerText.text = string.Format("Pause — reprise possible dans {0:D2}:{1:D2}", span.Minutes, span.Seconds);
        }

        public void OnContinueClicked()
        {
            if (!m_Running) return;
            m_Running = false;
            Hide();
            m_OnContinue?.Invoke();
        }
    }
}
