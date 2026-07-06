using UnityEngine;
using RPG.Core;
using RPG.Character;
using RPG.Dialogue;
using RPG.Quest;
using RPG.Companion;
using RPG.LLM;
using RPG.Combat;
using RPG.Camp;
using RPG.Utilities;

namespace RPG.Core
{
    /// <summary>
    /// Главный инициализатор игры. Создаёт все менеджеры и настраивает связи.
    /// Повесьте этот скрипт на пустой GameObject в первой сцене.
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
            // Принудительно отключаем GPU для Ollama (используем только CPU/RAM)
            // Если хочешь использовать GPU — закомментируй эту строку
            System.Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", "-1");

            // Создаём все необходимые менеджеры
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

            if (enableDebugMode)
                EnsureManager<DebugManager>("DebugManager");
        }

        private void Start()
        {
            ConfigureLLM();
            LoadGameData();

            if (startInCharacterCreation)
            {
                GameManager.Instance.SetGameState(GameState.CharacterCreation);
            }

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
            // Здесь загружаются данные из JSON файлов или ScriptableObject
            // Пример:
            // var quests = Resources.Load<TextAsset>("Data/quests");
            // if (quests != null)
            //     LoadQuestsFromJson(quests.text);

            Debug.Log("[GameBootstrap] Loading game data...");

            // Инициализируем базы данных
            RaceDatabase.Initialize();
            ClassDatabase.Initialize();

            // Загружаем пример данных для демонстрации
            LoadExampleData();
        }

        private void LoadExampleData()
        {
            // Пример компаньона
            var dominatrix = new CompanionData
            {
                companionId = "companion_dominatrix",
                displayName = "Валерия",
                gender = Gender.Female,
                race = RaceType.Human,
                characterClass = ClassType.Rogue,
                personality = "Доминантная, уверенная в себе, любит контроль. " +
                             "За жёсткой внешностью скрывается заботливая натура. " +
                             "Саркастична, остроумна, не терпит слабости.",
                backstory = "Бывшая наёмница и хозяйка борделя в крупном городе. " +
                           "Присоединилась к отряду после того, как её заведение " +
                           "было уничтожено таинственным культом.",
                stats = new CharacterStats
                {
                    strength = 12,
                    dexterity = 18,
                    constitution = 14,
                    intelligence = 14,
                    wisdom = 12,
                    charisma = 16,
                    level = 3
                },
                nsfwPreferences = "Доминирование, бондаж, ролевые игры. " +
                                  "Предпочитает быть сверху. Любит дразнить. " +
                                  "Реагирует на покорность партнёра.",
                llmAvailableFlags = new()
                {
                    "valeria_pet_name",
                    "valeria_accepted_submissive",
                    "valeria_romance_stage_1",
                    "valeria_shared_secret",
                    "valeria_trusts_player"
                },
                affinityAbilityThresholds = new()
                {
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 25,
                        abilityId = "valeria_backstab_plus",
                        abilityName = "Усиленный удар в спину",
                        description = "Урон от скрытных атак увеличен"
                    },
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 50,
                        abilityId = "valeria_shadow_step",
                        abilityName = "Шаг сквозь тень",
                        description = "Телепортация к цели"
                    },
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 75,
                        abilityId = "valeria_master_command",
                        abilityName = "Приказ госпожи",
                        description = "Враги получают дебафф при виде Валерии"
                    }
                }
            };
            dominatrix.stats.RecalculateDerivedStats();
            CompanionManager.Instance.RegisterCompanion(dominatrix);

            var orcWarrior = new CompanionData
            {
                companionId = "companion_orc_warrior",
                displayName = "Грогна",
                gender = Gender.Female,
                race = RaceType.HalfOrc,
                characterClass = ClassType.Warrior,
                personality = "Сильная, агрессивная, прямолинейная. " +
                             "Обожает битвы и не понимает тонкостей дипломатии. " +
                             "Верна друзьям до смерти. Скрывает неуверенность за грубостью.",
                backstory = "Воительница орочьего клана, изгнанная за нарушение традиций. " +
                           "Ищет способ доказать свою силу и вернуть честь.",
                stats = new CharacterStats
                {
                    strength = 18,
                    dexterity = 12,
                    constitution = 16,
                    intelligence = 8,
                    wisdom = 10,
                    charisma = 10,
                    level = 3
                },
                nsfwPreferences = "Грубый и страстный стиль. Предпочитает доминировать физически. " +
                                  "Нежна только с теми, кого уважает. Не любит нежности на людях.",
                llmAvailableFlags = new()
                {
                    "grogna_taught_patience",
                    "grogna_respects_player",
                    "grogna_romance_stage_1",
                    "grogna_shared_battle_bond"
                },
                affinityAbilityThresholds = new()
                {
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 25,
                        abilityId = "grogna_rage",
                        abilityName = "Ярость",
                        description = "+4 к урону на 3 хода"
                    },
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 50,
                        abilityId = "grogna_unstoppable",
                        abilityName = "Неудержимая",
                        description = "Игнорирует первый смертельный удар"
                    },
                    new AffinityAbilityThreshold
                    {
                        affinityRequired = 75,
                        abilityId = "grogna_warcry",
                        abilityName = "Боевой клич",
                        description = "Оглушает всех врагов в радиусе"
                    }
                }
            };
            orcWarrior.stats.RecalculateDerivedStats();
            CompanionManager.Instance.RegisterCompanion(orcWarrior);

            // Пример квеста с тегами
            var banditCampQuest = new QuestDefinition
            {
                questId = "quest_bandit_camp_infiltration",
                displayName = "Бандитский лагерь",
                description = "Разведать бандитский лагерь и узнать их планы.",
                questType = QuestType.Side,
                possibleTags = new()
                {
                    "noticed_by_guards",
                    "unnoticed",
                    "guard_alive",
                    "guard_dead",
                    "stealth_approach",
                    "combat_approach",
                    "transform_approach"
                },
                objectives = new()
                {
                    new QuestObjective
                    {
                        objectiveId = "obj_reach_camp",
                        displayName = "Добраться до лагеря",
                        description = "Найдите бандитский лагерь в лесу",
                        isRequired = true
                    },
                    new QuestObjective
                    {
                        objectiveId = "obj_infiltrate",
                        displayName = "Проникнуть внутрь",
                        description = "Найдите способ попасть в лагерь незамеченным",
                        isRequired = true
                    },
                    new QuestObjective
                    {
                        objectiveId = "obj_get_intel",
                        displayName = "Разведать планы",
                        description = "Узнайте, что планируют бандиты",
                        isRequired = true
                    },
                    new QuestObjective
                    {
                        objectiveId = "obj_escape",
                        displayName = "Выбраться",
                        description = "Покиньте лагерь живым",
                        isRequired = true
                    }
                },
                endings = new()
                {
                    new QuestEnding
                    {
                        endingId = "stealth_perfect",
                        displayName = "Призрак",
                        description = "Никто вас не заметил, все живы.",
                        requiredTags = new() { "unnoticed", "guard_alive" },
                        rewards = new()
                        {
                            new QuestReward
                            {
                                rewardType = RewardType.CompanionAbility,
                                targetId = "companion_dominatrix",
                                stringValue = "valeria_shadow_master",
                                intValue = 0
                            }
                        }
                    },
                    new QuestEnding
                    {
                        endingId = "combat_victory",
                        displayName = "Воин",
                        description = "Вы пробивались с боем.",
                        requiredTags = new() { "combat_approach", "guard_dead" },
                        rewards = new()
                        {
                            new QuestReward
                            {
                                rewardType = RewardType.Artifact,
                                stringValue = "sword_of_conquest",
                                intValue = 0
                            }
                        }
                    },
                    new QuestEnding
                    {
                        endingId = "partial_success",
                        displayName = "Выживание",
                        description = "Задание выполнено, но не идеально.",
                        requiredTags = new() { },
                        rewards = new()
                        {
                            new QuestReward
                            {
                                rewardType = RewardType.Experience,
                                intValue = 200
                            }
                        }
                    }
                }
            };
            QuestManager.Instance.RegisterQuest(banditCampQuest);

            // Пример диалога с LLM-модификациями
            var valeriaDialogue = new DialogueGraph
            {
                dialogueId = "dialogue_valeria_bandit_camp",
                displayName = "Валерия: план проникновения",
                speakerId = "companion_dominatrix",
                speakerName = "Валерия",
                allowLLMModification = true,
                context = new DialogueContext
                {
                    requiredQuestId = "quest_bandit_camp_infiltration",
                    requiredQuestState = "active"
                },
                nodes = new()
                {
                    new DialogueNode
                    {
                        nodeId = "start",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Ну что, котик, как будем пробираться? У меня есть пара идей...",
                        emotion = "smirk",
                        isLLMModifiable = true,
                        llmContextHint = "Тон зависит от того, принял ли игрок роль 'питомца'",
                        choices = new()
                        {
                            new DialogueChoice
                            {
                                choiceId = "stealth",
                                text = "Пролезем через форточку, как плуты.",
                                nextNodeId = "stealth_path",
                                questTag = "stealth_approach",
                                skillCheck = new SkillCheckData
                                {
                                    skillType = SkillType.Stealth,
                                    difficultyClass = 15
                                }
                            },
                            new DialogueChoice
                            {
                                choiceId = "combat",
                                text = "Идём с боем! Перебьём их всех!",
                                nextNodeId = "combat_path",
                                questTag = "combat_approach"
                            },
                            new DialogueChoice
                            {
                                choiceId = "druid",
                                text = "Превращусь в крысу и пробегу мимо.",
                                nextNodeId = "druid_path",
                                questTag = "transform_approach",
                                conditions = new()
                                {
                                    new ChoiceCondition
                                    {
                                        conditionType = ConditionType.HasFlag,
                                        parameter = "player_is_druid"
                                    }
                                }
                            },
                            // СКРЫТЫЙ вариант - разблокируется LLM
                            new DialogueChoice
                            {
                                choiceId = "obey_valeria",
                                text = "Что прикажешь, Госпожа?",
                                isHiddenByDefault = true,
                                nextNodeId = "dominatrix_path",
                                questTag = "stealth_approach",
                                llmUnlockCondition = new LLMUnlockCondition
                                {
                                    requiredFlag = "valeria_accepted_submissive",
                                    description = "Вы согласились быть 'собачкой' Валерии"
                                }
                            }
                        }
                    },
                    new DialogueNode
                    {
                        nodeId = "stealth_path",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Хм, скрытность... Не мой стиль, но если ты настаиваешь...",
                        autoAdvanceToNodeId = "plan_details"
                    },
                    new DialogueNode
                    {
                        nodeId = "combat_path",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Вот это мне нравится! Кровь, крики, хаос!",
                        autoAdvanceToNodeId = "plan_details"
                    },
                    new DialogueNode
                    {
                        nodeId = "druid_path",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Крыса? Серьёзно? Ну ладно, хоть что-то интересное.",
                        autoAdvanceToNodeId = "plan_details"
                    },
                    new DialogueNode
                    {
                        nodeId = "dominatrix_path",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Хорошая собачка. Слушай внимательно: ты прокрадёшься, " +
                               "и я НЕ хочу слышать о провалах. Понял?",
                        isLLMModifiable = true,
                        llmContextHint = "Если игрок был покорен - тон одобрительный. " +
                                        "Если сопротивлялся - тон раздражённый.",
                        onEnterActions = new()
                        {
                            new DialogueAction
                            {
                                actionType = DialogueActionType.ChangeAffinity,
                                parameter1 = "companion_dominatrix",
                                intValue = 5
                            }
                        },
                        choices = new()
                        {
                            new DialogueChoice
                            {
                                choiceId = "obey_stealth",
                                text = "Да, Госпожа. Сделаю всё как скажете.",
                                nextNodeId = "plan_details",
                                questTag = "stealth_approach"
                            }
                        }
                    },
                    new DialogueNode
                    {
                        nodeId = "plan_details",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Отлично, тогда действуем. Главное - не подведи меня.",
                        isLLMModifiable = true
                    }
                }
            };
            DialogueManager.Instance.RegisterDialogue(valeriaDialogue);

            // Пример квеста с LLM-разблокировкой через "обучение смирению"
            var grognaDialogue = new DialogueGraph
            {
                dialogueId = "dialogue_grogna_bandit_camp",
                displayName = "Грогна: план атаки",
                speakerId = "companion_orc_warrior",
                speakerName = "Грогна",
                allowLLMModification = true,
                context = new DialogueContext
                {
                    requiredQuestId = "quest_bandit_camp_infiltration",
                    requiredQuestState = "active",
                    requiredFlags = new() { "companion_orc_warrior_in_party" }
                },
                nodes = new()
                {
                    new DialogueNode
                    {
                        nodeId = "start",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Хватит болтать! Давайте ворвёмся и перебьём их!",
                        emotion = "angry",
                        choices = new()
                        {
                            new DialogueChoice
                            {
                                choiceId = "agree_fight",
                                text = "Вперёд! Покажем им!",
                                nextNodeId = "fight_plan",
                                questTag = "combat_approach"
                            },
                            new DialogueChoice
                            {
                                choiceId = "suggest_stealth",
                                text = "Погоди, может стоит быть потише...",
                                nextNodeId = "stealth_suggestion",
                                questTag = "stealth_approach"
                            },
                            // СКРЫТЫЙ вариант - открывается если игрок научил Грогну терпению в лагере
                            new DialogueChoice
                            {
                                choiceId = "patience_check",
                                text = "Грогна, вспомни что мы обсуждали в лагере. Сдержись.",
                                isHiddenByDefault = true,
                                nextNodeId = "patience_success",
                                questTag = "stealth_approach",
                                skillCheck = new SkillCheckData
                                {
                                    skillType = SkillType.Persuasion,
                                    difficultyClass = 20,
                                    successNodeId = "patience_success",
                                    failureNodeId = "patience_fail"
                                },
                                llmUnlockCondition = new LLMUnlockCondition
                                {
                                    requiredFlag = "grogna_taught_patience",
                                    description = "Вы научили Грогну смирению в лагере"
                                },
                                unavailableTooltip = "Проверка Убеждения SL 20 (Открылось благодаря разговору в лагере)"
                            }
                        }
                    },
                    new DialogueNode
                    {
                        nodeId = "fight_plan",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "ВОТ ЭТО ПРАВИЛЬНЫЙ ОТВЕТ! За мной!",
                        emotion = "excited"
                    },
                    new DialogueNode
                    {
                        nodeId = "stealth_suggestion",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "Тихо? Пф! Орки не прячутся!",
                        emotion = "annoyed"
                    },
                    new DialogueNode
                    {
                        nodeId = "patience_success",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "...Хм. Может ты и прав. Ладно, попробуем твой способ. " +
                               "Но если не получится - я их всех перебью.",
                        emotion = "thoughtful",
                        onEnterActions = new()
                        {
                            new DialogueAction
                            {
                                actionType = DialogueActionType.ChangeAffinity,
                                parameter1 = "companion_orc_warrior",
                                intValue = 10
                            }
                        }
                    },
                    new DialogueNode
                    {
                        nodeId = "patience_fail",
                        speakerType = DialogueSpeakerType.NPC,
                        text = "ЧТО?! Ты думаешь я какая-то трусиха?! ЗАБЫЛ ЧТО Я ГРОГНА?!",
                        emotion = "furious",
                        onEnterActions = new()
                        {
                            new DialogueAction
                            {
                                actionType = DialogueActionType.ChangeAffinity,
                                parameter1 = "companion_orc_warrior",
                                intValue = -5
                            }
                        }
                    }
                }
            };
            DialogueManager.Instance.RegisterDialogue(grognaDialogue);

            Debug.Log("[GameBootstrap] Example data loaded!");
        }
    }
}
