using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamekit3D.Experiment
{
    public enum QuestionType
    {
        SingleChoice,
        MultiChoice,
        OpenText,
        Numeric,
        Likert7,
        Likert10,

        // Text-only, non-answerable — displays instructions between groups of items (e.g. the
        // GEMS exploration/combat phase instructions) without producing an exported answer.
        SectionHeader,
    }

    [Serializable]
    public class QuestionItem
    {
        public string id;
        [TextArea(1, 3)]
        public string text;
        public QuestionType type;
        public string[] options;
        public bool required = true;

        [Tooltip("Analyse uniquement : à inverser (1<->7, 2<->6, etc.) au moment du scoring, comme spécifié par l'instrument (ex. items IEQ 6, 8, 9, 10, 18, 20).")]
        public bool reverseScored;
    }

    [CreateAssetMenu(fileName = "Questionnaire", menuName = "SpaceMusicEngine Demo/Questionnaire Definition")]
    public class QuestionnaireDefinition : ScriptableObject
    {
        public string title;
        public List<QuestionItem> items = new List<QuestionItem>();
    }
}
