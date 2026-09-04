using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class StroopPanel : ExperimentPanelBase
    {
        [Serializable]
        public class ColorOption
        {
            public Button button;
            public string colorName;
            public Color color;
        }

        public TextMeshProUGUI instructionText;
        public TextMeshProUGUI stimulusText;
        public TextMeshProUGUI feedbackText;
        public List<ColorOption> colorOptions;

        public int practiceTrialCount = 8;
        public int measuredTrialCount = 20;

        [Tooltip("Probabilité qu'un essai soit congruent (mot et couleur du mot identiques). " +
            "Réduite volontairement : la majorité des essais sont incongruents.")]
        public float congruentProbability = 0.33f;

        Action<StroopResult> m_OnComplete;
        StroopResult m_Result;

        bool m_CurrentPhaseIsPractice;
        int m_CurrentPhaseCount;
        int m_CurrentPhaseTrial;
        int m_CurrentWordIdx;
        int m_CurrentInkIdx;
        int m_PreviousWordIdx = -1;
        int m_PreviousInkIdx = -1;
        float m_TrialStartTime;
        bool m_WaitingForResponse;

        const float PracticeDelay = 0.35f;
        const float MeasuredDelay = 0.12f;

        public void OnClickColor0() => OnColorClicked(0);
        public void OnClickColor1() => OnColorClicked(1);
        public void OnClickColor2() => OnColorClicked(2);
        public void OnClickColor3() => OnColorClicked(3);

        public void Open(bool includePractice, Action<StroopResult> onComplete)
        {
            m_OnComplete = onComplete;
            m_Result = new StroopResult();
            m_PreviousWordIdx = -1;
            m_PreviousInkIdx = -1;
            feedbackText.text = string.Empty;
            Show();

            if (includePractice)
            {
                StartPhase(true, practiceTrialCount);
            }
            else
            {
                StartPhase(false, measuredTrialCount);
            }
        }

        void StartPhase(bool practice, int count)
        {
            m_CurrentPhaseIsPractice = practice;
            m_CurrentPhaseCount = count;
            m_CurrentPhaseTrial = 0;
            instructionText.text = practice ? "Entraînement" : "Test";
            ShowNextTrial();
        }

        void ShowNextTrial()
        {
            if (m_CurrentPhaseTrial >= m_CurrentPhaseCount)
            {
                PhaseComplete();
                return;
            }

            int attempts = 0;
            do
            {
                m_CurrentWordIdx = UnityEngine.Random.Range(0, colorOptions.Count);
                bool congruent = UnityEngine.Random.value < congruentProbability;
                if (congruent)
                {
                    m_CurrentInkIdx = m_CurrentWordIdx;
                }
                else
                {
                    m_CurrentInkIdx = UnityEngine.Random.Range(0, colorOptions.Count - 1);
                    if (m_CurrentInkIdx >= m_CurrentWordIdx) m_CurrentInkIdx++;
                }
                attempts++;
            }
            while (attempts < 20 && m_CurrentWordIdx == m_PreviousWordIdx && m_CurrentInkIdx == m_PreviousInkIdx);

            m_PreviousWordIdx = m_CurrentWordIdx;
            m_PreviousInkIdx = m_CurrentInkIdx;

            stimulusText.text = colorOptions[m_CurrentWordIdx].colorName.ToUpperInvariant();
            stimulusText.color = colorOptions[m_CurrentInkIdx].color;
            feedbackText.text = string.Empty;
            m_TrialStartTime = Time.realtimeSinceStartup;
            m_WaitingForResponse = true;
        }

        void OnColorClicked(int idx)
        {
            if (!m_WaitingForResponse) return;
            m_WaitingForResponse = false;

            float rtMs = (Time.realtimeSinceStartup - m_TrialStartTime) * 1000f;
            bool correct = idx == m_CurrentInkIdx;

            m_Result.trials.Add(new StroopTrial
            {
                index = m_Result.trials.Count,
                word = colorOptions[m_CurrentWordIdx].colorName,
                inkColor = colorOptions[m_CurrentInkIdx].colorName,
                clickedColor = colorOptions[idx].colorName,
                correct = correct,
                rt_ms = rtMs,
                isPractice = m_CurrentPhaseIsPractice,
            });

            m_CurrentPhaseTrial++;

            if (m_CurrentPhaseIsPractice)
            {
                feedbackText.text = correct ? "Correct" : "Incorrect";
            }

            StartCoroutine(NextTrialAfterDelay(m_CurrentPhaseIsPractice ? PracticeDelay : MeasuredDelay));
        }

        IEnumerator NextTrialAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            ShowNextTrial();
        }

        void PhaseComplete()
        {
            if (m_CurrentPhaseIsPractice)
            {
                StartPhase(false, measuredTrialCount);
            }
            else
            {
                FinishAll();
            }
        }

        void FinishAll()
        {
            var measured = m_Result.trials.Where(t => !t.isPractice).ToList();
            m_Result.errorCount = measured.Count(t => !t.correct);
            m_Result.errorRate = measured.Count > 0 ? (float)m_Result.errorCount / measured.Count * 100f : 0f;
            m_Result.meanRtMs = measured.Count > 0 ? measured.Average(t => t.rt_ms) : 0f;

            Hide();
            m_OnComplete?.Invoke(m_Result);
        }
    }
}
