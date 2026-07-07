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
        private Coroutine typingCoroutine;
        private Coroutine showChoicesCoroutine;
        private Coroutine skillCheckCoroutine;

        private void Awake()
        {
            EnsureDefaultUI();
            if (dialoguePanel != null)
                dialoguePanel.SetActive(false);
        }

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

            // Если диалог УЖЕ был запущен до выполнения Start(), подхватываем и отображаем его!
            if (dialogueManager.IsDialogueActive && dialogueManager.CurrentNode != null)
            {
                dialoguePanel.SetActive(true);
                OnNodePresented(dialogueManager.CurrentNode);
            }
        }

        private void EnsureDefaultUI()
        {
            if (dialoguePanel != null && choicesPanel != null && choiceButtonPrefab != null)
                return;

            Debug.Log("[DialogueUI] Creating default UI Canvas and dark-fantasy layout dynamically...");

            // 1. Создаём Canvas
            var canvasGo = new GameObject("DialogueCanvas");
            canvasGo.transform.SetParent(this.transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 2. Создаём Dialogue Panel (На весь экран на время тестов: 5% - 95%)
            var panelGo = new GameObject("DialoguePanel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.05f, 0.05f);
            panelRect.anchorMax = new Vector2(0.95f, 0.95f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f); // Тёмно-серый фон
            dialoguePanel = panelGo;

            // 3. Создаём Имя Говорящего (Speaker Name)
            var nameGo = new GameObject("SpeakerNameText");
            nameGo.transform.SetParent(panelGo.transform, false);
            var nameRect = nameGo.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.03f, 0.92f);
            nameRect.anchorMax = new Vector2(0.97f, 0.98f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            speakerNameText = nameGo.AddComponent<Text>();
            speakerNameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (speakerNameText.font == null) speakerNameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (speakerNameText.font == null) speakerNameText.font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            speakerNameText.fontSize = 26;
            speakerNameText.fontStyle = FontStyle.Bold;
            speakerNameText.color = new Color(1f, 0.84f, 0f, 1f); // Золотой цвет
            speakerNameText.alignment = TextAnchor.MiddleLeft;

            // 4. Создаём Текст Диалога (Dialogue Text)
            var textGo = new GameObject("DialogueText");
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.03f, 0.45f);
            textRect.anchorMax = new Vector2(0.97f, 0.90f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            dialogueText = textGo.AddComponent<Text>();
            dialogueText.font = speakerNameText.font;
            dialogueText.fontSize = 22;
            dialogueText.color = Color.white;
            dialogueText.alignment = TextAnchor.UpperLeft;
            dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            dialogueText.verticalOverflow = VerticalWrapMode.Truncate;

            // 5. Создаём Панель Выборов (Choices Panel)
            var choicesGo = new GameObject("ChoicesPanel");
            choicesGo.transform.SetParent(panelGo.transform, false);
            var choicesRect = choicesGo.AddComponent<RectTransform>();
            choicesRect.anchorMin = new Vector2(0.03f, 0.02f);
            choicesRect.anchorMax = new Vector2(0.97f, 0.42f);
            choicesRect.offsetMin = Vector2.zero;
            choicesRect.offsetMax = Vector2.zero;
            var layoutGroup = choicesGo.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 8f;
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
            choicesPanel = choicesGo;

            // 6. Создаём Шаблон Кнопки Выбора (Choice Button Prefab)
            var btnGo = new GameObject("ChoiceButtonPrefab");
            btnGo.transform.SetParent(canvasGo.transform, false);
            var btnRect = btnGo.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(0, 34);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.22f, 0.22f, 0.28f, 1f);
            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.45f, 1f);
            colors.pressedColor = new Color(0.45f, 0.45f, 0.55f, 1f);
            btn.colors = colors;

            var btnTextGo = new GameObject("Text");
            btnTextGo.transform.SetParent(btnGo.transform, false);
            var btnTextRect = btnTextGo.AddComponent<RectTransform>();
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            btnTextRect.offsetMin = new Vector2(12, 2);
            btnTextRect.offsetMax = new Vector2(-12, -2);
            var btnText = btnTextGo.AddComponent<Text>();
            btnText.font = speakerNameText.font;
            btnText.fontSize = 17;
            btnText.color = Color.white;
            btnText.alignment = TextAnchor.MiddleLeft;

            btnGo.SetActive(false);
            choiceButtonPrefab = btnGo;

            // 7. Создаём Панель Проверки Навыка (Skill Check Panel) - Верхняя середина экрана
            var checkGo = new GameObject("SkillCheckPanel");
            checkGo.transform.SetParent(canvasGo.transform, false);
            var checkRect = checkGo.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.25f, 0.75f);
            checkRect.anchorMax = new Vector2(0.75f, 0.92f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f); // Тёмный фон
            var outline = checkGo.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.84f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2, -2);
            skillCheckPanel = checkGo;

            var checkTextGo = new GameObject("SkillCheckResultText");
            checkTextGo.transform.SetParent(checkGo.transform, false);
            var checkTextRect = checkTextGo.AddComponent<RectTransform>();
            checkTextRect.anchorMin = Vector2.zero;
            checkTextRect.anchorMax = Vector2.one;
            checkTextRect.offsetMin = new Vector2(10, 5);
            checkTextRect.offsetMax = new Vector2(-10, -5);
            skillCheckResultText = checkTextGo.AddComponent<Text>();
            skillCheckResultText.font = speakerNameText.font;
            skillCheckResultText.fontSize = 22;
            skillCheckResultText.fontStyle = FontStyle.Bold;
            skillCheckResultText.alignment = TextAnchor.MiddleCenter;
            skillCheckResultText.color = Color.white;

            checkGo.SetActive(false);
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

        private DialogueNode previousNode;
        private string currentBaseText = "";

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

            // Определяем, нужно ли добавлять текст к предыдущему (если прошлый узел автоматически перешел сюда)
            bool appendToPrevious = (previousNode != null && previousNode.choices != null && previousNode.choices.Count == 0 && !string.IsNullOrEmpty(previousNode.autoAdvanceToNodeId));
            previousNode = node;

            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            if (showChoicesCoroutine != null) StopCoroutine(showChoicesCoroutine);

            // Запускаем печать текста
            typingCoroutine = StartCoroutine(TypeText(node.text, appendToPrevious));

            // Показываем выборы (после завершения печати)
            showChoicesCoroutine = StartCoroutine(ShowChoicesAfterTyping(node));
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

        private IEnumerator TypeText(string text, bool append = false)
        {
            isTyping = true;
            fullText = text;
            charIndex = 0;

            if (append && !string.IsNullOrEmpty(dialogueText.text))
            {
                currentBaseText = dialogueText.text + "\n\n--- ";
                if (!string.IsNullOrEmpty(speakerNameText.text))
                    currentBaseText += $"<b><color=#FFD700>{speakerNameText.text}:</color></b> ";
            }
            else
            {
                currentBaseText = "";
            }

            dialogueText.text = currentBaseText;

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
                if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                if (showChoicesCoroutine != null) StopCoroutine(showChoicesCoroutine);

                dialogueText.text = currentBaseText + fullText;
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
                buttonObj.SetActive(true);
                var buttonText = buttonObj.GetComponentInChildren<Text>();
                var button = buttonObj.GetComponent<Button>();

                bool isAvailable = dialogueManager.IsChoiceAvailable(choice);

                // Форматируем текст с учетом пола
                string displayText = DialogueManager.FormatGenderText(choice.text);
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
            if (skillCheckPanel == null || skillCheckResultText == null)
            {
                EnsureDefaultUI();
                if (skillCheckPanel == null || skillCheckResultText == null)
                {
                    Debug.LogWarning("[DialogueUI] Cannot show skill check result: skillCheckPanel is unassigned!");
                    return;
                }
            }

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

            if (skillCheckCoroutine != null) StopCoroutine(skillCheckCoroutine);
            skillCheckCoroutine = StartCoroutine(HideSkillCheckAfterDelay(2f));
        }

        private IEnumerator HideSkillCheckAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (skillCheckPanel != null)
                skillCheckPanel.SetActive(false);
            skillCheckCoroutine = null;
        }

        #endregion

        #region Input

        private void Update()
        {
            if (dialoguePanel == null || !dialoguePanel.activeSelf) return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool clickOrSpace = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                                (kb != null && kb.spaceKey.wasPressedThisFrame);

            // Клик/пробел для продвижения
            if (clickOrSpace)
            {
                if (isTyping)
                {
                    SkipTextAnimation();
                }
                else if (dialogueManager.CurrentNode != null &&
                         (dialogueManager.CurrentNode.choices == null || dialogueManager.CurrentNode.choices.Count == 0))
                {
                    dialogueManager.Advance();
                }
            }

            // Цифры для быстрого выбора
            if (kb != null && dialogueManager.CurrentNode?.choices != null)
            {
                var digitKeys = new[]
                {
                    kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key,
                    kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key
                };

                for (int i = 0; i < Mathf.Min(9, digitKeys.Length); i++)
                {
                    if (digitKeys[i].wasPressedThisFrame && i < dialogueManager.CurrentNode.choices.Count)
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
