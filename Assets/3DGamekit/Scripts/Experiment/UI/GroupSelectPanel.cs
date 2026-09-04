using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class GroupSelectPanel : ExperimentPanelBase
    {
        public TMP_InputField participantIdInput;
        public Button groupAButton;
        public Button groupBButton;
        public Button startButton;
        public TextMeshProUGUI feedbackText;

        static readonly Color SelectedColor = new Color(0.35f, 0.75f, 0.95f, 1f);
        static readonly Color UnselectedColor = new Color(0.25f, 0.25f, 0.3f, 1f);

        Action<string, ExperimentGroup> m_OnStart;
        ExperimentGroup? m_SelectedGroup;

        public void Open(Action<string, ExperimentGroup> onStart)
        {
            m_OnStart = onStart;
            m_SelectedGroup = null;
            participantIdInput.text = string.Empty;
            feedbackText.text = string.Empty;
            SetButtonBaseColor(groupAButton, UnselectedColor);
            SetButtonBaseColor(groupBButton, UnselectedColor);
            Show();
        }

        public void SelectGroupA()
        {
            SelectGroup(ExperimentGroup.A);
        }

        public void SelectGroupB()
        {
            SelectGroup(ExperimentGroup.B);
        }

        void SelectGroup(ExperimentGroup group)
        {
            m_SelectedGroup = group;
            SetButtonBaseColor(groupAButton, group == ExperimentGroup.A ? SelectedColor : UnselectedColor);
            SetButtonBaseColor(groupBButton, group == ExperimentGroup.B ? SelectedColor : UnselectedColor);
        }

        static void SetButtonBaseColor(Button button, Color color)
        {
            UIFactory.ApplyColorBlock(button, color);
            button.image.color = color;
        }

        public void TryStart()
        {
            if (string.IsNullOrWhiteSpace(participantIdInput.text))
            {
                feedbackText.text = "Merci de saisir un identifiant participant.";
                return;
            }

            if (m_SelectedGroup == null)
            {
                feedbackText.text = "Merci de sélectionner un groupe (A ou B).";
                return;
            }

            var participantId = participantIdInput.text.Trim();
            var group = m_SelectedGroup.Value;
            Hide();
            m_OnStart?.Invoke(participantId, group);
        }
    }
}
