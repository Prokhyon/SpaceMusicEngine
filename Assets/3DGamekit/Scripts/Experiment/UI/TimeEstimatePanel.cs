using System;
using System.Globalization;
using TMPro;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class TimeEstimatePanel : ExperimentPanelBase
    {
        public TMP_InputField minutesInput;
        public Button submitButton;
        public TextMeshProUGUI validationText;

        Action<float> m_OnSubmit;

        public void Open(Action<float> onSubmit)
        {
            m_OnSubmit = onSubmit;
            minutesInput.text = string.Empty;
            validationText.text = string.Empty;
            Show();
        }

        public void TrySubmit()
        {
            if (!float.TryParse(minutesInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
                && !float.TryParse(minutesInput.text, out minutes))
            {
                validationText.text = "Merci d'indiquer une estimation en minutes (ex. 12).";
                return;
            }

            Hide();
            m_OnSubmit?.Invoke(minutes);
        }
    }
}
