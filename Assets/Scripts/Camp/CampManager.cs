using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;
using RPG.Companion;
using RPG.LLM;

namespace RPG.Camp
{
    /// <summary>
    /// Система лагеря. Место отдыха, где игрок может:
    /// - Отдохнуть (восстановить HP/MP)
    /// - Поговорить с компаньонами (в том числе через LLM)
    /// - Управлять инвентарём
    /// - Просмотреть квесты
    /// </summary>
    public class CampManager : MonoBehaviour
    {
        public static CampManager Instance { get; private set; }

        [Header("Camp Settings")]
        [SerializeField] private int longRestHPRecovery = 100; // процент
        [SerializeField] private int longRestMPRecovery = 100;
        [SerializeField] private float longRestTimeHours = 8f;
        [SerializeField] private int campSuppliesCost = 1;

        [Header("Camp Events")]
        [SerializeField] private float randomEventChance = 0.15f;

        private bool isCamping;
        private List<string> triggeredCampEvents = new();

        public bool IsCamping => isCamping;

        public event Action OnCampStarted;
        public event Action OnCampEnded;
        public event Action<string> OnCampEventTriggered;

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
            GameManager.Instance.EventBus.OnCampStarted += () => { };
        }

        #region Camp Lifecycle

        public bool EnterCamp()
        {
            if (isCamping) return false;

            // Проверяем наличие припасов
            var player = Character.CharacterCreation.Instance?.Character;
            if (player != null && !player.HasItem("camp_supplies"))
            {
                Debug.LogWarning("[Camp] No camp supplies!");
                return false;
            }

            // Используем припасы
            player?.RemoveItem("camp_supplies");

            isCamping = true;
            GameManager.Instance.SetGameState(GameState.Camp);
            GameManager.Instance.EventBus.RaiseCampStarted();
            OnCampStarted?.Invoke();

            // Продвигаем время
            GameManager.Instance.AdvanceTime(1f);

            // Проверяем случайные события
            CheckRandomCampEvents();

            Debug.Log("[Camp] Entered camp");
            return true;
        }

        public void LeaveCamp()
        {
            if (!isCamping) return;

            isCamping = false;
            GameManager.Instance.SetGameState(GameState.Exploration);
            GameManager.Instance.EventBus.RaiseCampEnded();
            OnCampEnded?.Invoke();

            Debug.Log("[Camp] Left camp");
        }

        #endregion

        #region Rest

        public void LongRest()
        {
            if (!isCamping) return;

            var player = Character.CharacterCreation.Instance?.Character;
            if (player != null)
            {
                int hpRecovery = (player.stats.maxHP * longRestHPRecovery) / 100;
                int mpRecovery = (player.stats.maxMP * longRestMPRecovery) / 100;

                player.stats.Heal(hpRecovery);
                player.stats.RestoreMP(mpRecovery);
            }

            // Восстанавливаем компаньонов
            var companions = CompanionManager.Instance.GetPartyMembers();
            foreach (var companion in companions)
            {
                if (companion.stats != null)
                {
                    companion.stats.currentHP = companion.stats.maxHP;
                    companion.stats.currentMP = companion.stats.maxMP;
                }
            }

            // Продвигаем время
            GameManager.Instance.AdvanceTime(longRestTimeHours);

            // Триггерим пост-рест ивенты (как в BG3)
            TriggerPostRestEvents();

            Debug.Log("[Camp] Long rest completed");
        }

        public void ShortRest()
        {
            if (!isCamping) return;

            var player = Character.CharacterCreation.Instance?.Character;
            if (player != null)
            {
                int hpRecovery = player.stats.maxHP / 2;
                player.stats.Heal(hpRecovery);
            }

            GameManager.Instance.AdvanceTime(1f);
            Debug.Log("[Camp] Short rest completed");
        }

        #endregion

        #region Camp Events

        private void CheckRandomCampEvents()
        {
            if (UnityEngine.Random.value > randomEventChance)
                return;

            // Случайное событие в лагере
            var possibleEvents = GetAvailableCampEvents();
            if (possibleEvents.Count == 0) return;

            var selectedEvent = possibleEvents[UnityEngine.Random.Range(0, possibleEvents.Count)];
            TriggerCampEvent(selectedEvent);
        }

        private void TriggerPostRestEvents()
        {
            // Проверяем, есть ли у компаньонов что сказать
            var companions = CompanionManager.Instance.GetPartyMembers();
            foreach (var companion in companions)
            {
                // Проверяем триггеры для пост-рест диалогов
                if (ShouldTriggerCampDialogue(companion))
                {
                    GameManager.Instance.SetFlag(
                        $"camp_dialogue_available_{companion.companionId}");
                }
            }
        }

        private bool ShouldTriggerCampDialogue(CompanionData companion)
        {
            // Триггеры для диалога в лагере
            if (companion.affinity >= 50 && !GameManager.Instance.GetFlag(
                $"camp_talk_{companion.companionId}_done"))
                return true;

            if (companion.isRomanced && companion.romanceStage < 3)
                return true;

            return false;
        }

        private List<string> GetAvailableCampEvents()
        {
            var events = new List<string>();

            // Примеры событий
            if (!GameManager.Instance.GetFlag("camp_dream_1"))
                events.Add("camp_dream_1");
            if (!GameManager.Instance.GetFlag("camp_visitor_1"))
                events.Add("camp_visitor_1");

            return events;
        }

        private void TriggerCampEvent(string eventId)
        {
            triggeredCampEvents.Add(eventId);
            GameManager.Instance.SetFlag(eventId);
            OnCampEventTriggered?.Invoke(eventId);
        }

        #endregion

        #region Companion Interactions

        public bool CanTalkToCompanion(string companionId)
        {
            if (!isCamping) return false;
            return CompanionManager.Instance.IsCompanionInParty(companionId);
        }

        public void StartCompanionLLMTalk(string companionId)
        {
            if (!CanTalkToCompanion(companionId))
            {
                Debug.LogWarning($"[Camp] Cannot talk to {companionId}");
                return;
            }

            LLMDialogueProcessor.Instance?.StartConversation(companionId);
        }

        /// <summary>
        /// Проанализировать все сюжетные диалоги компаньона перед квестом
        /// </summary>
        public void PreAnalyzeCompanionDialogue(string companionId, string questId)
        {
            var companion = CompanionManager.Instance.GetCompanion(companionId);
            if (companion == null || companion.conversationSummaries.Count == 0)
                return;

            // Находим диалоги связанные с этим квестом
            // и вызываем LLM для их анализа
            LLMDialogueProcessor.Instance?.AnalyzeAndModifyDialogue(
                companionId,
                $"quest_{questId}_dialogue",
                (result) =>
                {
                    if (result != null && (result.disabledChoices.Count > 0 ||
                        result.enabledHidden.Count > 0))
                    {
                        Debug.Log($"[Camp] Dialogue modified for quest {questId}: " +
                                  $"disabled={result.disabledChoices.Count}, " +
                                  $"enabled={result.enabledHidden.Count}");
                    }
                });
        }

        #endregion
    }

    [Serializable]
    public class CampEvent
    {
        public string eventId;
        public string displayName;
        public string description;
        public List<string> requiredFlags = new();
        public List<string> forbiddenFlags = new();
        public string triggeredDialogueId;
        public float weight = 1f;
    }
}
