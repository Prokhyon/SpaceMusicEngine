using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public class QuestionnairePanel : ExperimentPanelBase
    {
        public TextMeshProUGUI titleText;
        public RectTransform contentContainer;
        public ScrollRect scrollRect;
        public Button continueButton;
        public TextMeshProUGUI validationText;

        class ItemWidget
        {
            public QuestionItem item;
            public Func<string> getAnswer;
        }

        readonly List<ItemWidget> m_Widgets = new List<ItemWidget>();
        Action<List<QuestionAnswer>> m_OnSubmit;

        public void Open(QuestionnaireDefinition definition, Action<List<QuestionAnswer>> onSubmit)
        {
            m_OnSubmit = onSubmit;
            titleText.text = definition.title;
            validationText.text = string.Empty;
            m_Widgets.Clear();

            for (int i = contentContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(contentContainer.GetChild(i).gameObject);
            }

            foreach (var item in definition.items)
            {
                BuildItem(item);
            }

            Show();

            // The content is rebuilt every time this panel opens; without forcing a layout pass
            // first, the ScrollRect resets its position based on stale (or not-yet-computed)
            // content height and lands somewhere near the bottom instead of at the first question.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer);
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        void BuildItem(QuestionItem item)
        {
            var block = UIFactory.CreateRect(contentContainer, "Question - " + item.id);
            UIFactory.AddVerticalLayout(block.gameObject, 10);
            var fitter = block.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (item.type == QuestionType.SectionHeader)
            {
                // Instruction text between groups of items (e.g. GEMS phase instructions) — no
                // answer widget, not added to m_Widgets, so it's excluded from validation/export.
                var headerText = UIFactory.CreateText(block, item.text, 22, TextAlignmentOptions.TopLeft,
                    new Color(0.55f, 0.8f, 0.95f), autoSize: true);
                headerText.fontStyle = FontStyles.Italic;
                return;
            }

            UIFactory.CreateText(block, item.text, 24, TextAlignmentOptions.TopLeft, autoSize: true);

            Func<string> getAnswer;

            switch (item.type)
            {
                case QuestionType.SingleChoice:
                    getAnswer = BuildChoiceList(block, item.options, exclusive: true);
                    break;
                case QuestionType.MultiChoice:
                    getAnswer = BuildChoiceList(block, item.options, exclusive: false);
                    break;
                case QuestionType.Likert7:
                    getAnswer = BuildLikert(block, 7);
                    break;
                case QuestionType.Likert10:
                    getAnswer = BuildLikert(block, 10);
                    break;
                case QuestionType.Numeric:
                    {
                        var input = UIFactory.CreateInputField(block, TMP_InputField.ContentType.IntegerNumber);
                        UIFactory.AddLayoutElement(input.gameObject, 50, 50);
                        getAnswer = () => input.text;
                        break;
                    }
                case QuestionType.OpenText:
                default:
                    {
                        var input = UIFactory.CreateInputField(block, TMP_InputField.ContentType.Standard, true);
                        UIFactory.AddLayoutElement(input.gameObject, 100, 100);
                        getAnswer = () => input.text;
                        break;
                    }
            }

            m_Widgets.Add(new ItemWidget { item = item, getAnswer = getAnswer });
        }

        Func<string> BuildChoiceList(RectTransform parent, string[] options, bool exclusive)
        {
            var listRoot = UIFactory.CreateRect(parent, "Options");
            UIFactory.AddVerticalLayout(listRoot.gameObject, 4);
            var fitter = listRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ToggleGroup group = null;
            if (exclusive)
            {
                group = listRoot.gameObject.AddComponent<ToggleGroup>();
                group.allowSwitchOff = true;
            }

            var toggles = new List<(Toggle toggle, string label)>();
            if (options != null)
            {
                foreach (var option in options)
                {
                    var toggle = UIFactory.CreateToggle(listRoot, option, group, out _);
                    toggles.Add((toggle, option));
                }
            }

            return () => string.Join("; ", toggles.Where(t => t.toggle.isOn).Select(t => t.label));
        }

        Func<string> BuildLikert(RectTransform parent, int scaleMax)
        {
            var row = UIFactory.CreateRect(parent, "Likert");
            UIFactory.AddHorizontalLayout(row.gameObject, 60);
            UIFactory.AddLayoutElement(row.gameObject, 56, 56);

            var group = row.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = true;

            float toggleWidth = scaleMax <= 7 ? 72f : 58f;

            var toggles = new List<(Toggle toggle, string label)>();
            for (int i = 1; i <= scaleMax; i++)
            {
                var label = i.ToString();
                var toggle = UIFactory.CreateToggle(row, label, group, out var text);
                text.alignment = TextAlignmentOptions.Center;
                UIFactory.SetPreferredSize(toggle.gameObject, toggleWidth, 44);
                toggles.Add((toggle, label));
            }

            return () =>
            {
                foreach (var t in toggles)
                {
                    if (t.toggle.isOn) return t.label;
                }
                return string.Empty;
            };
        }

        public void TrySubmit()
        {
            var missing = m_Widgets.Where(w => w.item.required && string.IsNullOrEmpty(w.getAnswer()));
            if (missing.Any())
            {
                validationText.text = "Merci de répondre à toutes les questions requises avant de continuer.";
                return;
            }

            var answers = m_Widgets
                .Select(w => new QuestionAnswer(w.item.id, w.item.text, w.getAnswer()))
                .ToList();

            Hide();
            m_OnSubmit?.Invoke(answers);
        }
    }
}
