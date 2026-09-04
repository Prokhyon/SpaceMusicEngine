using System;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    // Sits on top of the existing 3D Game Kit "Start" title scene: just a single button inviting
    // the experimenter/participant into the study, without hiding the scene behind it.
    public class StartOverlayPanel : ExperimentPanelBase
    {
        public Button startButton;

        Action m_OnStart;

        public void Open(Action onStart)
        {
            m_OnStart = onStart;
            Show();
        }

        public void OnStartClicked()
        {
            Hide();
            m_OnStart?.Invoke();
        }
    }
}
