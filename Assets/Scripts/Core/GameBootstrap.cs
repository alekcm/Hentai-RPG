using UnityEngine;
using RPG.Core;
using RPG.Character;
using RPG.Domains;
using RPG.Items;
using RPG.Dialogue;
using RPG.Quest;
using RPG.Companion;
using RPG.LLM;
using RPG.Combat;
using RPG.Camp;
using RPG.Content;
using RPG.Utilities;

namespace RPG.Core
{
    /// <summary>
    /// Главный инициализатор игры. Создаёт все менеджеры и настраивает связи.
    /// Повесьте этот скрипт на пустой GameObject в первой сцене.
    /// Пример-данные компаньонов (Валерия/Грогна) и квестов раньше жили здесь;
    /// они удалены при переходе на модель ГДД. Реальные компаньоны пролога
    /// будут добавлены отдельно, вместе со сценой r3 (орк/орчиха + плутовка).
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("API Configuration")]
        [SerializeField] private string llmApiKey = "";
        [SerializeField] private LLMProvider llmProvider = LLMProvider.Local_Ollama;
        [SerializeField] private string llmModel = "hf.co/QuantFactory/Qwen2.5-7B-Instruct-Uncensored-GGUF:Q4_K_M";
        [SerializeField] private string llmApiUrl = "http://localhost:11434/api/chat";

        [Header("Game Settings")]
        [SerializeField] private bool startInCharacterCreation = true;
        [SerializeField] private bool enableDebugMode = true;

        private void Awake()
        {
            // Для Ollama по умолчанию отключаем GPU — во время разработки удобнее нагружать CPU/RAM.
            // Если работаешь на видеокарте — закомментируй строку.
            System.Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", "-1");

            EnsureManager<GameManager>("GameManager");
            EnsureManager<SaveManager>("SaveManager");
            EnsureManager<CharacterCreation>("CharacterCreation");
            EnsureManager<DialogueManager>("DialogueManager");
            EnsureManager<QuestManager>("QuestManager");
            EnsureManager<CompanionManager>("CompanionManager");
            EnsureManager<LLMManager>("LLMManager");
            EnsureManager<LLMDialogueProcessor>("LLMDialogueProcessor");
            EnsureManager<CombatManager>("CombatManager");
            EnsureManager<CampManager>("CampManager");
            EnsureManager<AudioManager>("AudioManager");
            EnsureManager<LocalizationManager>("LocalizationManager");
            EnsureManager<PrologueEncounterBridge>("PrologueEncounterBridge");

            if (enableDebugMode)
                EnsureManager<DebugManager>("DebugManager");
        }

        private void Start()
        {
            ConfigureLLM();
            LoadGameData();

            if (startInCharacterCreation)
                GameManager.Instance.SetGameState(GameState.CharacterCreation);

            if (enableDebugMode && FindObjectOfType<DialogueTestRunner>() == null)
                gameObject.AddComponent<DialogueTestRunner>();

            // Поднимаем боевой UI (в скрытом виде).
            UI.GridCombatView.EnsureExists();
            UI.CombatUI.EnsureExists();

            Debug.Log("[GameBootstrap] Game initialized successfully!");
        }

        private T EnsureManager<T>(string name) where T : MonoBehaviour
        {
            var existing = FindObjectOfType<T>();
            if (existing != null) return existing;

            var go = new GameObject(name);
            var manager = go.AddComponent<T>();
            DontDestroyOnLoad(go);
            return manager;
        }

        private void ConfigureLLM()
        {
            var llm = LLMManager.Instance;
            if (llm == null) return;

            llm.SetProvider(llmProvider, llmApiUrl, llmModel);
            if (!string.IsNullOrEmpty(llmApiKey))
                llm.SetApiKey(llmApiKey);

            Debug.Log($"[GameBootstrap] LLM configured: {llmProvider} / {llmModel}");
        }

        private void LoadGameData()
        {
            Debug.Log("[GameBootstrap] Loading game data...");

            RaceDatabase.Initialize();
            ClassDatabase.Initialize();
            DomainDatabase.Initialize();
            ItemDatabase.Initialize();

            // Компаньоны и квесты пролога подгружаются отдельными регистраторами
            // (см. Assets/Scripts/Content/PrologueContent.cs — будет позже).
        }
    }
}
