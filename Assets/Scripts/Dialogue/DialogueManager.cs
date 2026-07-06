using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using RPG.Core;
using RPG.Character;

namespace RPG.Dialogue
{
    /// <summary>
    /// Главный менеджер диалогов. Управляет запуском, прогрессией и завершением диалогов.
    /// </summary>
    public class DialogueManager : MonoBehaviour, ISaveable
    {
        public static DialogueManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float defaultTextSpeed = 0.03f;
        [SerializeField] private float autoAdvanceDelay = 2f;

        // Текущее состояние
        private DialogueGraph currentDialogue;
        private DialogueNode currentNode;
        private bool isDialogueActive;
        private Stack<string> dialogueHistory = new();
        private List<DialogueResult> sessionResults = new();

        // Доступные диалоги (загружаются из ScriptableObject или JSON)
        private Dictionary<string, DialogueGraph> allDialogues = new();

        public bool IsDialogueActive => isDialogueActive;
        public DialogueNode CurrentNode => currentNode;
        public DialogueGraph CurrentDialogue => currentDialogue;

        public event Action<DialogueNode> OnNodePresented;
        public event Action<DialogueChoice> OnChoiceMade;
        public event Action<SkillCheckResult> OnSkillCheckPerformed;
        public event Action OnDialogueStarted;
        public event Action OnDialogueEnded;

        public string SaveKey => "DialogueManager";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            GameManager.Instance.SaveManager.RegisterSaveable(this);
        }

        #region Dialogue Lifecycle

        public bool StartDialogue(string dialogueId)
        {
            if (isDialogueActive)
            {
                Debug.LogWarning("[DialogueManager] Dialogue already active");
                return false;
            }

            if (!allDialogues.TryGetValue(dialogueId, out var dialogue))
            {
                Debug.LogError($"[DialogueManager] Dialogue not found: {dialogueId}");
                return false;
            }

            // Проверяем контекст
            if (!CheckDialogueContext(dialogue.context))
            {
                Debug.LogWarning($"[DialogueManager] Context check failed for: {dialogueId}");
                return false;
            }

            currentDialogue = dialogue;
            currentNode = dialogue.GetStartNode();
            isDialogueActive = true;
            dialogueHistory.Clear();
            sessionResults.Clear();

            // Применяем LLM модификации
            ApplyLLMModifications();

            GameManager.Instance.SetGameState(GameState.Dialogue);
            GameManager.Instance.EventBus.RaiseDialogueStarted(dialogueId);
            OnDialogueStarted?.Invoke();

            PresentCurrentNode();
            return true;
        }

        public void MakeChoice(int choiceIndex)
        {
            if (!isDialogueActive || currentNode == null)
                return;

            if (choiceIndex < 0 || choiceIndex >= currentNode.choices.Count)
            {
                Debug.LogError($"[DialogueManager] Invalid choice index: {choiceIndex}");
                return;
            }

            var choice = currentNode.choices[choiceIndex];

            // Проверяем доступность
            if (!IsChoiceAvailable(choice))
            {
                Debug.LogWarning("[DialogueManager] Choice is not available");
                return;
            }

            // Проверяем навык
            if (choice.skillCheck != null && choice.skillCheck.HasCheck)
            {
                var checkResult = PerformSkillCheck(choice.skillCheck);
                OnSkillCheckPerformed?.Invoke(checkResult);

                if (checkResult.success && !string.IsNullOrEmpty(choice.skillCheck.successNodeId))
                {
                    ProcessChoiceActions(choice);
                    NavigateToNode(choice.skillCheck.successNodeId);
                    return;
                }
                else if (!checkResult.success && !string.IsNullOrEmpty(choice.skillCheck.failureNodeId))
                {
                    ProcessChoiceActions(choice);
                    NavigateToNode(choice.skillCheck.failureNodeId);
                    return;
                }
            }

            // Обрабатываем действия
            ProcessChoiceActions(choice);

            // Сохраняем в историю
            dialogueHistory.Push($"{currentNode.nodeId} -> {choice.choiceId}");

            // Событие
            OnChoiceMade?.Invoke(choice);
            GameManager.Instance.EventBus.RaiseDialogueChoiceMade(currentDialogue.dialogueId, choiceIndex);

            // Устанавливаем тег квеста
            if (!string.IsNullOrEmpty(choice.questTag))
            {
                GameManager.Instance.SetFlag(choice.questTag);
                if (currentDialogue.speakerId != null)
                    GameManager.Instance.EventBus.RaiseQuestTagSet(currentDialogue.speakerId, choice.questTag);
            }

            // Навигация
            if (!string.IsNullOrEmpty(choice.nextNodeId))
            {
                NavigateToNode(choice.nextNodeId);
            }
            else
            {
                EndDialogue();
            }
        }

        public void Advance()
        {
            if (!isDialogueActive || currentNode == null)
                return;

            // Если есть выборы, не продвигаем автоматически
            if (currentNode.choices.Count > 0)
                return;

            if (!string.IsNullOrEmpty(currentNode.autoAdvanceToNodeId))
            {
                NavigateToNode(currentNode.autoAdvanceToNodeId);
            }
            else
            {
                EndDialogue();
            }
        }

        public void EndDialogue()
        {
            if (!isDialogueActive) return;

            string dialogueId = currentDialogue?.dialogueId;
            isDialogueActive = false;
            currentDialogue = null;
            currentNode = null;

            GameManager.Instance.SetGameState(GameState.Exploration);
            GameManager.Instance.EventBus.RaiseDialogueEnded(dialogueId);
            OnDialogueEnded?.Invoke();
        }

        #endregion

        #region Node Processing

        private void NavigateToNode(string nodeId)
        {
            var node = currentDialogue.GetNode(nodeId);
            if (node == null)
            {
                Debug.LogError($"[DialogueManager] Node not found: {nodeId}");
                EndDialogue();
                return;
            }

            currentNode = node;
            PresentCurrentNode();
        }

        private void PresentCurrentNode()
        {
            if (currentNode == null) return;

            // Выполняем действия входа
            foreach (var action in currentNode.onEnterActions)
            {
                ExecuteAction(action);
            }

            // Устанавливаем тег квеста если есть
            if (!string.IsNullOrEmpty(currentNode.questTagOnSelect))
            {
                GameManager.Instance.SetFlag(currentNode.questTagOnSelect);
            }

            // Собираем доступные выборы
            FilterChoices();

            // Представляем узел UI
            OnNodePresented?.Invoke(currentNode);

            // Автоматическое продвижение если нет выборов
            if (currentNode.choices.Count == 0 && !currentNode.IsEndNode)
            {
                StartCoroutine(AutoAdvance(currentNode.autoAdvanceDelay));
            }

            // Конец диалога
            if (currentNode.IsEndNode)
            {
                StartCoroutine(DelayedEnd(1f));
            }
        }

        private IEnumerator AutoAdvance(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (isDialogueActive)
                Advance();
        }

        private IEnumerator DelayedEnd(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (isDialogueActive)
                EndDialogue();
        }

        private void FilterChoices()
        {
            // Фильтруем выборы по условиям доступности
            // UI будет отображать только доступные + серые недоступные
        }

        #endregion

        #region Choice Availability

        public bool IsChoiceAvailable(DialogueChoice choice)
        {
            // Проверяем все условия
            foreach (var condition in choice.conditions)
            {
                if (!condition.Evaluate())
                    return false;
            }

            // Проверяем LLM unlock
            if (choice.isHiddenByDefault)
            {
                if (choice.llmUnlockCondition == null)
                    return false;

                if (!string.IsNullOrEmpty(choice.llmUnlockCondition.requiredFlag) &&
                    !GameManager.Instance.GetFlag(choice.llmUnlockCondition.requiredFlag))
                    return false;
            }

            return true;
        }

        public bool IsChoiceVisible(DialogueChoice choice)
        {
            if (choice.isHiddenByDefault)
                return IsChoiceAvailable(choice); // скрытые видны только когда доступны

            if (!choice.showWhenUnavailable && !IsChoiceAvailable(choice))
                return false;

            return true;
        }

        public string GetUnavailableReason(DialogueChoice choice)
        {
            if (!string.IsNullOrEmpty(choice.unavailableTooltip))
                return choice.unavailableTooltip;

            if (choice.skillCheck != null && choice.skillCheck.HasCheck)
            {
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    int bonus = player.stats.GetSkillBonus(choice.skillCheck.skillType);
                    return $"[{choice.skillCheck.skillType}] SL {choice.skillCheck.difficultyClass} " +
                           $"(ваш бонус: +{bonus})";
                }
            }

            foreach (var condition in choice.conditions)
            {
                if (!condition.Evaluate())
                {
                    return GetConditionReason(condition);
                }
            }

            if (choice.isHiddenByDefault && choice.llmUnlockCondition != null)
                return choice.llmUnlockCondition.description ?? "Требуется особый подход";

            return "Недоступно";
        }

        private string GetConditionReason(ChoiceCondition condition)
        {
            return condition.conditionType switch
            {
                ConditionType.HasFlag => "Не выполнены условия",
                ConditionType.HasItem => $"Требуется предмет: {condition.parameter}",
                ConditionType.HasQuest => "Требуется квест",
                ConditionType.HasGold => $"Требуется золото: {condition.value}",
                ConditionType.AttributeCheck => $"Требуется {condition.parameter} ≥ {condition.value}",
                ConditionType.CompanionAffinity => $"Недостаточные отношения с компаньоном",
                ConditionType.CompanionPresent => $"Требуется компаньон в отряде",
                ConditionType.TimeOfDay => $"Доступно только {condition.parameter.ToLower()}",
                _ => "Недоступно"
            };
        }

        #endregion

        #region Skill Checks

        private SkillCheckResult PerformSkillCheck(SkillCheckData check)
        {
            var player = CharacterCreation.Instance?.Character;
            if (player == null)
                return new SkillCheckResult { success = false };

            var (success, roll, total) = player.stats.CheckSkillDetailed(
                check.skillType, check.difficultyClass);

            var result = new SkillCheckResult
            {
                success = success,
                skillType = check.skillType,
                difficultyClass = check.difficultyClass,
                roll = roll,
                bonus = player.stats.GetSkillBonus(check.skillType),
                total = total,
                isCriticalSuccess = roll == 20,
                isCriticalFailure = roll == 1
            };

            // Критический успех/провал переопределяют
            if (result.isCriticalSuccess) result.success = true;
            if (result.isCriticalFailure) result.success = false;

            // Уведомляем
            GameManager.Instance.EventBus.RaiseSkillCheckResult(
                check.skillType.ToString(), result.success);

            return result;
        }

        #endregion

        #region Actions

        private void ProcessChoiceActions(DialogueChoice choice)
        {
            foreach (var action in choice.onChooseActions)
            {
                ExecuteAction(action);
            }
        }

        private void ExecuteAction(DialogueAction action)
        {
            switch (action.actionType)
            {
                case DialogueActionType.SetFlag:
                    GameManager.Instance.SetFlag(action.parameter1, true);
                    break;
                case DialogueActionType.ClearFlag:
                    GameManager.Instance.SetFlag(action.parameter1, false);
                    break;
                case DialogueActionType.GiveItem:
                    CharacterCreation.Instance?.Character?.AddItem(action.parameter1);
                    break;
                case DialogueActionType.RemoveItem:
                    CharacterCreation.Instance?.Character?.RemoveItem(action.parameter1);
                    break;
                case DialogueActionType.AddGold:
                    CharacterCreation.Instance?.Character?.AddGold(action.intValue);
                    break;
                case DialogueActionType.RemoveGold:
                    CharacterCreation.Instance?.Character?.SpendGold(action.intValue);
                    break;
                case DialogueActionType.GiveExperience:
                    if (CharacterCreation.Instance?.Character != null)
                    {
                        CharacterCreation.Instance.Character.stats.experience += action.intValue;
                        if (CharacterCreation.Instance.Character.stats.TryLevelUp())
                        {
                            GameManager.Instance.EventBus.RaiseLevelUp(
                                "player", CharacterCreation.Instance.Character.stats.level);
                        }
                    }
                    break;
                case DialogueActionType.ChangeAffinity:
                    Companion.CompanionManager.Instance?.ModifyAffinity(
                        action.parameter1, action.intValue);
                    break;
                case DialogueActionType.SetQuestTag:
                    GameManager.Instance.SetFlag(action.parameter1);
                    break;
                case DialogueActionType.AdvanceQuest:
                    Quest.QuestManager.Instance?.AdvanceQuest(
                        action.parameter1, action.parameter2);
                    break;
            }
        }

        #endregion

        #region LLM Modifications

        private void ApplyLLMModifications()
        {
            if (currentDialogue == null || !currentDialogue.allowLLMModification)
                return;

            foreach (var mod in currentDialogue.llmModifications)
            {
                // Проверяем триггер
                if (!string.IsNullOrEmpty(mod.triggerFlag) &&
                    !GameManager.Instance.GetFlag(mod.triggerFlag))
                    continue;

                // Применяем модификацию текста
                var node = currentDialogue.GetNode(mod.targetNodeId);
                if (node == null) continue;

                if (!string.IsNullOrEmpty(mod.modifiedText))
                    node.text = mod.modifiedText;

                // Блокируем/разблокируем выборы
                if (mod.affectsChoices)
                {
                    foreach (var choiceId in mod.disabledChoiceIds)
                    {
                        var choice = node.choices.Find(c => c.choiceId == choiceId);
                        if (choice != null)
                        {
                            choice.conditions.Add(new ChoiceCondition
                            {
                                conditionType = ConditionType.HasFlag,
                                parameter = "llm_blocked_never_true_" + mod.modificationId,
                                invert = false // никогда не выполнится = заблокировано
                            });
                            choice.unavailableTooltip = GetBlockedReason(mod.tone);
                        }
                    }

                    foreach (var choiceId in mod.enabledChoiceIds)
                    {
                        var choice = node.choices.Find(c => c.choiceId == choiceId);
                        if (choice != null && choice.isHiddenByDefault)
                        {
                            choice.isHiddenByDefault = false;
                        }
                    }
                }
            }
        }

        private string GetBlockedReason(string tone)
        {
            return tone switch
            {
                "submissive" => "Ваш персонаж сейчас слишком покорен для этого",
                "aggressive" => "Ваш персонаж слишком агрессивен для мирного подхода",
                "flirtatious" => "Сейчас не время для флирта",
                _ => "Этот вариант сейчас недоступен"
            };
        }

        /// <summary>
        /// Применить модификации от LLM после разговора с компаньоном
        /// </summary>
        public void ApplyLLMResults(string dialogueId, LLMConversationResult result)
        {
            if (!allDialogues.TryGetValue(dialogueId, out var dialogue))
                return;

            // Устанавливаем флаги
            foreach (var flag in result.setFlags)
            {
                GameManager.Instance.SetFlag(flag);
            }

            // Разблокируем скрытые выборы
            foreach (var flag in result.unlockedFlags)
            {
                GameManager.Instance.SetFlag(flag);
            }

            // Создаём модификацию
            var mod = new LLMDialogueModification
            {
                modificationId = $"llm_{dialogueId}_{DateTime.Now.Ticks}",
                triggerFlag = result.setFlags.Count > 0 ? result.setFlags[0] : "",
                modifiedText = null, // текст не меняется, меняются только выборы
                affectsChoices = true,
                enabledChoiceIds = result.unlockedFlags,
                disabledChoiceIds = new()
            };

            dialogue.llmModifications.Add(mod);

            GameManager.Instance.EventBus.RaiseLLMDialougeModified(dialogueId);
        }

        #endregion

        #region Dialogue Registration

        public void RegisterDialogue(DialogueGraph dialogue)
        {
            allDialogues[dialogue.dialogueId] = dialogue;
        }

        public void RegisterDialogues(List<DialogueGraph> dialogues)
        {
            foreach (var d in dialogues)
                allDialogues[d.dialogueId] = d;
        }

        public DialogueGraph GetDialogue(string dialogueId)
        {
            return allDialogues.TryGetValue(dialogueId, out var d) ? d : null;
        }

        public bool HasDialogue(string dialogueId)
        {
            return allDialogues.ContainsKey(dialogueId);
        }

        #endregion

        #region Context Check

        private bool CheckDialogueContext(DialogueContext context)
        {
            if (context == null) return true;

            // Проверяем флаги
            foreach (var flag in context.requiredFlags)
            {
                if (!GameManager.Instance.GetFlag(flag))
                    return false;
            }
            foreach (var flag in context.forbiddenFlags)
            {
                if (GameManager.Instance.GetFlag(flag))
                    return false;
            }

            // Проверяем квест
            if (!string.IsNullOrEmpty(context.requiredQuestId))
            {
                if (!Quest.QuestManager.Instance.CheckQuestState(
                    context.requiredQuestId, context.requiredQuestState))
                    return false;
            }

            // Проверяем уровень
            if (context.minLevel > 0)
            {
                var player = CharacterCreation.Instance?.Character;
                if (player != null && player.stats.level < context.minLevel)
                    return false;
            }

            // Проверяем, в лагере ли мы
            if (context.campOnly && GameManager.Instance.CurrentState != GameState.Camp)
                return false;

            return true;
        }

        #endregion

        #region Save/Load

        public string OnSave()
        {
            var data = new DialogueSaveData
            {
                activeDialogueId = currentDialogue?.dialogueId,
                activeNodeId = currentNode?.nodeId,
                history = new List<string>(dialogueHistory),
                dialogueModifications = SerializeModifications()
            };
            return JsonUtility.ToJson(data);
        }

        public void OnLoad(string json)
        {
            var data = JsonUtility.FromJson<DialogueSaveData>(json);
            if (data == null) return;

            // Восстанавливаем модификации
            DeserializeModifications(data.dialogueModifications);

            // Если был активный диалог - восстанавливаем
            if (!string.IsNullOrEmpty(data.activeDialogueId) &&
                allDialogues.ContainsKey(data.activeDialogueId))
            {
                dialogueHistory = new Stack<string>(data.history ?? new());
                StartDialogue(data.activeDialogueId);
                if (!string.IsNullOrEmpty(data.activeNodeId))
                    NavigateToNode(data.activeNodeId);
            }
        }

        private string SerializeModifications()
        {
            // Сериализуем все LLM модификации
            var mods = new SerializableModsDict();
            foreach (var kvp in allDialogues)
            {
                if (kvp.Value.llmModifications.Count > 0)
                    mods[kvp.Key] = kvp.Value.llmModifications;
            }
            return JsonUtility.ToJson(new SerializableMods { mods = mods });
        }

        private void DeserializeModifications(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var data = JsonUtility.FromJson<SerializableMods>(json);
            if (data?.mods == null) return;

            foreach (var kvp in data.mods)
            {
                if (allDialogues.TryGetValue(kvp.Key, out var dialogue))
                {
                    dialogue.llmModifications = kvp.Value;
                }
            }
        }

        #endregion
    }

    [Serializable]
    public class SkillCheckResult
    {
        public bool success;
        public SkillType skillType;
        public int difficultyClass;
        public int roll;
        public int bonus;
        public int total;
        public bool isCriticalSuccess;
        public bool isCriticalFailure;
    }

    [Serializable]
    public class DialogueResult
    {
        public string dialogueId;
        public string choiceId;
        public string questTag;
        public bool skillCheckPassed;
    }

    [Serializable]
    public class DialogueSaveData
    {
        public string activeDialogueId;
        public string activeNodeId;
        public List<string> history;
        public string dialogueModifications;
    }

    [Serializable]
    public class SerializableMods
    {
        public SerializableModsDict mods = new();
    }

    // Wrapper для Dictionary<string, List<LLMDialogueModification>> для JsonUtility
    [Serializable]
    public class SerializableModsDict : Dictionary<string, List<LLMDialogueModification>>,
        ISerializationCallbackReceiver
    {
        [SerializeField] private List<string> keys = new();
        [SerializeField] private List<string> values = new();

        public void OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();
            foreach (var kvp in this)
            {
                keys.Add(kvp.Key);
                values.Add(JsonUtility.ToJson(new SerializableModList { items = kvp.Value }));
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                var list = JsonUtility.FromJson<SerializableModList>(values[i]);
                this[keys[i]] = list?.items ?? new();
            }
        }
    }

    [Serializable]
    public class SerializableModList
    {
        public List<LLMDialogueModification> items = new();
    }
}
