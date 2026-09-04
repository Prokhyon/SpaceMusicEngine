using System;
using TMPro;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class InfoPanel : ExperimentPanelBase
    {
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI bodyText;
        public Button continueButton;

        Action m_OnContinue;

        public void Open(string title, string body, Action onContinue)
        {
            m_OnContinue = onContinue;
            titleText.text = title;
            bodyText.text = body;
            Show();
        }

        public void Continue()
        {
            Hide();
            m_OnContinue?.Invoke();
        }
    }
}
