using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Dialogue
{
    /// <summary>
    /// Граф диалога. Содержит все узлы и переходы между ними.
    /// Один DialogueGraph = один диалог с NPC или компаньоном.
    /// </summary>
    [Serializable]
    public class DialogueGraph
    {
        public string dialogueId;
        public string displayName;
        public string speakerId; // ID NPC/компаньона
        public string speakerName;
        public DialogueContext context; // когда доступен этот диалог

        // Узлы графа
        public List<DialogueNode> nodes = new();
        public string startNodeId;

        // LLM модификации
        public bool allowLLMModification = true;
        public List<LLMDialogueModification> llmModifications = new();

        public DialogueNode GetStartNode()
        {
            return nodes.Find(n => n.nodeId == startNodeId);
        }

        public DialogueNode GetNode(string nodeId)
        {
            return nodes.Find(n => n.nodeId == nodeId);
        }

        /// <summary>
        /// Получить все узлы, помеченные для LLM-модификации
        /// </summary>
        public List<DialogueNode> GetLLMModifiableNodes()
        {
            return nodes.FindAll(n => n.isLLMModifiable);
        }

        /// <summary>
        /// Получить скрытые варианты, которые может открыть LLM
        /// </summary>
        public List<DialogueChoice> GetHiddenChoices()
        {
            var hidden = new List<DialogueChoice>();
            foreach (var node in nodes)
            {
                foreach (var choice in node.choices)
                {
                    if (choice.isHiddenByDefault && choice.llmUnlockCondition != null)
                        hidden.Add(choice);
                }
            }
            return hidden;
        }
    }

    /// <summary>
    /// Контекст - условия доступности диалога
    /// </summary>
    [Serializable]
    public class DialogueContext
    {
        public string requiredQuestId;
        public string requiredQuestState; // "active", "completed", "failed"
        public List<string> requiredFlags = new();
        public List<string> forbiddenFlags = new();
        public bool campOnly; // доступен только в лагере
        public int minCompanionAffinity; // минимальное отношение с компаньоном
        public int maxCompanionAffinity = 100;
        public bool isNSFW;
        public int minLevel;
    }

    /// <summary>
    /// Узел диалога - одна реплика с вариантами выбора
    /// </summary>
    [Serializable]
    public class DialogueNode
    {
        public string nodeId;
        public DialogueSpeakerType speakerType; // NPC говорит, игрок говорит, описательный текст
        public string speakerId; // если NPC - ID, если игрок - пусто

        [TextArea(3, 10)]
        public string text; // текст реплики

        // Варианты ответа игрока
        public List<DialogueChoice> choices = new();

        // Автоматический переход (если нет выборов)
        public string autoAdvanceToNodeId;
        public float autoAdvanceDelay = 2f;

        // Действия при входе в узел
        public List<DialogueAction> onEnterActions = new();

        // Визуал/звук
        public string emotion; // эмоция для портрета
        public string voiceLineId;
        public string backgroundOverride;

        // LLM
        public bool isLLMModifiable; // может ли LLM менять текст этого узла
        public string llmContextHint; // подсказка для LLM, в каком ключе менять

        // Тег квеста, который устанавливается этим узлом
        public string questTagOnSelect;

        // Пассивные проверки (стиль Pathfinder/Disco Elysium)
        public SkillCheckData passiveSkillCheck;
        [TextArea(2, 5)]
        public string passiveSuccessText;
        [TextArea(2, 5)]
        public string passiveFailureText;
        public string passiveSuccessFlag;

        public bool IsEndNode => choices.Count == 0 && string.IsNullOrEmpty(autoAdvanceToNodeId);
    }

    public enum DialogueSpeakerType
    {
        NPC,
        Player,
        Narrator
    }

    /// <summary>
    /// Вариант выбора в диалоге
    /// </summary>
    [Serializable]
    public class DialogueChoice
    {
        public string choiceId;

        [TextArea(2, 5)]
        public string text; // текст варианта

        // Проверка навыка
        public SkillCheckData skillCheck;

        // Куда ведёт этот выбор
        public string nextNodeId;

        // Действия при выборе
        public List<DialogueAction> onChooseActions = new();

        // Условия отображения
        public List<ChoiceCondition> conditions = new();

        // Если выбор недоступен, но виден (серый)
        public bool showWhenUnavailable = true;
        public string unavailableTooltip;

        // LLM разблокировка
        public bool isHiddenByDefault;
        public LLMUnlockCondition llmUnlockCondition;

        // Тег квеста
        public string questTag;

        // Для NSFW контента
        public bool isNSFWChoice;
        public string nsfwSceneId;
    }

    /// <summary>
    /// Данные проверки навыка
    /// </summary>
    [Serializable]
    public class SkillCheckData
    {
        public Character.SkillType skillType;
        public int difficultyClass; // SL (сложность)
        public bool isContested; // встречная проверка
        public string contestedWithSkill; // для встречных
        public bool allowRetry;
        public string successNodeId; // отдельный узел при успехе
        public string failureNodeId; // отдельный узел при провале

        public bool HasCheck => difficultyClass > 0;
    }

    /// <summary>
    /// Условие отображения выбора
    /// </summary>
    [Serializable]
    public class ChoiceCondition
    {
        public ConditionType conditionType;
        public string parameter; // флаг, itemId, questId и т.д.
        public string value;
        public bool invert; // инвертировать условие

        public bool Evaluate()
        {
            bool result = conditionType switch
            {
                ConditionType.HasFlag => Core.GameManager.Instance.GetFlag(parameter),
                ConditionType.HasItem => CheckHasItem(parameter),
                ConditionType.HasQuest => CheckHasQuest(parameter, value),
                ConditionType.HasGold => CheckHasGold(int.Parse(value)),
                ConditionType.AttributeCheck => CheckAttribute(parameter, int.Parse(value)),
                ConditionType.CompanionAffinity => CheckCompanionAffinity(parameter, int.Parse(value)),
                ConditionType.CompanionPresent => CheckCompanionPresent(parameter),
                ConditionType.TimeOfDay => CheckTimeOfDay(parameter),
                ConditionType.LevelCheck => CheckLevel(int.Parse(!string.IsNullOrEmpty(value) ? value : parameter)),
                ConditionType.RaceCheck => CheckRace(!string.IsNullOrEmpty(value) ? value : parameter),
                ConditionType.ClassCheck => CheckClass(!string.IsNullOrEmpty(value) ? value : parameter),
                ConditionType.GenderCheck => CheckGender(!string.IsNullOrEmpty(value) ? value : parameter),
                ConditionType.RaceOrClassCheck => CheckRaceOrClass(!string.IsNullOrEmpty(value) ? value : parameter),
                _ => true
            };
            return invert ? !result : result;
        }

        private bool CheckHasItem(string itemId)
        {
            // Проверяем у игрока
            var player = Character.CharacterCreation.Instance?.Character;
            return player != null && player.HasItem(itemId);
        }

        private bool CheckHasQuest(string questId, string state)
        {
            // Делегируем QuestManager
            return Quest.QuestManager.Instance?.CheckQuestState(questId, state) ?? false;
        }

        private bool CheckHasGold(int amount)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            return player != null && player.gold >= amount;
        }

        private bool CheckAttribute(string attribute, int minValue)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            if (player == null) return false;

            var attr = Enum.Parse<Character.AttributeType>(attribute);
            return player.stats.GetAttributeValue(attr) >= minValue;
        }

        private bool CheckCompanionAffinity(string companionId, int minAffinity)
        {
            return (Companion.CompanionManager.Instance?.GetAffinity(companionId) ?? 0) >= minAffinity;
        }

        private bool CheckCompanionPresent(string companionId)
        {
            return Companion.CompanionManager.Instance?.IsCompanionInParty(companionId) ?? false;
        }

        private bool CheckTimeOfDay(string timeOfDay)
        {
            var gm = Core.GameManager.Instance;
            if (gm == null) return true;
            return timeOfDay.ToLower() switch
            {
                "day" => !gm.IsNight,
                "night" => gm.IsNight,
                _ => true
            };
        }

        private bool CheckLevel(int minLevel)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            return player != null && player.stats.level >= minLevel;
        }

        private bool CheckRace(string raceName)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            if (player == null || string.IsNullOrEmpty(raceName)) return false;
            string[] parts = raceName.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string playerRace = player.race.ToString();
            foreach (var p in parts)
            {
                if (playerRace.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool CheckClass(string className)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            if (player == null || string.IsNullOrEmpty(className)) return false;
            string[] parts = className.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string playerClass = player.characterClass.ToString();
            foreach (var p in parts)
            {
                if (playerClass.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool CheckGender(string genderName)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            if (player == null || string.IsNullOrEmpty(genderName)) return false;
            string[] parts = genderName.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string playerGender = player.gender.ToString();
            foreach (var p in parts)
            {
                if (playerGender.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool CheckRaceOrClass(string names)
        {
            var player = Character.CharacterCreation.Instance?.Character;
            if (player == null || string.IsNullOrEmpty(names)) return false;
            string[] parts = names.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string playerRace = player.race.ToString();
            string playerClass = player.characterClass.ToString();
            foreach (var p in parts)
            {
                string clean = p.Trim();
                if (playerRace.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                    playerClass.Equals(clean, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    public enum ConditionType
    {
        HasFlag,
        HasItem,
        HasQuest,
        HasGold,
        AttributeCheck,
        CompanionAffinity,
        CompanionPresent,
        TimeOfDay,
        LevelCheck,
        RaceCheck,
        ClassCheck,
        GenderCheck,
        RaceOrClassCheck
    }

    /// <summary>
    /// Действие, выполняемое при диалоге
    /// </summary>
    [Serializable]
    public class DialogueAction
    {
        public DialogueActionType actionType;
        public string parameter1;
        public string parameter2;
        public int intValue;
        public bool boolValue;
    }

    public enum DialogueActionType
    {
        SetFlag,
        ClearFlag,
        GiveItem,
        RemoveItem,
        AddGold,
        RemoveGold,
        GiveExperience,
        SetQuestTag,
        AdvanceQuest,
        ChangeAffinity,
        StartCombat,
        OpenShop,
        PlayAnimation,
        PlaySound,
        ChangeScene,
        TriggerEvent,
        UnlockAbility,
        ApplyEffect,
        StartDialogue
    }

    /// <summary>
    /// Условие разблокировки скрытого варианта через LLM
    /// </summary>
    [Serializable]
    public class LLMUnlockCondition
    {
        public string requiredFlag; // флаг, который должен быть установлен в LLM разговоре
        public string requiredPersonalityShift; // требуемый сдвиг личности
        public int minAffinityChange;
        public string description; // что покажется игроку в tooltip
    }

    /// <summary>
    /// Модификация диалога от LLM
    /// </summary>
    [Serializable]
    public class LLMDialogueModification
    {
        public string modificationId;
        public string targetNodeId;
        public string modifiedText;
        public string triggerFlag; // флаг, при наличии которого применяется
        public string tone; // тон речи (aggressive, submissive, flirtatious, etc.)
        public bool affectsChoices; // меняет ли доступность выборов
        public List<string> disabledChoiceIds = new();
        public List<string> enabledChoiceIds = new();
    }
}
