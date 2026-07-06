using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;

namespace RPG.Quest
{
    /// <summary>
    /// Система управления квестами. Квесты имеют несколько путей (тегов)
    /// и несколько концовок с геймплейными наградами.
    /// </summary>
    public class QuestManager : MonoBehaviour, ISaveable
    {
        public static QuestManager Instance { get; private set; }

        // Все доступные квесты
        private Dictionary<string, QuestDefinition> allQuests = new();

        // Активные квесты игрока
        private Dictionary<string, QuestInstance> activeQuests = new();

        // Завершённые квесты
        private Dictionary<string, QuestCompletionData> completedQuests = new();

        public string SaveKey => "QuestManager";

        public event Action<string> OnQuestAccepted;
        public event Action<string, string> OnQuestObjectiveUpdated;
        public event Action<string, QuestEnding> OnQuestCompleted;
        public event Action<string> OnQuestFailed;

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

            // Подписываемся на события
            GameManager.Instance.EventBus.OnGlobalFlagChanged += OnGlobalFlagChanged;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance?.EventBus != null)
                GameManager.Instance.EventBus.OnGlobalFlagChanged -= OnGlobalFlagChanged;
        }

        #region Quest Registration

        public void RegisterQuest(QuestDefinition quest)
        {
            allQuests[quest.questId] = quest;
        }

        public void RegisterQuests(List<QuestDefinition> quests)
        {
            foreach (var q in quests)
                allQuests[q.questId] = q;
        }

        #endregion

        #region Quest Lifecycle

        public bool AcceptQuest(string questId)
        {
            if (activeQuests.ContainsKey(questId) || completedQuests.ContainsKey(questId))
            {
                Debug.LogWarning($"[QuestManager] Quest already active or completed: {questId}");
                return false;
            }

            if (!allQuests.TryGetValue(questId, out var definition))
            {
                Debug.LogError($"[QuestManager] Quest not found: {questId}");
                return false;
            }

            // Проверяем предусловия
            if (!CheckPrerequisites(definition.prerequisites))
            {
                Debug.LogWarning($"[QuestManager] Prerequisites not met: {questId}");
                return false;
            }

            var instance = new QuestInstance
            {
                questId = questId,
                definition = definition,
                currentState = QuestState.Active,
                currentObjectiveIndex = 0,
                tags = new(),
                flags = new(),
                startTime = GameManager.Instance.CurrentDay,
                companionInvolved = definition.associatedCompanionId
            };

            activeQuests[questId] = instance;

            GameManager.Instance.EventBus.RaiseQuestStarted(questId);
            OnQuestAccepted?.Invoke(questId);

            Debug.Log($"[QuestManager] Quest accepted: {definition.displayName}");
            return true;
        }

        public void AdvanceQuest(string questId, string objectiveId = null)
        {
            if (!activeQuests.TryGetValue(questId, out var instance))
                return;

            if (!string.IsNullOrEmpty(objectiveId))
            {
                // Находим конкретный объектив
                var obj = instance.definition.objectives.Find(o => o.objectiveId == objectiveId);
                if (obj != null)
                {
                    obj.isCompleted = true;
                    GameManager.Instance.EventBus.RaiseQuestNodeReached(questId, objectiveId);
                    OnQuestObjectiveUpdated?.Invoke(questId, objectiveId);
                }
            }
            else
            {
                // Продвигаем к следующему объективу
                instance.currentObjectiveIndex++;
                if (instance.currentObjectiveIndex >= instance.definition.objectives.Count)
                {
                    // Определяем концовку
                    var ending = DetermineEnding(instance);
                    CompleteQuest(questId, ending);
                }
            }

            // Проверяем, все ли объективы выполнены
            if (AllObjectivesComplete(instance))
            {
                var ending = DetermineEnding(instance);
                CompleteQuest(questId, ending);
            }
        }

        public void SetQuestTag(string questId, string tag)
        {
            if (!activeQuests.TryGetValue(questId, out var instance))
                return;

            if (!instance.tags.Contains(tag))
            {
                instance.tags.Add(tag);
                GameManager.Instance.EventBus.RaiseQuestTagSet(questId, tag);
                Debug.Log($"[QuestManager] Quest {questId} tag set: {tag}");
            }
        }

        public bool HasQuestTag(string questId, string tag)
        {
            if (!activeQuests.TryGetValue(questId, out var instance))
                return false;
            return instance.tags.Contains(tag);
        }

        private void CompleteQuest(string questId, QuestEnding ending)
        {
            if (!activeQuests.TryGetValue(questId, out var instance))
                return;

            instance.currentState = QuestState.Completed;
            instance.endingReached = ending.endingId;

            // Сохраняем данные завершения
            completedQuests[questId] = new QuestCompletionData
            {
                questId = questId,
                endingId = ending.endingId,
                tags = new List<string>(instance.tags),
                completionDay = GameManager.Instance.CurrentDay
            };

            // Выдаём награды
            GrantRewards(ending);

            // Удаляем из активных
            activeQuests.Remove(questId);

            GameManager.Instance.EventBus.RaiseQuestCompleted(questId, ending.endingId);
            OnQuestCompleted?.Invoke(questId, ending);

            Debug.Log($"[QuestManager] Quest completed: {instance.definition.displayName} " +
                      $"- Ending: {ending.displayName}");
        }

        public void FailQuest(string questId)
        {
            if (!activeQuests.TryGetValue(questId, out var instance))
                return;

            instance.currentState = QuestState.Failed;
            activeQuests.Remove(questId);

            GameManager.Instance.EventBus.RaiseQuestFailed(questId);
            OnQuestFailed?.Invoke(questId);
        }

        #endregion

        #region Ending Determination

        private QuestEnding DetermineEnding(QuestInstance instance)
        {
            var endings = instance.definition.endings;
            if (endings.Count == 0)
                return new QuestEnding { endingId = "default", displayName = "Завершено" };

            // Находим концовку, чьи теги совпадают с тегами квеста
            QuestEnding bestMatch = null;
            int bestScore = -1;

            foreach (var ending in endings)
            {
                int score = CalculateEndingScore(ending, instance);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = ending;
                }
            }

            return bestMatch ?? endings[0];
        }

        private int CalculateEndingScore(QuestEnding ending, QuestInstance instance)
        {
            int score = 0;

            foreach (var requiredTag in ending.requiredTags)
            {
                if (instance.tags.Contains(requiredTag))
                    score += 10;
            }

            foreach (var forbiddenTag in ending.forbiddenTags)
            {
                if (instance.tags.Contains(forbiddenTag))
                    score -= 20;
            }

            foreach (var requiredFlag in ending.requiredFlags)
            {
                if (GameManager.Instance.GetFlag(requiredFlag))
                    score += 5;
            }

            return score;
        }

        #endregion

        #region Rewards

        private void GrantRewards(QuestEnding ending)
        {
            foreach (var reward in ending.rewards)
            {
                switch (reward.rewardType)
                {
                    case RewardType.Experience:
                        var player = Character.CharacterCreation.Instance?.Character;
                        if (player != null)
                        {
                            player.stats.experience += reward.intValue;
                            GameManager.Instance.EventBus.RaiseExperienceGained("player", reward.intValue);
                        }
                        break;

                    case RewardType.Gold:
                        Character.CharacterCreation.Instance?.Character?.AddGold(reward.intValue);
                        break;

                    case RewardType.Item:
                        Character.CharacterCreation.Instance?.Character?.AddItem(reward.stringValue);
                        GameManager.Instance.EventBus.RaiseItemAcquired("player", reward.stringValue);
                        break;

                    case RewardType.CompanionAbility:
                        Companion.CompanionManager.Instance?.UnlockAbility(
                            reward.targetId, reward.stringValue);
                        GameManager.Instance.EventBus.RaiseCompanionTraitUnlocked(
                            reward.targetId, reward.stringValue);
                        break;

                    case RewardType.Artifact:
                        // Мощный артефакт для игрока
                        Character.CharacterCreation.Instance?.Character?.AddItem(reward.stringValue);
                        GameManager.Instance.SetFlag($"artifact_{reward.stringValue}");
                        break;

                    case RewardType.FactionReputation:
                        Character.CharacterCreation.Instance?.Character?
                            .ModifyFactionReputation(reward.targetId, reward.intValue);
                        break;

                    case RewardType.Flag:
                        GameManager.Instance.SetFlag(reward.stringValue);
                        break;

                    case RewardType.CompanionAffinity:
                        Companion.CompanionManager.Instance?.ModifyAffinity(
                            reward.targetId, reward.intValue);
                        break;
                }
            }
        }

        #endregion

        #region Queries

        public bool CheckQuestState(string questId, string state)
        {
            if (string.IsNullOrEmpty(state)) return true;

            switch (state.ToLower())
            {
                case "active":
                    return activeQuests.ContainsKey(questId);
                case "completed":
                    return completedQuests.ContainsKey(questId);
                case "failed":
                    return completedQuests.TryGetValue(questId, out var data) &&
                           data.endingId == "failed";
                case "not_started":
                    return !activeQuests.ContainsKey(questId) &&
                           !completedQuests.ContainsKey(questId);
                default:
                    return false;
            }
        }

        public QuestInstance GetQuest(string questId)
        {
            return activeQuests.TryGetValue(questId, out var q) ? q : null;
        }

        public List<QuestInstance> GetActiveQuests()
        {
            return new List<QuestInstance>(activeQuests.Values);
        }

        public List<QuestCompletionData> GetCompletedQuests()
        {
            return new List<QuestCompletionData>(completedQuests.Values);
        }

        public string GetQuestEnding(string questId)
        {
            return completedQuests.TryGetValue(questId, out var data) ? data.endingId : null;
        }

        public bool IsQuestAvailable(string questId)
        {
            if (activeQuests.ContainsKey(questId) || completedQuests.ContainsKey(questId))
                return false;

            if (!allQuests.TryGetValue(questId, out var def))
                return false;

            return CheckPrerequisites(def.prerequisites);
        }

        private bool CheckPrerequisites(QuestPrerequisites prereqs)
        {
            if (prereqs == null) return true;

            if (prereqs.minLevel > 0)
            {
                var player = Character.CharacterCreation.Instance?.Character;
                if (player != null && player.stats.level < prereqs.minLevel)
                    return false;
            }

            foreach (var quest in prereqs.requiredCompletedQuests)
            {
                if (!completedQuests.ContainsKey(quest))
                    return false;
            }

            foreach (var quest in prereqs.requiredCompletedQuestsWithEnding)
            {
                if (!completedQuests.TryGetValue(quest.Key, out var data) ||
                    data.endingId != quest.Value)
                    return false;
            }

            foreach (var flag in prereqs.requiredFlags)
            {
                if (!GameManager.Instance.GetFlag(flag))
                    return false;
            }

            if (!string.IsNullOrEmpty(prereqs.requiredCompanion))
            {
                if (!Companion.CompanionManager.Instance.IsCompanionInParty(prereqs.requiredCompanion))
                    return false;
            }

            return true;
        }

        #endregion

        #region Event Handlers

        private void OnGlobalFlagChanged(string flag, bool value)
        {
            // Проверяем, не нужно ли автоматически продвинуть квест
            foreach (var kvp in activeQuests)
            {
                var instance = kvp.Value;
                foreach (var obj in instance.definition.objectives)
                {
                    if (!obj.isCompleted && !string.IsNullOrEmpty(obj.completeOnFlag) &&
                        obj.completeOnFlag == flag && value)
                    {
                        obj.isCompleted = true;
                        OnQuestObjectiveUpdated?.Invoke(kvp.Key, obj.objectiveId);
                    }
                }

                if (AllObjectivesComplete(instance))
                {
                    var ending = DetermineEnding(instance);
                    CompleteQuest(kvp.Key, ending);
                }
            }

            // Проверяем доступность новых квестов
            foreach (var kvp in allQuests)
            {
                if (!activeQuests.ContainsKey(kvp.Key) &&
                    !completedQuests.ContainsKey(kvp.Key) &&
                    IsQuestAvailable(kvp.Key))
                {
                    // Можно уведомить UI о доступном квесте
                }
            }
        }

        private bool AllObjectivesComplete(QuestInstance instance)
        {
            foreach (var obj in instance.definition.objectives)
            {
                if (!obj.isCompleted && obj.isRequired)
                    return false;
            }
            return true;
        }

        #endregion

        #region Save/Load

        public string OnSave()
        {
            var data = new QuestSaveData();

            // Сохраняем активные квесты
            foreach (var kvp in activeQuests)
            {
                data.activeQuests.Add(new QuestInstanceSaveData
                {
                    questId = kvp.Key,
                    currentObjectiveIndex = kvp.Value.currentObjectiveIndex,
                    tags = new List<string>(kvp.Value.tags),
                    flags = new List<string>(kvp.Value.flags),
                    objectiveStates = GetObjectiveStates(kvp.Value)
                });
            }

            // Сохраняем завершённые
            foreach (var kvp in completedQuests)
            {
                data.completedQuests.Add(kvp.Value);
            }

            return JsonUtility.ToJson(data);
        }

        public void OnLoad(string json)
        {
            var data = JsonUtility.FromJson<QuestSaveData>(json);
            if (data == null) return;

            activeQuests.Clear();
            completedQuests.Clear();

            // Восстанавливаем активные
            foreach (var saved in data.activeQuests)
            {
                if (allQuests.TryGetValue(saved.questId, out var def))
                {
                    var instance = new QuestInstance
                    {
                        questId = saved.questId,
                        definition = def,
                        currentState = QuestState.Active,
                        currentObjectiveIndex = saved.currentObjectiveIndex,
                        tags = saved.tags ?? new(),
                        flags = saved.flags ?? new()
                    };

                    // Восстанавливаем состояния объективов
                    RestoreObjectiveStates(instance, saved.objectiveStates);

                    activeQuests[saved.questId] = instance;
                }
            }

            // Восстанавливаем завершённые
            foreach (var completed in data.completedQuests)
            {
                completedQuests[completed.questId] = completed;
            }
        }

        private List<ObjectiveStateSaveData> GetObjectiveStates(QuestInstance instance)
        {
            var states = new List<ObjectiveStateSaveData>();
            foreach (var obj in instance.definition.objectives)
            {
                states.Add(new ObjectiveStateSaveData
                {
                    objectiveId = obj.objectiveId,
                    isCompleted = obj.isCompleted
                });
            }
            return states;
        }

        private void RestoreObjectiveStates(QuestInstance instance,
            List<ObjectiveStateSaveData> states)
        {
            if (states == null) return;
            foreach (var state in states)
            {
                var obj = instance.definition.objectives.Find(o => o.objectiveId == state.objectiveId);
                if (obj != null)
                    obj.isCompleted = state.isCompleted;
            }
        }

        #endregion
    }

    #region Data Classes

    [Serializable]
    public class QuestDefinition
    {
        public string questId;
        public string displayName;
        [TextArea(3, 8)]
        public string description;
        public QuestType questType; // main, side, companion, personal
        public string associatedCompanionId; // для companion квестов

        public QuestPrerequisites prerequisites;
        public List<QuestObjective> objectives = new();
        public List<QuestEnding> endings = new();

        // Возможные теги
        public List<string> possibleTags = new();
    }

    public enum QuestType
    {
        Main,
        Side,
        Companion,
        Personal,
        Romance
    }

    [Serializable]
    public class QuestPrerequisites
    {
        public int minLevel;
        public List<string> requiredCompletedQuests = new();
        public SerializableStringStringDict requiredCompletedQuestsWithEnding = new();
        public List<string> requiredFlags = new();
        public string requiredCompanion;
    }

    [Serializable]
    public class QuestObjective
    {
        public string objectiveId;
        public string displayName;
        [TextArea(2, 5)]
        public string description;
        public bool isRequired = true;
        public bool isCompleted;

        // Автоматическое выполнение
        public string completeOnFlag;
        public string completeOnTag;
        public string completeOnQuestNode;
    }

    [Serializable]
    public class QuestEnding
    {
        public string endingId;
        public string displayName;
        [TextArea(2, 5)]
        public string description;

        // Условия для этой концовки
        public List<string> requiredTags = new();
        public List<string> forbiddenTags = new();
        public List<string> requiredFlags = new();

        // Награды
        public List<QuestReward> rewards = new();

        // Влияние на будущие квесты
        public List<string> setFlagsOnComplete = new();
        public List<string> unlockQuests = new();
    }

    [Serializable]
    public class QuestReward
    {
        public RewardType rewardType;
        public string targetId; // companion, faction и т.д.
        public string stringValue;
        public int intValue;
    }

    public enum RewardType
    {
        Experience,
        Gold,
        Item,
        CompanionAbility,
        Artifact,
        FactionReputation,
        Flag,
        CompanionAffinity,
        UnlockSpell,
        UnlockSkill
    }

    [Serializable]
    public class QuestInstance
    {
        public string questId;
        [NonSerialized] public QuestDefinition definition;
        public QuestState currentState;
        public int currentObjectiveIndex;
        public List<string> tags = new();
        public List<string> flags = new();
        public int startTime;
        public string endingReached;
        public string companionInvolved;
    }

    public enum QuestState
    {
        NotStarted,
        Active,
        Completed,
        Failed
    }

    [Serializable]
    public class QuestCompletionData
    {
        public string questId;
        public string endingId;
        public List<string> tags = new();
        public int completionDay;
    }

    #endregion

    #region Save Data

    [Serializable]
    public class QuestSaveData
    {
        public List<QuestInstanceSaveData> activeQuests = new();
        public List<QuestCompletionData> completedQuests = new();
    }

    [Serializable]
    public class QuestInstanceSaveData
    {
        public string questId;
        public int currentObjectiveIndex;
        public List<string> tags = new();
        public List<string> flags = new();
        public List<ObjectiveStateSaveData> objectiveStates = new();
    }

    [Serializable]
    public class ObjectiveStateSaveData
    {
        public string objectiveId;
        public bool isCompleted;
    }

    [Serializable]
    public class SerializableStringStringDict : Dictionary<string, string>,
        ISerializationCallbackReceiver
    {
        [SerializeField] private List<string> keys = new();
        [SerializeField] private List<string> values = new();

        public void OnBeforeSerialize()
        {
            keys.Clear(); values.Clear();
            foreach (var kvp in this) { keys.Add(kvp.Key); values.Add(kvp.Value); }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            for (int i = 0; i < keys.Count && i < values.Count; i++)
                this[keys[i]] = values[i];
        }
    }

    #endregion
}
