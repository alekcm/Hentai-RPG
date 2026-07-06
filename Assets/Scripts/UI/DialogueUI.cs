using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using RPG.Core;
using RPG.Dialogue;
using RPG.Character;

namespace RPG.UI
{
    /// <summary>
    /// UI контроллер для диалоговой системы. Отображает текст, выборы,
    /// результаты проверок навыков, анимации текста.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject dialoguePanel;
        [SerializeField] private GameObject choicesPanel;
        [SerializeField] private GameObject skillCheckPanel;

        [Header("Text Elements")]
        [SerializeField] private Text speakerNameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text skillCheckResultText;

        [Header("Portraits")]
        [SerializeField] private Image speakerPortrait;
        [SerializeField] private Image playerPortrait;

        [Header("Choice Template")]
        [SerializeField] private GameObject choiceButtonPrefab;

        [Header("Animation")]
        [SerializeField] private float textSpeed = 0.03f;
        [SerializeField] private float choiceFadeInTime = 0.2f;

        [Header("Colors")]
        [SerializeField] private Color availableChoiceColor = Color.white;
        [SerializeField] private Color unavailableChoiceColor = Color.gray;
        [SerializeField] private Color skillCheckColor = new Color(1f, 0.8f, 0.2f);
        [SerializeField] private Color nsfwChoiceColor = new Color(1f, 0.4f, 0.6f);
        [SerializeField] private Color hiddenChoiceColor = new Color(0.4f, 0.8f, 1f);

        private bool isTyping;
        private string fullText;
        private int charIndex;
        private List<GameObject> activeChoiceButtons = new();
        private DialogueManager dialogueManager;

        private void Start()
        {
            dialogueManager = DialogueManager.Instance;
            if (dialogueManager == null)
            {
                Debug.LogError("[DialogueUI] DialogueManager not found!");
                return;
            }

            dialogueManager.OnNodePresented += OnNodePresented;
            dialogueManager.OnChoiceMade += OnChoiceMade;
            dialogueManager.OnSkillCheckPerformed += OnSkillCheckPerformed;
            dialogueManager.OnDialogueStarted += OnDialogueStarted;
            dialogueManager.OnDialogueEnded += OnDialogueEnded;

            dialoguePanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (dialogueManager != null)
            {
                dialogueManager.OnNodePresented -= OnNodePresented;
                dialogueManager.OnChoiceMade -= OnChoiceMade;
                dialogueManager.OnSkillCheckPerformed -= OnSkillCheckPerformed;
                dialogueManager.OnDialogueStarted -= OnDialogueStarted;
                dialogueManager.OnDialogueEnded -= OnDialogueEnded;
            }
        }

        #region Event Handlers

        private void OnDialogueStarted()
        {
            dialoguePanel.SetActive(true);
        }

        private void OnDialogueEnded()
        {
            dialoguePanel.SetActive(false);
            ClearChoices();
        }

        private void OnNodePresented(DialogueNode node)
        {
            ClearChoices();

            // Устанавливаем имя говорящего
            switch (node.speakerType)
            {
                case DialogueSpeakerType.NPC:
                    speakerNameText.text = dialogueManager.CurrentDialogue?.speakerName ?? "";
                    break;
                case DialogueSpeakerType.Player:
                    speakerNameText.text = CharacterCreation.Instance?.Character?.playerName ?? "Вы";
                    break;
                case DialogueSpeakerType.Narrator:
                    speakerNameText.text = "";
                    break;
            }

            // Запускаем печать текста
            StartCoroutine(TypeText(node.text));

            // Показываем выборы (после завершения печати)
            StartCoroutine(ShowChoicesAfterTyping(node));
        }

        private void OnChoiceMade(DialogueChoice choice)
        {
            // Визуальный фидбек выбора
            StartCoroutine(FlashChoice(choice));
        }

        private void OnSkillCheckPerformed(SkillCheckResult result)
        {
            ShowSkillCheckResult(result);
        }

        #endregion

        #region Text Animation

        private IEnumerator TypeText(string text)
        {
            isTyping = true;
            fullText = text;
            charIndex = 0;
            dialogueText.text = "";

            while (charIndex < fullText.Length)
            {
                dialogueText.text += fullText[charIndex];
                charIndex++;
                yield return new WaitForSeconds(textSpeed);
            }

            isTyping = false;
        }

        public void SkipTextAnimation()
        {
            if (isTyping)
            {
                StopAllCoroutines();
                dialogueText.text = fullText;
                isTyping = false;

                // Показываем выборы сразу
                if (dialogueManager.CurrentNode != null)
                    ShowChoices(dialogueManager.CurrentNode);
            }
        }

        private IEnumerator ShowChoicesAfterTyping(DialogueNode node)
        {
            while (isTyping)
                yield return null;

            yield return new WaitForSeconds(0.1f);
            ShowChoices(node);
        }

        #endregion

        #region Choices

        private void ShowChoices(DialogueNode node)
        {
            ClearChoices();

            if (node.choices == null || node.choices.Count == 0)
                return;

            choicesPanel.SetActive(true);

            foreach (var choice in node.choices)
            {
                if (!dialogueManager.IsChoiceVisible(choice))
                    continue;

                var buttonObj = Instantiate(choiceButtonPrefab, choicesPanel.transform);
                var buttonText = buttonObj.GetComponentInChildren<Text>();
                var button = buttonObj.GetComponent<Button>();

                bool isAvailable = dialogueManager.IsChoiceAvailable(choice);

                // Форматируем текст
                string displayText = choice.text;
                Color textColor = availableChoiceColor;

                // Проверка навыка
                if (choice.skillCheck != null && choice.skillCheck.HasCheck)
                {
                    var player = CharacterCreation.Instance?.Character;
                    int bonus = player?.stats.GetSkillBonus(choice.skillCheck.skillType) ?? 0;
                    displayText = $"[{choice.skillCheck.skillType} SL{choice.skillCheck.difficultyClass}] " +
                                  $"{choice.text} <color=#888888>(+{bonus})</color>";
                    textColor = skillCheckColor;
                }

                // NSFW вариант
                if (choice.isNSFWChoice)
                    textColor = nsfwChoiceColor;

                // LLM-разблокированный
                if (choice.llmUnlockCondition != null && !choice.isHiddenByDefault)
                    textColor = hiddenChoiceColor;

                if (!isAvailable)
                {
                    textColor = unavailableChoiceColor;
                    string reason = dialogueManager.GetUnavailableReason(choice);
                    displayText += $"\n<color=#666666><i>{reason}</i></color>";
                    button.interactable = false;
                }
                else
                {
                    // Привязываем обработчик клика
                    var capturedChoice = choice;
                    int choiceIndex = node.choices.IndexOf(choice);
                    button.onClick.AddListener(() =>
                    {
                        dialogueManager.MakeChoice(choiceIndex);
                    });
                }

                buttonText.text = displayText;
                buttonText.color = textColor;
                activeChoiceButtons.Add(buttonObj);

                // Анимация появления
                var canvasGroup = buttonObj.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = buttonObj.AddComponent<CanvasGroup>();
                StartCoroutine(FadeIn(canvasGroup));
            }
        }

        private void ClearChoices()
        {
            foreach (var button in activeChoiceButtons)
            {
                if (button != null)
                    Destroy(button);
            }
            activeChoiceButtons.Clear();
            choicesPanel.SetActive(false);
        }

        private IEnumerator FadeIn(CanvasGroup group)
        {
            group.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < choiceFadeInTime)
            {
                elapsed += Time.deltaTime;
                group.alpha = elapsed / choiceFadeInTime;
                yield return null;
            }
            group.alpha = 1f;
        }

        private IEnumerator FlashChoice(DialogueChoice choice)
        {
            // Визуальный эффект при выборе
            yield return new WaitForSeconds(0.2f);
        }

        #endregion

        #region Skill Check Display

        private void ShowSkillCheckResult(SkillCheckResult result)
        {
            skillCheckPanel.SetActive(true);

            string resultText;
            Color color;

            if (result.isCriticalSuccess)
            {
                resultText = $"КРИТИЧЕСКИЙ УСПЕХ!\n{result.skillType}: " +
                            $"{result.roll} (кубик) + {result.bonus} (бонус) = " +
                            $"{result.total} vs SL {result.difficultyClass}";
                color = new Color(1f, 0.85f, 0f);
            }
            else if (result.isCriticalFailure)
            {
                resultText = $"КРИТИЧЕСКИЙ ПРОВАЛ!\n{result.skillType}: " +
                            $"{result.roll} (кубик) + {result.bonus} (бонус) = " +
                            $"{result.total} vs SL {result.difficultyClass}";
                color = new Color(0.8f, 0.1f, 0.1f);
            }
            else if (result.success)
            {
                resultText = $"УСПЕХ!\n{result.skillType}: " +
                            $"{result.roll} + {result.bonus} = " +
                            $"{result.total} vs SL {result.difficultyClass}";
                color = new Color(0.2f, 0.8f, 0.2f);
            }
            else
            {
                resultText = $"ПРОВАЛ\n{result.skillType}: " +
                            $"{result.roll} + {result.bonus} = " +
                            $"{result.total} vs SL {result.difficultyClass}";
                color = new Color(0.8f, 0.3f, 0.3f);
            }

            skillCheckResultText.text = resultText;
            skillCheckResultText.color = color;

            StartCoroutine(HideSkillCheckAfterDelay(2f));
        }

        private IEnumerator HideSkillCheckAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            skillCheckPanel.SetActive(false);
        }

        #endregion

        #region Input

        private void Update()
        {
            if (!dialoguePanel.activeSelf) return;

            // Клик/пробел для продвижения
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                if (isTyping)
                {
                    SkipTextAnimation();
                }
                else if (dialogueManager.CurrentNode != null &&
                         dialogueManager.CurrentNode.choices.Count == 0)
                {
                    dialogueManager.Advance();
                }
            }

            // Цифры для быстрого выбора
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    if (i < dialogueManager.CurrentNode?.choices.Count)
                    {
                        var choice = dialogueManager.CurrentNode.choices[i];
                        if (dialogueManager.IsChoiceAvailable(choice))
                            dialogueManager.MakeChoice(i);
                    }
                }
            }
        }

        #endregion
    }
}
