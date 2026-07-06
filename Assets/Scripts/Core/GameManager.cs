using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Core
{
    /// <summary>
    /// Главный менеджер игры. Singleton, управляет инициализацией всех подсистем
    /// и глобальным состоянием игры.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private SaveManager saveManager;
        [SerializeField] private GameEventBus eventBus;

        [Header("Game State")]
        [SerializeField] private GameState currentState = GameState.MainMenu;

        public SaveManager SaveManager => saveManager;
        public GameEventBus EventBus => eventBus;
        public GameState CurrentState => currentState;

        // Глобальные игровые флаги
        private Dictionary<string, bool> globalFlags = new();
        private Dictionary<string, int> globalCounters = new();
        private Dictionary<string, string> globalStrings = new();

        // Игровое время
        private float gameTimeHours = 8f; // начинаем в 8 утра
        private int currentDay = 1;

        public float GameTimeHours => gameTimeHours;
        public int CurrentDay => currentDay;
        public bool IsNight => gameTimeHours < 6f || gameTimeHours > 22f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (eventBus == null)
                eventBus = gameObject.AddComponent<GameEventBus>();
        }

        private void Start()
        {
            InitializeSystems();
        }

        private void InitializeSystems()
        {
            Debug.Log("[GameManager] Initializing all game systems...");
            // Все системы инициализируются через свои Awake/Start
            // GameManager лишь координирует порядок
        }

        public void SetGameState(GameState newState)
        {
            var oldState = currentState;
            currentState = newState;
            eventBus.RaiseGameStateChanged(oldState, newState);
        }

        #region Global Flags

        public void SetFlag(string flagName, bool value = true)
        {
            globalFlags[flagName] = value;
            eventBus.RaiseGlobalFlagChanged(flagName, value);
        }

        public bool GetFlag(string flagName)
        {
            return globalFlags.TryGetValue(flagName, out bool value) && value;
        }

        public void IncrementCounter(string counterName, int amount = 1)
        {
            if (!globalCounters.ContainsKey(counterName))
                globalCounters[counterName] = 0;
            globalCounters[counterName] += amount;
        }

        public int GetCounter(string counterName)
        {
            return globalCounters.TryGetValue(counterName, out int value) ? value : 0;
        }

        public void SetGlobalString(string key, string value)
        {
            globalStrings[key] = value;
        }

        public string GetGlobalString(string key, string defaultValue = "")
        {
            return globalStrings.TryGetValue(key, out string value) ? value : defaultValue;
        }

        #endregion

        #region Time

        public void AdvanceTime(float hours)
        {
            gameTimeHours += hours;
            while (gameTimeHours >= 24f)
            {
                gameTimeHours -= 24f;
                currentDay++;
                eventBus.RaiseNewDay(currentDay);
            }
            eventBus.RaiseTimeChanged(gameTimeHours, currentDay);
        }

        #endregion

        #region Save/Load Data

        public GameSaveData CollectSaveData()
        {
            return new GameSaveData
            {
                globalFlags = new Dictionary<string, bool>(globalFlags),
                globalCounters = new Dictionary<string, int>(globalCounters),
                globalStrings = new Dictionary<string, string>(globalStrings),
                gameTimeHours = gameTimeHours,
                currentDay = currentDay,
                currentState = currentState
            };
        }

        public void ApplySaveData(GameSaveData data)
        {
            globalFlags = data.globalFlags ?? new();
            globalCounters = data.globalCounters ?? new();
            globalStrings = data.globalStrings ?? new();
            gameTimeHours = data.gameTimeHours;
            currentDay = data.currentDay;
            currentState = data.currentState;
        }

        #endregion
    }

    public enum GameState
    {
        MainMenu,
        CharacterCreation,
        Exploration,
        Dialogue,
        Combat,
        Camp,
        Inventory,
        QuestLog,
        Loading,
        Paused
    }

    [Serializable]
    public class GameSaveData
    {
        public Dictionary<string, bool> globalFlags;
        public Dictionary<string, int> globalCounters;
        public Dictionary<string, string> globalStrings;
        public float gameTimeHours;
        public int currentDay;
        public GameState currentState;
    }
}
