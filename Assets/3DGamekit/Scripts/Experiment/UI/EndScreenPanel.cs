using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gamekit3D.Experiment
{
    public class EndScreenPanel : ExperimentPanelBase
    {
        public TextMeshProUGUI messageText;
        public TextMeshProUGUI pathText;
        public Button newSessionButton;
        public Button quitButton;

        Action m_OnNewSession;

        public void Open(string exportedPath, Action onNewSession)
        {
            m_OnNewSession = onNewSession;
            messageText.text = "Merci d'avoir participé à cette étude ! Vos données ont été enregistrées.";
            pathText.text = string.IsNullOrEmpty(exportedPath)
                ? "Attention : l'export des données a échoué."
                : "Fichier exporté :\n" + exportedPath;
            Show();
        }

        public void OnNewSessionClicked()
        {
            m_OnNewSession?.Invoke();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
