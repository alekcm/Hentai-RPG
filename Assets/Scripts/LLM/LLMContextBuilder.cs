using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using RPG.Character;
using RPG.Companion;
using RPG.Core;

namespace RPG.LLM
{
    /// <summary>
    /// Строит системный промпт и контекст для LLM-разговора с компаньонами.
    /// Учитывает характер, текущие отношения, историю, доступные флаги.
    /// </summary>
    public class LLMContextBuilder
    {
        /// <summary>
        /// Построить полный системный промпт для разговора с компаньоном
        /// </summary>
        public static string BuildSystemPrompt(CompanionLLMContext context, ConversationSettings settings)
        {
            var sb = new StringBuilder();

            // Роль и ограничения
            sb.AppendLine("Ты — персонаж в RPG-игре. Ты должна оставаться в роли и отвечать от первого лица.");
            sb.AppendLine("ВАЖНО: Ты НЕ создаёшь новые сюжетные события, квесты или предметы.");
            sb.AppendLine("Ты реагируешь только на слова игрока в рамках своего характера и текущих отношений.");
            sb.AppendLine();

            // Персонаж
            sb.AppendLine($"## Твой персонаж: {context.displayName}");
            sb.AppendLine($"Характер: {context.personality}");
            if (!string.IsNullOrEmpty(context.backstory))
                sb.AppendLine($"Предыстория: {context.backstory}");
            sb.AppendLine();

            // Текущее состояние
            sb.AppendLine($"## Текущее состояние:");
            sb.AppendLine($"Настроение: {context.currentMood ?? "спокойное"}");
            sb.AppendLine($"Уровень отношений: {context.relationship} ({context.affinity}/100)");

            if (context.isRomanced)
                sb.AppendLine($"Романтические отношения: активны (стадия {context.romanceStage})");

            sb.AppendLine($"Тон общения: {context.dialogueTone}");
            sb.AppendLine();

            // Недавние события
            if (context.recentEvents.Count > 0)
            {
                sb.AppendLine("## Недавние события:");
                foreach (var ev in context.recentEvents)
                    sb.AppendLine($"- {ev}");
                sb.AppendLine();
            }

            // История разговоров
            if (context.conversationHistory.Count > 0)
            {
                sb.AppendLine("## Краткая история ваших разговоров:");
                int start = Math.Max(0, context.conversationHistory.Count - 5);
                for (int i = start; i < context.conversationHistory.Count; i++)
                    sb.AppendLine($"- {context.conversationHistory[i]}");
                sb.AppendLine();
            }

            // NSFW настройки (если разрешено)
            if (settings.allowNSFW && !string.IsNullOrEmpty(context.nsfwPreferences))
            {
                sb.AppendLine("## Интимные предпочтения:");
                sb.AppendLine(context.nsfwPreferences);
                sb.AppendLine("Отвечай соответственно уровню ваших отношений.");
                sb.AppendLine();
            }

            // Доступные флаги (что LLM может "разблокировать")
            if (context.availableFlags.Count > 0)
            {
                sb.AppendLine("## Доступные сюжетные маркеры:");
                sb.AppendLine("Если разговор логически приводит к одному из этих состояний, ");
                sb.AppendLine("включи соответствующий маркер в свой ответ (в квадратных скобках).");
                foreach (var flag in context.availableFlags)
                    sb.AppendLine($"- [{flag}]");
                sb.AppendLine();
            }

            // Разблокированные способности
            if (context.unlockedTraits.Count > 0)
            {
                sb.AppendLine($"## Особые черты: {string.Join(", ", context.unlockedTraits)}");
                sb.AppendLine();
            }

            // Инструкции по форматированию
            sb.AppendLine("## Формат ответа:");
            sb.AppendLine("Отвечай обычным текстом от первого лица.");
            if (context.availableFlags.Count > 0)
            {
                sb.AppendLine("Если нужно установить маркер, добавь его в конце в формате: [FLAG:flag_name]");
            }
            if (settings.generateAffinityChange)
            {
                sb.AppendLine("В конце ответа укажи изменение отношения: [AFFINITY:+2] или [AFFINITY:-1]");
                sb.AppendLine("Диапазон: от -5 до +5 за одно сообщение.");
            }
            sb.AppendLine();

            // Ограничения длины
            sb.AppendLine($"Отвечай кратко, максимум {settings.maxResponseLength} слов. " +
                         "Не описывай действия игрока, только свои реакции и слова.");

            return sb.ToString();
        }

        /// <summary>
        /// Построить промпт для анализа диалога (модификация существующих диалогов)
        /// </summary>
        public static string BuildDialogueAnalysisPrompt(
            CompanionLLMContext context,
            string dialogueText,
            List<string> availableChoices,
            List<string> hiddenChoices)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Ты — система модификации диалогов для RPG-игры.");
            sb.AppendLine("Тебе дан текущий диалог с NPC и набор вариантов ответа.");
            sb.AppendLine("На основе истории взаимодействия игрока с персонажем, ");
            sb.AppendLine("определи, какие скрытые варианты должны стать доступны, ");
            sb.AppendLine("и какие обычные варианты должны быть заблокированы.");
            sb.AppendLine();

            sb.AppendLine($"## Персонаж: {context.displayName}");
            sb.AppendLine($"Характер: {context.personality}");
            sb.AppendLine($"Отношения: {context.relationship} ({context.affinity}/100)");
            sb.AppendLine($"Настроение: {context.currentMood}");
            sb.AppendLine();

            if (context.conversationHistory.Count > 0)
            {
                sb.AppendLine("## История общения:");
                foreach (var hist in context.conversationHistory)
                    sb.AppendLine($"- {hist}");
                sb.AppendLine();
            }

            sb.AppendLine("## Текущий диалог:");
            sb.AppendLine(dialogueText);
            sb.AppendLine();

            sb.AppendLine("## Доступные варианты ответа:");
            for (int i = 0; i < availableChoices.Count; i++)
                sb.AppendLine($"{i + 1}. {availableChoices[i]}");
            sb.AppendLine();

            if (hiddenChoices.Count > 0)
            {
                sb.AppendLine("## Скрытые варианты (могут быть разблокированы):");
                for (int i = 0; i < hiddenChoices.Count; i++)
                    sb.AppendLine($"{i + 1}. {hiddenChoices[i]}");
                sb.AppendLine();
            }

            sb.AppendLine("## Ответь в JSON формате:");
            sb.AppendLine("{");
            sb.AppendLine("  \"disabled_choices\": [0, 2], // индексы заблокированных (0-based)");
            sb.AppendLine("  \"enabled_hidden\": [0], // индексы разблокированных скрытых (0-based)");
            sb.AppendLine("  \"tone_modifier\": \"submissive\", // тон: aggressive, submissive, flirtatious, neutral, dominant");
            sb.AppendLine("  \"reason\": \"После того как игрок согласился быть 'собачкой'...\"");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Построить промпт для генерации резюме диалога
        /// </summary>
        public static string BuildSummaryPrompt(
            string companionName,
            List<DialogueTurn> conversation)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Сожми следующий диалог между игроком и персонажем в 2-3 предложения.");
            sb.AppendLine("Сохраняй только ключевые решения и эмоциональные сдвиги.");
            sb.AppendLine($"Персонаж: {companionName}");
            sb.AppendLine();
            sb.AppendLine("## Диалог:");

            foreach (var turn in conversation)
            {
                sb.AppendLine($"{turn.speaker}: {turn.text}");
            }

            sb.AppendLine();
            sb.AppendLine("Резюме (2-3 предложения):");

            return sb.ToString();
        }

        /// <summary>
        /// Построить промпт для определения сдвига личности
        /// </summary>
        public static string BuildPersonalityShiftPrompt(
            CompanionLLMContext context,
            List<DialogueTurn> conversation,
            List<string> possibleShifts)
        {
            var sb = new StringBuilder();

            sb.AppendLine("На основе разговора определи, произошёл ли сдвиг в поведении персонажа.");
            sb.AppendLine($"Персонаж: {context.displayName}");
            sb.AppendLine($"Текущий характер: {context.personality}");
            sb.AppendLine($"Текущее настроение: {context.currentMood}");
            sb.AppendLine();

            sb.AppendLine("## Разговор:");
            foreach (var turn in conversation)
                sb.AppendLine($"{turn.speaker}: {turn.text}");
            sb.AppendLine();

            sb.AppendLine("## Возможные сдвиги:");
            foreach (var shift in possibleShifts)
                sb.AppendLine($"- {shift}");
            sb.AppendLine();

            sb.AppendLine("Ответь в JSON:");
            sb.AppendLine("{");
            sb.AppendLine("  \"shift_occurred\": true/false,");
            sb.AppendLine("  \"shift_description\": \"описание или null\",");
            sb.AppendLine("  \"affinity_change\": -5..+5,");
            sb.AppendLine("  \"new_mood\": \"краткое описание нового настроения\"");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }

    #region Settings & Types

    [Serializable]
    public class ConversationSettings
    {
        public bool allowNSFW = true;
        public bool generateAffinityChange = true;
        public int maxResponseLength = 100;
        public int maxConversationTurns = 30;
        public bool useStreaming = false;
    }

    [Serializable]
    public class DialogueTurn
    {
        public string speaker; // "player" или companionId
        public string text;
        public float timestamp;
    }

    /// <summary>
    /// Результат анализа диалога от LLM (для модификации существующих диалогов)
    /// </summary>
    [Serializable]
    public class DialogueAnalysisResult
    {
        public List<int> disabledChoices = new();
        public List<int> enabledHidden = new();
        public string toneModifier;
        public string reason;
    }

    /// <summary>
    /// Результат определения сдвига личности
    /// </summary>
    [Serializable]
    public class PersonalityShiftResult
    {
        public bool shiftOccurred;
        public string shiftDescription;
        public int affinityChange;
        public string newMood;
    }

    #endregion
}
