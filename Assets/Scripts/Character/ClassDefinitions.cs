using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Domains;

namespace RPG.Character
{
    /// <summary>
    /// 5 классов по ГДД: Воин, Плут, Волшебник, Друид, Священник.
    /// Каждый имеет:
    ///  — стартовые Домены (из них выбирается первый домен персонажа).
    ///  — классовую особенность (Подлая атака, Провоцированная атака и т.д.).
    ///  — набор подклассов.
    ///  — стартовое снаряжение.
    /// </summary>
    [Serializable]
    public class ClassDefinition
    {
        public ClassType classType;
        public string displayName;
        [TextArea(2, 5)] public string description;

        /// <summary>Домены, из которых игрок обязан выбрать первый (привязанный к классу) домен.</summary>
        public List<DomainType> starterDomains = new();

        /// <summary>Начальное Уклонение (по ГДД обычно 9–11).</summary>
        public int baseEvasion = 10;

        /// <summary>Классовая особенность — работает автоматически или через флаги в CombatManager.</summary>
        public ClassFeature classFeature;

        /// <summary>Подклассы.</summary>
        public List<SubclassDefinition> subclasses = new();

        /// <summary>Стартовое снаряжение (id из ItemDatabase).</summary>
        public List<string> startingEquipment = new();

        [TextArea(2, 5)] public string personalityArchetype;
    }

    [Serializable]
    public class ClassFeature
    {
        public string featureId;
        public string displayName;
        [TextArea(2, 6)] public string description;
    }

    [Serializable]
    public class SubclassDefinition
    {
        public string subclassId;
        public string displayName;
        [TextArea(2, 5)] public string description;

        /// <summary>Особенности подкласса (несколько штук, из ГДД).</summary>
        public List<ClassFeature> features = new();
    }

    public static class ClassDatabase
    {
        private static Dictionary<ClassType, ClassDefinition> classes;

        public static void Initialize()
        {
            classes = new Dictionary<ClassType, ClassDefinition>();

            // -------------------- ВОИН --------------------
            classes[ClassType.Warrior] = new ClassDefinition
            {
                classType = ClassType.Warrior,
                displayName = "Воин",
                description = "Мастер оружия и обороны. Стойкий защитник передовой.",
                baseEvasion = 10,
                starterDomains = new() { DomainType.Weapon, DomainType.Defense },
                classFeature = new ClassFeature
                {
                    featureId = "warrior_provoked_attack",
                    displayName = "Провоцированная атака / Боевая подготовка",
                    description =
                        "Если противник вплотную пытается покинуть эту дистанцию, совершите Бросок Реакции (характеристика на ваш выбор) против его Сложности. При успехе выберите один эффект (два — при критическом успехе): цель не может сдвинуться / вы наносите ей урон основного оружия / вы перемещаетесь вместе с целью.\n" +
                        "Боевая подготовка: игнорируете хват при экипировке оружия."
                },
                startingEquipment = new() { "weapon_long_sword", "weapon_round_shield", "armor_chain" },
                personalityArchetype = "Прямой боец, привыкший, что за его спиной кто-то есть.",
                subclasses = new()
                {
                    new SubclassDefinition
                    {
                        subclassId = "warrior_berserker",
                        displayName = "Берсерк",
                        description = "Разжигает в себе ярость и наращивает урон каждым ударом.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "berserker_unstoppable",
                                displayName = "Неудержимость",
                                description = "Потратьте Выносливость или совершите успешную атаку — получите 1 заряд ярости. Ваши броски урона получают +заряд, и добавляется ещё 1 заряд. При заряде 4 + половина уровня — состояние заканчивается."
                            },
                            new ClassFeature {
                                featureId = "berserker_hope_swap",
                                displayName = "Кровь вместо надежды",
                                description = "При успешной атаке с Надеждой можно вместо получения Надежды нанести дополнительно 1d6 урона или заставить противника потерять Выносливость."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "warrior_bulwark",
                        displayName = "Заступник",
                        description = "Опора отряда: щит и стена одновременно.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "bulwark_immovable",
                                displayName = "Непоколебимый",
                                description = "Постоянный бонус +1 к Порогам Урона."
                            },
                            new ClassFeature {
                                featureId = "bulwark_front_shield",
                                displayName = "Щит Передовой",
                                description = "При успешной атаке с Надеждой можно вместо получения Надежды восстановить 1 ячейку брони."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "warrior_martial_artist",
                        displayName = "Мастер боевых искусств",
                        description = "Выбирает 2 боевые стойки, меняет их за Выносливость.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "ma_stances",
                                displayName = "Боевые стойки",
                                description =
                                    "При создании персонажа выберите 2 стойки. В начале боя выбирается активная стойка; смена в бою стоит 1 Выносливость.\n" +
                                    "Брутальная: макс. на кости урона — доп. кость.\n" +
                                    "Защитная: атакующий вас теряет Выносливость.\n" +
                                    "Захватная: успех — можно потратить Выносливость, чтобы временно Обездвижить цель.\n" +
                                    "Устойчивая: −1 к Уклонению, зато при уроне бросьте доп. кость и отбросьте наименьший результат.\n" +
                                    "Точная: +1 к Броскам Атаки.\n" +
                                    "Быстрая: при Броске Атаки потратьте Выносливость — включите доп. цель."
                            }
                        }
                    }
                }
            };

            // -------------------- ПЛУТ --------------------
            classes[ClassType.Rogue] = new ClassDefinition
            {
                classType = ClassType.Rogue,
                displayName = "Плут",
                description = "Скрытный убийца или отравитель. Работает по слабостям и позициям.",
                baseEvasion = 11,
                starterDomains = new() { DomainType.Body, DomainType.Weapon, DomainType.Charm },
                classFeature = new ClassFeature
                {
                    featureId = "rogue_sneak_attack",
                    displayName = "Подлая атака",
                    description = "Когда вы успешно атакуете цель с преимуществом или если союзник Вплотную к цели — добавьте количество d6, равное половине уровня."
                },
                startingEquipment = new() { "weapon_dagger", "weapon_small_dagger_off", "armor_leather" },
                personalityArchetype = "Прагматик и хитрец, выживший под пятой Инквизиции.",
                subclasses = new()
                {
                    new SubclassDefinition
                    {
                        subclassId = "rogue_executioners_guild",
                        displayName = "Гильдия Палачей",
                        description = "Учат бить один раз, но так, чтобы противник не встал.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "executioner_first_strike",
                                displayName = "Первый удар",
                                description = "Первая успешная атака в бою наносит удвоенный урон."
                            },
                            new ClassFeature {
                                featureId = "executioner_sudden_attack",
                                displayName = "Внезапная атака",
                                description = "Ваша Подлая атака наносит ещё 1d6 урона."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "rogue_poisoners_guild",
                        displayName = "Гильдия Отравителей",
                        description = "Мазь на клинке — половина работы.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "poisoner_toxic_mixtures",
                                displayName = "Токсичные смеси",
                                description =
                                    "Потратьте Выносливость — получите 1d4+1 жетонов смесей. При успешной атаке примените один эффект:\n" +
                                    "Корень Горгоны — цель получает постоянный −1 к Сложности (один раз на цель).\n" +
                                    "Могильная спора — цель также должна потратить Выносливость.\n" +
                                    "Плющ пиявки — +1d6 к урону этой атаки."
                            }
                        }
                    }
                }
            };

            // -------------------- ВОЛШЕБНИК --------------------
            classes[ClassType.Mage] = new ClassDefinition
            {
                classType = ClassType.Mage,
                displayName = "Волшебник",
                description = "Заклинатель, изучающий магию как ремесло. Уязвим в ближнем, силён на дистанции.",
                baseEvasion = 9,
                starterDomains = new() { DomainType.Magic },
                classFeature = new ClassFeature
                {
                    featureId = "mage_strange_patterns",
                    displayName = "Странные закономерности",
                    description = "Каждый раз, когда на Костях Дуальности выпадает чистое «7», вы получаете Надежду или восстанавливаете Выносливость."
                },
                startingEquipment = new() { "weapon_two_handed_staff", "armor_padded" },
                personalityArchetype = "Любознательный и осторожный. Знания — единственная валюта, которой он доверяет.",
                subclasses = new()
                {
                    new SubclassDefinition
                    {
                        subclassId = "mage_school_of_knowledge",
                        displayName = "Школа знаний",
                        description = "Гибкость и мастерство навыков.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "sok_prepared",
                                displayName = "Подготовленный",
                                description = "Возьмите дополнительную Карту Домена вашего уровня или ниже из домена, к которому у вас есть доступ."
                            },
                            new ClassFeature {
                                featureId = "sok_adept",
                                displayName = "Адепт",
                                description = "Повысьте один из ваших навыков на +1."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "mage_school_of_war",
                        displayName = "Школа войны",
                        description = "Боевой маг переднего края.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "sow_battle_mage",
                                displayName = "Боевой маг",
                                description = "Дополнительная Ячейка Ран."
                            },
                            new ClassFeature {
                                featureId = "sow_defeat_fear",
                                displayName = "Победи свой страх",
                                description = "При успешной атаке со Страхом вы наносите дополнительно 1d10 магического урона."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "mage_school_of_metamagic",
                        displayName = "Школа метамагии",
                        description = "Модифицирует заклинания за счёт карт домена.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "som_metamagic",
                                displayName = "Метамагия",
                                description =
                                    "Перед заклинанием/магической атакой оружия «заблокируйте» одну карту домена до конца боя, чтобы выбрать эффект:\n" +
                                    "— удвоить дальность\n— +2 к попаданию или проверке\n— удвоить одну кость урона\n— поразить доп. цель в дистанции."
                            }
                        }
                    }
                }
            };

            // -------------------- СВЯЩЕННИК --------------------
            classes[ClassType.Cleric] = new ClassDefinition
            {
                classType = ClassType.Cleric,
                displayName = "Священник",
                description = "Бывший (или всё ещё?) служитель. В Веридии — тонкая грань между надеждой и ересью.",
                baseEvasion = 10,
                starterDomains = new() { DomainType.Light, DomainType.Terror },
                classFeature = new ClassFeature
                {
                    featureId = "cleric_believe_in_best",
                    displayName = "Вера в лучшее",
                    description = "Когда вы или ваш союзник проваливает бросок, потратьте Выносливость или Надежду — небольшое божественное вмешательство добавит вдохновение к броску (возможно, превращая провал в успех)."
                },
                startingEquipment = new() { "weapon_mace", "weapon_round_shield", "armor_chain" },
                personalityArchetype = "Сострадателен к раненым, непреклонен к грешникам — сам решает, кто есть кто.",
                subclasses = new()
                {
                    new SubclassDefinition
                    {
                        subclassId = "cleric_seraph",
                        displayName = "Серафим",
                        description = "Крылатый страж, летающий над полем боя.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "seraph_winged_guardian",
                                displayName = "Крылатый страж",
                                description = "Потратьте Выносливость, чтобы перейти в форму крылатого стража. В форме — парите, игнорируете поверхности. При успешной атаке — можно потратить 1 Надежду, чтобы нанести +1d8 урона."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "cleric_punisher",
                        displayName = "Каратель",
                        description = "Живёт местью и не прощает попадания.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "punisher_vengeance",
                                displayName = "Месть",
                                description = "Когда противник вплотную попал по вам, потратьте Выносливость, чтобы мгновенно провести по нему атаку с преимуществом. Этот бросок всегда считается с Надеждой."
                            },
                            new ClassFeature {
                                featureId = "punisher_retribution",
                                displayName = "Возмездие",
                                description = "Раз в бой при получении урона от атаки восстановите Выносливость."
                            }
                        }
                    },
                    new SubclassDefinition
                    {
                        subclassId = "cleric_priest",
                        displayName = "Жрец",
                        description = "Классический молитвенник — держит отряд молебнами.",
                        features = new()
                        {
                            new ClassFeature {
                                featureId = "priest_prayer",
                                displayName = "Молебен",
                                description =
                                    "Один раз за бой произнесите один из молебнов (свободное действие):\n" +
                                    "— Целебный: вы и все союзники в 4 кл. восстанавливают 1 ячейку здоровья.\n" +
                                    "— Боевой: цель в 4 кл. становится временно Уязвимой.\n" +
                                    "— Воодушевляющий: вы получаете Надежду за каждого спутника в 4 кл., включая себя."
                            }
                        }
                    }
                }
            };

            // -------------------- ДРУИД --------------------
            classes[ClassType.Druid] = new ClassDefinition
            {
                classType = ClassType.Druid,
                displayName = "Друид",
                description = "Оборотень и хранитель природы. В Веридии природа не «чиста» — она страдает от Морали.",
                baseEvasion = 10,
                starterDomains = new() { DomainType.Nature },
                classFeature = new ClassFeature
                {
                    featureId = "druid_beast_form",
                    displayName = "Звериная форма",
                    description =
                        "Потратьте Выносливость, чтобы превратиться в существо из списка (Огромный арахнид / Шелкопряд / Волк / Собака / Газель / Броненосец). " +
                        "В форме нельзя использовать оружие или заклинания из карт домена (кроме тех, что уже действуют). Броня становится частью тела. Форма даёт свои особенности, бонус к Уклонению и меняет характеристику атаки."
                },
                startingEquipment = new() { "weapon_short_staff", "armor_leather" },
                personalityArchetype = "Медлителен на словах, стремителен в действии. Слушает лес, а не проповедников.",
                subclasses = new()
                {
                    new SubclassDefinition
                    {
                        subclassId = "druid_placeholder",
                        displayName = "Хранитель Рощи (заглушка)",
                        description = "Подклассы Друида будут доработаны позже. Сейчас это техническая заглушка, ничего не даёт.",
                        features = new()
                    }
                }
            };
        }

        public static ClassDefinition GetClass(ClassType type)
        {
            if (classes == null) Initialize();
            return classes.TryGetValue(type, out var def) ? def : null;
        }

        public static IReadOnlyList<ClassType> PlayableClasses => new[]
        {
            ClassType.Warrior, ClassType.Rogue, ClassType.Mage, ClassType.Cleric, ClassType.Druid
        };
    }

    public enum ClassType
    {
        Warrior = 0,
        Rogue = 1,
        Mage = 2,
        Cleric = 3,
        Druid = 4
    }
}
