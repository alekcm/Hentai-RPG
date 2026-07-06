using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Core
{
    /// <summary>
    /// Шина событий для связи между системами без прямых зависимостей.
    /// Все системы подписываются на нужные события и реагируют.
    /// </summary>
    public class GameEventBus : MonoBehaviour
    {
        #region Game State Events

        public event Action<GameState, GameState> OnGameStateChanged;
        public void RaiseGameStateChanged(GameState oldState, GameState newState)
            => OnGameStateChanged?.Invoke(oldState, newState);

        #endregion

        #region Time Events

        public event Action<float, int> OnTimeChanged;
        public void RaiseTimeChanged(float hours, int day)
            => OnTimeChanged?.Invoke(hours, day);

        public event Action<int> OnNewDay;
        public void RaiseNewDay(int day)
            => OnNewDay?.Invoke(day);

        #endregion

        #region Global Flag Events

        public event Action<string, bool> OnGlobalFlagChanged;
        public void RaiseGlobalFlagChanged(string flag, bool value)
            => OnGlobalFlagChanged?.Invoke(flag, value);

        #endregion

        #region Dialogue Events

        public event Action<string> OnDialogueStarted;
        public void RaiseDialogueStarted(string dialogueId)
            => OnDialogueStarted?.Invoke(dialogueId);

        public event Action<string> OnDialogueEnded;
        public void RaiseDialogueEnded(string dialogueId)
            => OnDialogueEnded?.Invoke(dialogueId);

        public event Action<string, int> OnDialogueChoiceMade;
        public void RaiseDialogueChoiceMade(string dialogueId, int choiceIndex)
            => OnDialogueChoiceMade?.Invoke(dialogueId, choiceIndex);

        public event Action<string, bool> OnSkillCheckResult;
        public void RaiseSkillCheckResult(string checkName, bool success)
            => OnSkillCheckResult?.Invoke(checkName, success);

        #endregion

        #region Quest Events

        public event Action<string> OnQuestStarted;
        public void RaiseQuestStarted(string questId)
            => OnQuestStarted?.Invoke(questId);

        public event Action<string, string> OnQuestCompleted;
        public void RaiseQuestCompleted(string questId, string endingTag)
            => OnQuestCompleted?.Invoke(questId, endingTag);

        public event Action<string> OnQuestFailed;
        public void RaiseQuestFailed(string questId)
            => OnQuestFailed?.Invoke(questId);

        public event Action<string, string> OnQuestTagSet;
        public void RaiseQuestTagSet(string questId, string tag)
            => OnQuestTagSet?.Invoke(questId, tag);

        public event Action<string, string> OnQuestNodeReached;
        public void RaiseQuestNodeReached(string questId, string nodeId)
            => OnQuestNodeReached?.Invoke(questId, nodeId);

        #endregion

        #region Companion Events

        public event Action<string> OnCompanionJoined;
        public void RaiseCompanionJoined(string companionId)
            => OnCompanionJoined?.Invoke(companionId);

        public event Action<string> OnCompanionLeft;
        public void RaiseCompanionLeft(string companionId)
            => OnCompanionLeft?.Invoke(companionId);

        public event Action<string, int> OnCompanionAffinityChanged;
        public void RaiseCompanionAffinityChanged(string companionId, int newAffinity)
            => OnCompanionAffinityChanged?.Invoke(companionId, newAffinity);

        public event Action<string, string> OnCompanionTraitUnlocked;
        public void RaiseCompanionTraitUnlocked(string companionId, string traitId)
            => OnCompanionTraitUnlocked?.Invoke(companionId, traitId);

        #endregion

        #region LLM Events

        public event Action<string> OnLLMConversationStarted;
        public void RaiseLLMConversationStarted(string companionId)
            => OnLLMConversationStarted?.Invoke(companionId);

        public event Action<string, LLMConversationResult> OnLLMConversationEnded;
        public void RaiseLLMConversationEnded(string companionId, LLMConversationResult result)
            => OnLLMConversationEnded?.Invoke(companionId, result);

        public event Action<string> OnLLMDialougeModified;
        public void RaiseLLMDialougeModified(string dialogueId)
            => OnLLMDialougeModified?.Invoke(dialogueId);

        #endregion

        #region Combat Events

        public event Action OnCombatStarted;
        public void RaiseCombatStarted()
            => OnCombatStarted?.Invoke();

        public event Action<bool> OnCombatEnded;
        public void RaiseCombatEnded(bool playerWon)
            => OnCombatEnded?.Invoke(playerWon);

        public event Action<string, int> OnDamageDealt;
        public void RaiseDamageDealt(string targetId, int damage)
            => OnDamageDealt?.Invoke(targetId, damage);

        public event Action<string> OnCharacterDied;
        public void RaiseCharacterDied(string characterId)
            => OnCharacterDied?.Invoke(characterId);

        #endregion

        #region Character Events

        public event Action<string, int> OnExperienceGained;
        public void RaiseExperienceGained(string characterId, int amount)
            => OnExperienceGained?.Invoke(characterId, amount);

        public event Action<string, int> OnLevelUp;
        public void RaiseLevelUp(string characterId, int newLevel)
            => OnLevelUp?.Invoke(characterId, newLevel);

        public event Action<string, string> OnItemAcquired;
        public void RaiseItemAcquired(string characterId, string itemId)
            => OnItemAcquired?.Invoke(characterId, itemId);

        public event Action<string, string> OnItemLost;
        public void RaiseItemLost(string characterId, string itemId)
            => OnItemLost?.Invoke(characterId, itemId);

        #endregion

        #region Camp Events

        public event Action OnCampStarted;
        public void RaiseCampStarted()
            => OnCampStarted?.Invoke();

        public event Action OnCampEnded;
        public void RaiseCampEnded()
            => OnCampEnded?.Invoke();

        #endregion
    }

    /// <summary>
    /// Результат LLM-разговора, который может повлиять на игру
    /// </summary>
    [Serializable]
    public class LLMConversationResult
    {
        public string companionId;
        public int affinityChange;
        public List<string> unlockedFlags = new();
        public List<string> setFlags = new();
        public string personalityShift; // краткое описание сдвига в поведении
        public bool hadNSFWContent;
        public string summary; // сжатое резюме разговора

        public bool HasGameplayImpact =>
            affinityChange != 0 ||
            unlockedFlags.Count > 0 ||
            setFlags.Count > 0 ||
            !string.IsNullOrEmpty(personalityShift);
    }
}
