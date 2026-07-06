using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;
using RPG.Character;

namespace RPG.Companion
{
    /// <summary>
    /// Менеджер компаньонов. Управляет наймом, отношениями, личными квестами
    /// и интеграцией с LLM-системой.
    /// </summary>
    public class CompanionManager : MonoBehaviour, ISaveable
    {
        public static CompanionManager Instance { get; private set; }

        [Header("Party Settings")]
        [SerializeField] private int maxPartySize = 4;

        // Все доступные компаньоны
        private Dictionary<string, CompanionData> allCompanions = new();

        // Текущий отряд
        private List<string> partyMembers = new();

        public string SaveKey => "CompanionManager";

        public event Action<string> OnCompanionRecruited;
        public event Action<string> OnCompanionDismissed;
        public event Action<string, int> OnAffinityChanged;
        public event Action<string, string> OnAbilityUnlocked;
        public event Action<string> OnCompanionRomanceStarted;

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

        #region Companion Registration

        public void RegisterCompanion(CompanionData companion)
        {
            allCompanions[companion.companionId] = companion;
        }

        public void RegisterCompanions(List<CompanionData> companions)
        {
            foreach (var c in companions)
                allCompanions[c.companionId] = c;
        }

        public CompanionData GetCompanion(string companionId)
        {
            return allCompanions.TryGetValue(companionId, out var c) ? c : null;
        }

        public List<CompanionData> GetAllCompanions()
        {
            return new List<CompanionData>(allCompanions.Values);
        }

        #endregion

        #region Party Management

        public bool AddToParty(string companionId)
        {
            if (partyMembers.Count >= maxPartySize)
            {
                Debug.LogWarning("[CompanionManager] Party is full");
                return false;
            }

            if (partyMembers.Contains(companionId))
                return false;

            if (!allCompanions.ContainsKey(companionId))
                return false;

            partyMembers.Add(companionId);
            GameManager.Instance.EventBus.RaiseCompanionJoined(companionId);
            OnCompanionRecruited?.Invoke(companionId);
            return true;
        }

        public bool RemoveFromParty(string companionId)
        {
            if (!partyMembers.Remove(companionId))
                return false;

            GameManager.Instance.EventBus.RaiseCompanionLeft(companionId);
            OnCompanionDismissed?.Invoke(companionId);
            return true;
        }

        public bool IsCompanionInParty(string companionId)
        {
            return partyMembers.Contains(companionId);
        }

        public List<CompanionData> GetPartyMembers()
        {
            var members = new List<CompanionData>();
            foreach (var id in partyMembers)
            {
                if (allCompanions.TryGetValue(id, out var c))
                    members.Add(c);
            }
            return members;
        }

        public int GetPartySize() => partyMembers.Count;
        public int GetMaxPartySize() => maxPartySize;

        #endregion

        #region Affinity System

        public int GetAffinity(string companionId)
        {
            if (allCompanions.TryGetValue(companionId, out var c))
                return c.affinity;
            return 0;
        }

        public void ModifyAffinity(string companionId, int change)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return;

            int oldAffinity = c.affinity;
            c.affinity = Mathf.Clamp(c.affinity + change, -100, 100);

            if (c.affinity != oldAffinity)
            {
                GameManager.Instance.EventBus.RaiseCompanionAffinityChanged(
                    companionId, c.affinity);
                OnAffinityChanged?.Invoke(companionId, c.affinity);

                // Проверяем пороги
                CheckAffinityThresholds(c, oldAffinity, c.affinity);
            }
        }

        private void CheckAffinityThresholds(CompanionData companion, int oldAffinity, int newAffinity)
        {
            // Romance threshold
            if (oldAffinity < 75 && newAffinity >= 75 && !companion.isRomanced)
            {
                companion.romanceAvailable = true;
                Debug.Log($"[CompanionManager] Romance now available with {companion.displayName}");
            }

            // Hostile threshold
            if (oldAffinity > -50 && newAffinity <= -50)
            {
                companion.isHostile = true;
                Debug.LogWarning($"[CompanionManager] {companion.displayName} is now hostile!");
            }

            // Leave party if too hostile
            if (newAffinity <= -75 && IsCompanionInParty(companion.companionId))
            {
                RemoveFromParty(companion.companionId);
                Debug.Log($"[CompanionManager] {companion.displayName} left the party!");
            }

            // Unlock abilities at thresholds
            foreach (var threshold in companion.affinityAbilityThresholds)
            {
                if (oldAffinity < threshold.affinityRequired &&
                    newAffinity >= threshold.affinityRequired &&
                    !companion.unlockedAbilities.Contains(threshold.abilityId))
                {
                    companion.unlockedAbilities.Add(threshold.abilityId);
                    OnAbilityUnlocked?.Invoke(companion.companionId, threshold.abilityId);
                }
            }
        }

        public CompanionRelationship GetRelationshipLevel(string companionId)
        {
            int affinity = GetAffinity(companionId);
            if (affinity >= 75) return CompanionRelationship.Devoted;
            if (affinity >= 50) return CompanionRelationship.Friendly;
            if (affinity >= 25) return CompanionRelationship.Warm;
            if (affinity >= -25) return CompanionRelationship.Neutral;
            if (affinity >= -50) return CompanionRelationship.Cold;
            return CompanionRelationship.Hostile;
        }

        #endregion

        #region Romance System

        public bool CanStartRomance(string companionId)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return false;
            return c.romanceAvailable && !c.isRomanced && !c.isHostile;
        }

        public bool StartRomance(string companionId)
        {
            if (!CanStartRomance(companionId))
                return false;

            var c = allCompanions[companionId];
            c.isRomanced = true;
            c.romanceStage = 1;

            GameManager.Instance.SetFlag($"romance_{companionId}_started");
            OnCompanionRomanceStarted?.Invoke(companionId);

            return true;
        }

        public int GetRomanceStage(string companionId)
        {
            if (allCompanions.TryGetValue(companionId, out var c))
                return c.romanceStage;
            return 0;
        }

        public void AdvanceRomance(string companionId)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return;
            if (c.isRomanced)
            {
                c.romanceStage++;
                GameManager.Instance.SetFlag($"romance_{companionId}_stage_{c.romanceStage}");
            }
        }

        #endregion

        #region Abilities

        public void UnlockAbility(string companionId, string abilityId)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return;

            if (!c.unlockedAbilities.Contains(abilityId))
            {
                c.unlockedAbilities.Add(abilityId);
                OnAbilityUnlocked?.Invoke(companionId, abilityId);
                GameManager.Instance.EventBus.RaiseCompanionTraitUnlocked(companionId, abilityId);
            }
        }

        public List<string> GetUnlockedAbilities(string companionId)
        {
            if (allCompanions.TryGetValue(companionId, out var c))
                return new List<string>(c.unlockedAbilities);
            return new();
        }

        public bool HasAbility(string companionId, string abilityId)
        {
            if (allCompanions.TryGetValue(companionId, out var c))
                return c.unlockedAbilities.Contains(abilityId);
            return false;
        }

        #endregion

        #region LLM Integration

        /// <summary>
        /// Получить полный контекст компаньона для LLM
        /// </summary>
        public CompanionLLMContext GetLLMContext(string companionId)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return null;

            return new CompanionLLMContext
            {
                companionId = companionId,
                displayName = c.displayName,
                personality = c.personality,
                backstory = c.backstory,
                relationship = GetRelationshipLevel(companionId),
                affinity = c.affinity,
                isRomanced = c.isRomanced,
                romanceStage = c.romanceStage,
                currentMood = c.currentMood,
                recentEvents = new List<string>(c.recentEvents),
                conversationHistory = new List<string>(c.conversationSummaries),
                unlockedTraits = new List<string>(c.unlockedAbilities),
                nsfwPreferences = c.nsfwPreferences,
                dialogueTone = GetCurrentDialogueTone(c),
                availableFlags = GetAvailableLLMFlags(c)
            };
        }

        /// <summary>
        /// Применить результат LLM-разговора
        /// </summary>
        public void ApplyLLMResult(string companionId, LLMConversationResult result)
        {
            if (!allCompanions.TryGetValue(companionId, out var c))
                return;

            // Изменение отношения
            if (result.affinityChange != 0)
                ModifyAffinity(companionId, result.affinityChange);

            // Сохраняем резюме
            if (!string.IsNullOrEmpty(result.summary))
            {
                c.conversationSummaries.Add(result.summary);
                // Ограничиваем историю
                if (c.conversationSummaries.Count > 20)
                    c.conversationSummaries.RemoveAt(0);
            }

            // Сдвиг личности
            if (!string.IsNullOrEmpty(result.personalityShift))
            {
                c.personalityShifts.Add(result.personalityShift);
                c.currentMood = result.personalityShift;
            }

            // Флаги
            foreach (var flag in result.setFlags)
                GameManager.Instance.SetFlag(flag);

            foreach (var flag in result.unlockedFlags)
                GameManager.Instance.SetFlag(flag);

            // NSFW
            if (result.hadNSFWContent)
                c.nsfwEncounters++;
        }

        private string GetCurrentDialogueTone(CompanionData c)
        {
            if (c.affinity >= 75 && c.isRomanced) return "loving, intimate";
            if (c.affinity >= 50) return "warm, friendly";
            if (c.affinity >= 0) return "neutral, professional";
            if (c.affinity >= -25) return "cold, distant";
            return "hostile, contemptuous";
        }

        private List<string> GetAvailableLLMFlags(CompanionData c)
        {
            var flags = new List<string>();
            // Флаги, которые LLM может установить/снять
            foreach (var flag in c.llmAvailableFlags)
            {
                if (!GameManager.Instance.GetFlag(flag))
                    flags.Add(flag);
            }
            return flags;
        }

        #endregion

        #region Save/Load

        public string OnSave()
        {
            var data = new CompanionSaveData();
            data.partyMembers = new List<string>(partyMembers);

            foreach (var kvp in allCompanions)
            {
                data.companionStates.Add(new CompanionStateData
                {
                    companionId = kvp.Key,
                    affinity = kvp.Value.affinity,
                    isRomanced = kvp.Value.isRomanced,
                    romanceStage = kvp.Value.romanceStage,
                    unlockedAbilities = new List<string>(kvp.Value.unlockedAbilities),
                    conversationSummaries = new List<string>(kvp.Value.conversationSummaries),
                    personalityShifts = new List<string>(kvp.Value.personalityShifts),
                    currentMood = kvp.Value.currentMood,
                    nsfwEncounters = kvp.Value.nsfwEncounters,
                    recentEvents = new List<string>(kvp.Value.recentEvents),
                    isHostile = kvp.Value.isHostile,
                    isRecruited = kvp.Value.isRecruited
                });
            }

            return JsonUtility.ToJson(data);
        }

        public void OnLoad(string json)
        {
            var data = JsonUtility.FromJson<CompanionSaveData>(json);
            if (data == null) return;

            partyMembers = data.partyMembers ?? new();

            foreach (var state in data.companionStates)
            {
                if (allCompanions.TryGetValue(state.companionId, out var c))
                {
                    c.affinity = state.affinity;
                    c.isRomanced = state.isRomanced;
                    c.romanceStage = state.romanceStage;
                    c.unlockedAbilities = state.unlockedAbilities ?? new();
                    c.conversationSummaries = state.conversationSummaries ?? new();
                    c.personalityShifts = state.personalityShifts ?? new();
                    c.currentMood = state.currentMood;
                    c.nsfwEncounters = state.nsfwEncounters;
                    c.recentEvents = state.recentEvents ?? new();
                    c.isHostile = state.isHostile;
                    c.isRecruited = state.isRecruited;
                }
            }
        }

        #endregion
    }

    #region Data Classes

    [Serializable]
    public class CompanionData
    {
        // Основное
        public string companionId;
        public string displayName;
        public Gender gender;
        public RaceType race;
        public ClassType characterClass;

        [TextArea(5, 15)]
        public string personality;

        [TextArea(5, 15)]
        public string backstory;

        // Статы
        public CharacterStats stats;

        // Отношения
        public int affinity;
        public bool isRomanced;
        public bool romanceAvailable;
        public int romanceStage;
        public bool isHostile;
        public bool isRecruited;

        // Способности
        public List<string> unlockedAbilities = new();
        public List<AffinityAbilityThreshold> affinityAbilityThresholds = new();

        // LLM контекст
        public string currentMood;
        public List<string> recentEvents = new();
        public List<string> conversationSummaries = new();
        public List<string> personalityShifts = new();
        public List<string> llmAvailableFlags = new();

        // NSFW
        [TextArea(3, 10)]
        public string nsfwPreferences;
        public int nsfwEncounters;

        // Личный квест
        public string personalQuestId;

        // Портрет и визуал
        public string portraitSpriteId;
        public string modelId;

        // Голос
        public string voiceId;
    }

    [Serializable]
    public class AffinityAbilityThreshold
    {
        public int affinityRequired;
        public string abilityId;
        public string abilityName;
        public string description;
    }

    public enum CompanionRelationship
    {
        Hostile,
        Cold,
        Neutral,
        Warm,
        Friendly,
        Devoted
    }

    /// <summary>
    /// Контекст компаньона для передачи в LLM
    /// </summary>
    [Serializable]
    public class CompanionLLMContext
    {
        public string companionId;
        public string displayName;
        public string personality;
        public string backstory;
        public CompanionRelationship relationship;
        public int affinity;
        public bool isRomanced;
        public int romanceStage;
        public string currentMood;
        public List<string> recentEvents = new();
        public List<string> conversationHistory = new();
        public List<string> unlockedTraits = new();
        public string nsfwPreferences;
        public string dialogueTone;
        public List<string> availableFlags = new();
    }

    #endregion

    #region Save Data

    [Serializable]
    public class CompanionSaveData
    {
        public List<string> partyMembers = new();
        public List<CompanionStateData> companionStates = new();
    }

    [Serializable]
    public class CompanionStateData
    {
        public string companionId;
        public int affinity;
        public bool isRomanced;
        public int romanceStage;
        public List<string> unlockedAbilities = new();
        public List<string> conversationSummaries = new();
        public List<string> personalityShifts = new();
        public string currentMood;
        public int nsfwEncounters;
        public List<string> recentEvents = new();
        public bool isHostile;
        public bool isRecruited;
    }

    #endregion
}
