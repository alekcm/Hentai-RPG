using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RPG.Core;
using RPG.Companion;
using RPG.Dialogue;

namespace RPG.LLM
{
    /// <summary>
    /// Процессор LLM-диалогов. Управляет свободным общением с компаньонами,
    /// парсит ответы, извлекает флаги и изменения отношений.
    /// </summary>
    public class LLMDialogueProcessor : MonoBehaviour
    {
        public static LLMDialogueProcessor Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private ConversationSettings defaultSettings;

        // Текущая активная сессия
        private LLMConversationSession activeSession;

        public bool IsConversationActive => activeSession != null && activeSession.isActive;
        public LLMConversationSession ActiveSession => activeSession;

        public event Action<string> OnCompanionResponse;
        public event Action<string> OnPlayerMessageSent;
        public event Action<LLMConversationResult> OnConversationEnded;
        public event Action OnConversationStarted;
        public event Action<string> OnError;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (defaultSettings == null)
                defaultSettings = new ConversationSettings();
        }

        #region Conversation Lifecycle

        /// <summary>
        /// Начать свободный разговор с компаньоном (в лагере, и т.д.)
        /// </summary>
        public void StartConversation(string companionId, ConversationSettings settings = null)
        {
            if (activeSession != null && activeSession.isActive)
            {
                Debug.LogWarning("[LLMDialogue] Conversation already active");
                return;
            }

            var context = CompanionManager.Instance.GetLLMContext(companionId);
            if (context == null)
            {
                Debug.LogError($"[LLMDialogue] Companion not found: {companionId}");
                return;
            }

            activeSession = new LLMConversationSession
            {
                companionId = companionId,
                context = context,
                settings = settings ?? defaultSettings,
                isActive = true,
                conversationHistory = new List<DialogueTurn>(),
                startTime = Time.time
            };

            GameManager.Instance.EventBus.RaiseLLMConversationStarted(companionId);
            OnConversationStarted?.Invoke();

            Debug.Log($"[LLMDialogue] Started conversation with {context.displayName}");
        }

        /// <summary>
        /// Отправить сообщение от лица игрока
        /// </summary>
        public void SendPlayerMessage(string message)
        {
            if (activeSession == null || !activeSession.isActive)
            {
                Debug.LogWarning("[LLMDialogue] No active conversation");
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
                return;

            // Добавляем сообщение игрока в историю
            activeSession.conversationHistory.Add(new DialogueTurn
            {
                speaker = "player",
                text = message,
                timestamp = Time.time
            });

            OnPlayerMessageSent?.Invoke(message);

            // Строим запрос к LLM
            SendToLLM();
        }

        private void SendToLLM()
        {
            var systemPrompt = LLMContextBuilder.BuildSystemPrompt(
                activeSession.context, activeSession.settings);

            // Определяем, нужен ли NSFW контент в этом сообщении
            bool needsNsfw = activeSession.settings.allowNSFW &&
                            activeSession.conversationHistory.Count > 0 &&
                            IsConversationGoingNSFW();

            // Переключаем на NSFW модель если нужно
            if (needsNsfw)
            {
                LLMManager.Instance.UseNsfwModelForNextRequest(true);
            }

            // Собираем историю сообщений
            var request = new LLMRequest
            {
                messages = new List<LLMMessage>
                {
                    new LLMMessage { role = "system", content = systemPrompt }
                }
            };

            // Добавляем историю разговора
            foreach (var turn in activeSession.conversationHistory)
            {
                string role = turn.speaker == "player" ? "user" : "assistant";
                request.messages.Add(new LLMMessage { role = role, content = turn.text });
            }

            LLMManager.Instance.SendRequest(request, OnLLMResponse, OnLLMError);
        }

        /// <summary>
        /// Определяет, перешёл ли разговор в NSFW направление
        /// </summary>
        private bool IsConversationGoingNSFW()
        {
            if (activeSession.conversationHistory.Count == 0)
                return false;

            // Проверяем последние 3 сообщения
            int startIdx = Mathf.Max(0, activeSession.conversationHistory.Count - 3);
            string[] nsfwKeywords = { "раздева", "поцел", "обним", "тело", "страст",
                "близост", "интим", "kiss", "touch", "undress", "intimate", "desire",
                "close", "bed", "together", "alone" };

            for (int i = startIdx; i < activeSession.conversationHistory.Count; i++)
            {
                string text = activeSession.conversationHistory[i].text.ToLower();
                foreach (var keyword in nsfwKeywords)
                {
                    if (text.Contains(keyword.ToLower()))
                        return true;
                }
            }

            // Также если romance stage высокий и affinity высокая
            if (activeSession.context.isRomanced && activeSession.context.romanceStage >= 2 &&
                activeSession.context.affinity >= 60)
                return true;

            return false;
        }

        private void OnLLMResponse(string response)
        {
            if (activeSession == null || !activeSession.isActive)
                return;

            // Парсим ответ
            var parsed = ParseLLMResponse(response);

            // Добавляем ответ компаньона в историю
            activeSession.conversationHistory.Add(new DialogueTurn
            {
                speaker = activeSession.companionId,
                text = parsed.cleanText,
                timestamp = Time.time
            });

            // Накапливаем изменения
            if (parsed.affinityChange != 0)
                activeSession.totalAffinityChange += parsed.affinityChange;

            activeSession.collectedFlags.AddRange(parsed.flags);

            // Отправляем чистый текст в UI
            OnCompanionResponse?.Invoke(parsed.cleanText);

            // Проверяем лимит сообщений
            if (activeSession.conversationHistory.Count >=
                activeSession.settings.maxConversationTurns * 2)
            {
                Debug.Log("[LLMDialogue] Max conversation length reached");
            }
        }

        private void OnLLMError(string error)
        {
            Debug.LogError($"[LLMDialogue] LLM Error: {error}");
            OnError?.Invoke("Извини, я... задумалась. Попробуй ещё раз.");
        }

        /// <summary>
        /// Завершить разговор и применить результаты
        /// </summary>
        public LLMConversationResult EndConversation()
        {
            if (activeSession == null || !activeSession.isActive)
                return null;

            var result = BuildConversationResult();

            // Генерируем резюме через LLM
            StartCoroutine(GenerateSummaryAndFinish(result));

            return result;
        }

        private IEnumerator GenerateSummaryAndFinish(LLMConversationResult result)
        {
            if (activeSession.conversationHistory.Count > 4)
            {
                // Генерируем резюме
                var summaryPrompt = LLMContextBuilder.BuildSummaryPrompt(
                    activeSession.context.displayName,
                    activeSession.conversationHistory);

                string summary = null;
                bool done = false;

                LLMManager.Instance.QuickRequest(
                    "Ты сжимаешь диалоги в краткие резюме.",
                    summaryPrompt,
                    (resp) => { summary = resp; done = true; },
                    (err) => { done = true; });

                // Ждём ответ с таймаутом
                float waitTime = 0f;
                while (!done && waitTime < 10f)
                {
                    yield return null;
                    waitTime += Time.deltaTime;
                }

                result.summary = summary ?? "Разговор без значительных событий.";
            }
            else
            {
                result.summary = "Короткий разговор.";
            }

            // Определяем сдвиг личности если были значительные события
            if (activeSession.conversationHistory.Count > 6 ||
                activeSession.totalAffinityChange != 0)
            {
                yield return StartCoroutine(DeterminePersonalityShift(result));
            }

            // Применяем результаты
            ApplyResult(result);

            activeSession.isActive = false;

            GameManager.Instance.EventBus.RaiseLLMConversationEnded(
                activeSession.companionId, result);
            OnConversationEnded?.Invoke(result);

            activeSession = null;
        }

        private IEnumerator DeterminePersonalityShift(LLMConversationResult result)
        {
            var companion = CompanionManager.Instance.GetCompanion(activeSession.companionId);
            if (companion == null) yield break;

            var possibleShifts = new List<string>
            {
                "стала более покорной",
                "стала более доминирующей",
                "стала более нежной и заботливой",
                "стала более холодной и отстранённой",
                "стала более агрессивной и напористой",
                "стала более игривой и флиртующей",
                "стала более открытой и доверчивой",
                "стала более замкнутой и подозрительной"
            };

            var prompt = LLMContextBuilder.BuildPersonalityShiftPrompt(
                activeSession.context,
                activeSession.conversationHistory,
                possibleShifts);

            string response = null;
            bool done = false;

            var request = new LLMRequest
            {
                messages = new List<LLMMessage>
                {
                    new LLMMessage { role = "system", content = "Отвечай только в JSON формате." },
                    new LLMMessage { role = "user", content = prompt }
                },
                useJsonFormat = true
            };

            LLMManager.Instance.SendRequest(request,
                (resp) => { response = resp; done = true; },
                (err) => { done = true; });

            float waitTime = 0f;
            while (!done && waitTime < 10f)
            {
                yield return null;
                waitTime += Time.deltaTime;
            }

            if (!string.IsNullOrEmpty(response))
            {
                try
                {
                    var shiftResult = JsonUtility.FromJson<PersonalityShiftResult>(response);
                    if (shiftResult != null && shiftResult.shiftOccurred)
                    {
                        result.personalityShift = shiftResult.shiftDescription;
                        if (shiftResult.affinityChange != 0)
                            result.affinityChange += shiftResult.affinityChange;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LLMDialogue] Failed to parse shift result: {e.Message}");
                }
            }
        }

        private void ApplyResult(LLMConversationResult result)
        {
            CompanionManager.Instance.ApplyLLMResult(result.companionId, result);
        }

        #endregion

        #region Dialogue Modification

        /// <summary>
        /// Анализ существующего диалога для определения LLM-модификаций.
        /// Вызывается перед началом сюжетного диалога с компаньоном.
        /// </summary>
        public void AnalyzeAndModifyDialogue(string companionId, string dialogueId,
            Action<DialogueAnalysisResult> onComplete)
        {
            var context = CompanionManager.Instance.GetLLMContext(companionId);
            if (context == null || context.conversationHistory.Count == 0)
            {
                onComplete?.Invoke(new DialogueAnalysisResult());
                return;
            }

            var dialogue = DialogueManager.Instance.GetDialogue(dialogueId);
            if (dialogue == null)
            {
                onComplete?.Invoke(new DialogueAnalysisResult());
                return;
            }

            // Собираем тексты диалога и варианты
            var dialogueText = CollectDialogueText(dialogue);
            var availableChoices = CollectAvailableChoices(dialogue);
            var hiddenChoices = CollectHiddenChoices(dialogue);

            if (hiddenChoices.Count == 0)
            {
                onComplete?.Invoke(new DialogueAnalysisResult());
                return;
            }

            var prompt = LLMContextBuilder.BuildDialogueAnalysisPrompt(
                context, dialogueText, availableChoices, hiddenChoices);

            var request = new LLMRequest
            {
                messages = new List<LLMMessage>
                {
                    new LLMMessage { role = "system", content = "Отвечай только в JSON формате." },
                    new LLMMessage { role = "user", content = prompt }
                },
                useJsonFormat = true
            };

            LLMManager.Instance.SendRequest(request,
                (response) =>
                {
                    var result = ParseAnalysisResult(response);
                    if (result != null)
                    {
                        // Применяем модификации к диалогу
                        ApplyDialogueModifications(dialogue, companionId, result);
                    }
                    onComplete?.Invoke(result ?? new DialogueAnalysisResult());
                },
                (error) =>
                {
                    Debug.LogError($"[LLMDialogue] Analysis failed: {error}");
                    onComplete?.Invoke(new DialogueAnalysisResult());
                });
        }

        private string CollectDialogueText(DialogueGraph dialogue)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var node in dialogue.nodes)
            {
                if (!string.IsNullOrEmpty(node.text))
                {
                    string speaker = node.speakerType == DialogueSpeakerType.NPC ?
                        dialogue.speakerName : "Игрок";
                    sb.AppendLine($"{speaker}: {node.text}");
                }
            }
            return sb.ToString();
        }

        private List<string> CollectAvailableChoices(DialogueGraph dialogue)
        {
            var choices = new List<string>();
            foreach (var node in dialogue.nodes)
            {
                foreach (var choice in node.choices)
                {
                    if (!choice.isHiddenByDefault)
                        choices.Add(choice.text);
                }
            }
            return choices;
        }

        private List<string> CollectHiddenChoices(DialogueGraph dialogue)
        {
            var hidden = new List<string>();
            foreach (var node in dialogue.nodes)
            {
                foreach (var choice in node.choices)
                {
                    if (choice.isHiddenByDefault)
                        hidden.Add(choice.text);
                }
            }
            return hidden;
        }

        private void ApplyDialogueModifications(
            DialogueGraph dialogue, string companionId, DialogueAnalysisResult analysis)
        {
            // Создаём модификацию
            var mod = new LLMDialogueModification
            {
                modificationId = $"llm_analysis_{dialogue.dialogueId}_{DateTime.Now.Ticks}",
                triggerFlag = "", // уже применено
                affectsChoices = true
            };

            // Блокируем варианты
            int choiceIdx = 0;
            foreach (var node in dialogue.nodes)
            {
                foreach (var choice in node.choices)
                {
                    if (!choice.isHiddenByDefault && analysis.disabledChoices.Contains(choiceIdx))
                    {
                        mod.disabledChoiceIds.Add(choice.choiceId);

                        // Добавляем условие недоступности
                        choice.conditions.Add(new ChoiceCondition
                        {
                            conditionType = ConditionType.HasFlag,
                            parameter = $"llm_blocked_{mod.modificationId}_{choiceIdx}",
                            invert = false
                        });
                        choice.unavailableTooltip = analysis.reason;

                        // Устанавливаем флаг чтобы условие всегда было false
                        // (т.е. флаг не существует = условие false = заблокировано)
                    }
                    if (!choice.isHiddenByDefault)
                        choiceIdx++;
                }
            }

            // Разблокируем скрытые
            int hiddenIdx = 0;
            foreach (var node in dialogue.nodes)
            {
                foreach (var choice in node.choices)
                {
                    if (choice.isHiddenByDefault && analysis.enabledHidden.Contains(hiddenIdx))
                    {
                        mod.enabledChoiceIds.Add(choice.choiceId);
                        choice.isHiddenByDefault = false;
                    }
                    if (choice.isHiddenByDefault || mod.enabledChoiceIds.Contains(choice.choiceId))
                        hiddenIdx++;
                }
            }

            mod.tone = analysis.toneModifier;
            dialogue.llmModifications.Add(mod);

            GameManager.Instance.EventBus.RaiseLLMDialougeModified(dialogue.dialogueId);
        }

        private DialogueAnalysisResult ParseAnalysisResult(string json)
        {
            try
            {
                // Простой парсинг JSON
                var result = new DialogueAnalysisResult();

                // Парсим disabled_choices
                var disabledMatch = Regex.Match(json, "\"disabled_choices\"\\s*:\\s*\\[([^\\]]*)\\]");
                if (disabledMatch.Success)
                {
                    var nums = Regex.Matches(disabledMatch.Groups[1].Value, "\\d+");
                    foreach (Match num in nums)
                        result.disabledChoices.Add(int.Parse(num.Value));
                }

                // Парсим enabled_hidden
                var enabledMatch = Regex.Match(json, "\"enabled_hidden\"\\s*:\\s*\\[([^\\]]*)\\]");
                if (enabledMatch.Success)
                {
                    var nums = Regex.Matches(enabledMatch.Groups[1].Value, "\\d+");
                    foreach (Match num in nums)
                        result.enabledHidden.Add(int.Parse(num.Value));
                }

                // Парсим tone_modifier
                var toneMatch = Regex.Match(json, "\"tone_modifier\"\\s*:\\s*\"([^\"]+)\"");
                if (toneMatch.Success)
                    result.toneModifier = toneMatch.Groups[1].Value;

                // Парсим reason
                var reasonMatch = Regex.Match(json, "\"reason\"\\s*:\\s*\"([^\"]+)\"");
                if (reasonMatch.Success)
                    result.reason = reasonMatch.Groups[1].Value;

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLMDialogue] Failed to parse analysis: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Response Parsing

        private ParsedLLMResponse ParseLLMResponse(string rawResponse)
        {
            var parsed = new ParsedLLMResponse
            {
                cleanText = rawResponse,
                affinityChange = 0,
                flags = new List<string>()
            };

            // Извлекаем флаги [FLAG:flag_name]
            var flagMatches = Regex.Matches(rawResponse, "\\[FLAG:([^\\]]+)\\]");
            foreach (Match match in flagMatches)
            {
                parsed.flags.Add(match.Groups[1].Value.Trim());
                parsed.cleanText = parsed.cleanText.Replace(match.Value, "").Trim();
            }

            // Извлекаем изменение отношения [AFFINITY:+N] или [AFFINITY:-N]
            var affinityMatch = Regex.Match(rawResponse, "\\[AFFINITY:([+-]?\\d+)\\]");
            if (affinityMatch.Success)
            {
                int.TryParse(affinityMatch.Groups[1].Value, out parsed.affinityChange);
                parsed.affinityChange = Mathf.Clamp(parsed.affinityChange, -5, 5);
                parsed.cleanText = parsed.cleanText.Replace(affinityMatch.Value, "").Trim();
            }

            // Чистим лишние пробелы
            parsed.cleanText = Regex.Replace(parsed.cleanText, @"\s+", " ").Trim();

            return parsed;
        }

        private LLMConversationResult BuildConversationResult()
        {
            var result = new LLMConversationResult
            {
                companionId = activeSession.companionId,
                affinityChange = activeSession.totalAffinityChange,
                unlockedFlags = new List<string>(),
                setFlags = new List<string>(activeSession.collectedFlags),
                hadNSFWContent = activeSession.conversationHistory.Exists(
                    t => t.text.Contains("[NSFW]") || t.text.Contains("*раздева"))
            };

            // Определяем какие флаги разблокированы
            foreach (var flag in activeSession.collectedFlags)
            {
                if (flag.StartsWith("unlock_") || flag.StartsWith("enable_"))
                    result.unlockedFlags.Add(flag);
            }

            return result;
        }

        #endregion

        #region Quick Actions

        /// <summary>
        /// Быстрый способ проверить, может ли компаньон говорить прямо сейчас
        /// </summary>
        public bool CanTalkToCompanion(string companionId)
        {
            if (activeSession != null && activeSession.isActive)
                return false;

            if (!CompanionManager.Instance.IsCompanionInParty(companionId))
                return false;

            if (GameManager.Instance.CurrentState != GameState.Camp &&
                GameManager.Instance.CurrentState != GameState.Exploration)
                return false;

            return true;
        }

        /// <summary>
        /// Получить историю разговоров с компаньоном (для UI журнала)
        /// </summary>
        public List<string> GetConversationHistory(string companionId)
        {
            var companion = CompanionManager.Instance.GetCompanion(companionId);
            if (companion == null) return new();
            return new List<string>(companion.conversationSummaries);
        }

        #endregion
    }

    #region Internal Types

    public class LLMConversationSession
    {
        public string companionId;
        public CompanionLLMContext context;
        public ConversationSettings settings;
        public bool isActive;
        public List<DialogueTurn> conversationHistory = new();
        public float startTime;
        public int totalAffinityChange;
        public List<string> collectedFlags = new();
    }

    internal class ParsedLLMResponse
    {
        public string cleanText;
        public int affinityChange;
        public List<string> flags = new();
    }

    #endregion
}
