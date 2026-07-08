using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Определения рас по ГДД (ERP Game.txt).
    /// В ранней версии игры доступны 3 расы: Человек, Эльф, Тифлинг.
    /// Расовых бонусов к атрибутам нет — только особенности, работающие в бою через Надежду/Выносливость.
    /// Орк (полуорк) существует как раса компаньонов, не игрока.
    /// </summary>
    [Serializable]
    public class RaceDefinition
    {
        public RaceType raceType;
        public string displayName;
        [TextArea(2, 5)] public string description;

        /// <summary>Расовые особенности в виде id (обрабатываются подписчиками в бою).</summary>
        public List<RaceFeature> features = new();

        /// <summary>Строка-архетип для LLM-контекста.</summary>
        [TextArea(2, 5)] public string personalityArchetype;
    }

    [Serializable]
    public class RaceFeature
    {
        public string featureId;      // например "tiefling_fearless"
        public string displayName;
        [TextArea(2, 4)] public string description;
        public RaceFeatureTrigger trigger; // когда предлагаем игроку использовать
        public RaceFeatureCost cost;       // что тратим
    }

    public enum RaceFeatureTrigger
    {
        Manual,                 // игрок сам активирует
        OnFearRoll,             // при броске со Страхом (Тифлинг: Бесстрашный)
        OnAttackAgainstAdjacent,// когда рядом с нами кого-то атакуют (Тифлинг: Пугающий)
        BeforeNextRoll,         // перед своим следующим броском (Эльф: Многовековая практика)
        OnFailedRoll,           // при провале броска (Человек: Адаптивность)
        Passive                 // сработает автоматически без окна (Орк: Стойкий; Человек: макс. Выносливости)
    }

    public enum RaceFeatureCost
    {
        None,
        Stamina,
        Hope
    }

    public static class RaceDatabase
    {
        private static Dictionary<RaceType, RaceDefinition> races;

        public static void Initialize()
        {
            races = new Dictionary<RaceType, RaceDefinition>
            {
                {
                    RaceType.Human, new RaceDefinition
                    {
                        raceType = RaceType.Human,
                        displayName = "Человек",
                        description = "Обычный житель Веридии. Гибкий, упорный, привыкший подстраиваться под жизнь под пятой Инквизиции.",
                        personalityArchetype = "Обычный житель королевства, познавший на своей шкуре двуличие Церкви Света.",
                        features = new()
                        {
                            new RaceFeature
                            {
                                featureId = "human_high_stamina",
                                displayName = "Высокая выносливость",
                                description = "Имеет на 1 больше максимума очков Выносливости и начинает бой с двумя очками.",
                                trigger = RaceFeatureTrigger.Passive,
                                cost = RaceFeatureCost.None
                            },
                            new RaceFeature
                            {
                                featureId = "human_adaptability",
                                displayName = "Адаптивность",
                                description = "Провалив бросок, потратьте 1 Надежду, чтобы восстановить 1 Выносливость.",
                                trigger = RaceFeatureTrigger.OnFailedRoll,
                                cost = RaceFeatureCost.Hope
                            }
                        }
                    }
                },
                {
                    RaceType.Elf, new RaceDefinition
                    {
                        raceType = RaceType.Elf,
                        displayName = "Эльф",
                        description = "Долгоживущая раса, тонко ощущающая потоки магии. Инквизиция считает их прирождёнными еретиками.",
                        personalityArchetype = "Мудрый, дистанцированный, воспринимает суету людей как быстро проходящее наваждение.",
                        features = new()
                        {
                            new RaceFeature
                            {
                                featureId = "elf_ancient_practice",
                                displayName = "Многовековая практика",
                                description = "Потратьте Выносливость, чтобы совершить следующий бросок с преимуществом.",
                                trigger = RaceFeatureTrigger.BeforeNextRoll,
                                cost = RaceFeatureCost.Stamina
                            },
                            new RaceFeature
                            {
                                featureId = "elf_magically_gifted",
                                displayName = "Магически одарённый",
                                description = "Потратьте Надежду, чтобы сделать противника временно Уязвимым.",
                                trigger = RaceFeatureTrigger.Manual,
                                cost = RaceFeatureCost.Hope
                            }
                        }
                    }
                },
                {
                    RaceType.Tiefling, new RaceDefinition
                    {
                        raceType = RaceType.Tiefling,
                        displayName = "Тифлинг",
                        description = "Потомок демонов. В Веридии — синоним «скверны» в глазах Инквизиции: тифлингов хватают по любому подозрению.",
                        personalityArchetype = "Привык к косым взглядам и обвинениям. Внешне спокоен, внутри — постоянный подсчёт путей отступления.",
                        features = new()
                        {
                            new RaceFeature
                            {
                                featureId = "tiefling_fearless",
                                displayName = "Бесстрашный",
                                description = "Когда вы совершаете бросок со Страхом, потратьте Выносливость, чтобы заменить его на бросок с Надеждой. Вы не получаете Надежду за этот бросок.",
                                trigger = RaceFeatureTrigger.OnFearRoll,
                                cost = RaceFeatureCost.Stamina
                            },
                            new RaceFeature
                            {
                                featureId = "tiefling_intimidating",
                                displayName = "Пугающий",
                                description = "Потратьте Надежду, чтобы дать помеху на атаку, совершаемую в 2 клетках от вас.",
                                trigger = RaceFeatureTrigger.OnAttackAgainstAdjacent,
                                cost = RaceFeatureCost.Hope
                            }
                        }
                    }
                },
                // Орк — раса компаньонов (первая пара компаньонов пролога).
                {
                    RaceType.Orc, new RaceDefinition
                    {
                        raceType = RaceType.Orc,
                        displayName = "Орк",
                        description = "Крепкая раса, известная в Веридии своей несгибаемостью. Не доступна для игрока.",
                        personalityArchetype = "Прямолинейный, вспыльчивый, живёт по кодексу силы.",
                        features = new()
                        {
                            new RaceFeature
                            {
                                featureId = "orc_hardy",
                                displayName = "Стойкий",
                                description = "Когда у вас остаётся лишь 1 шкала здоровья, все атаки по вам совершаются с помехой.",
                                trigger = RaceFeatureTrigger.Passive,
                                cost = RaceFeatureCost.None
                            },
                            new RaceFeature
                            {
                                featureId = "orc_tusks",
                                displayName = "Бивни",
                                description = "При успешной атаке по цели Вплотную можно потратить Надежду, чтобы нанести дополнительно 1d6 урона бивнями.",
                                trigger = RaceFeatureTrigger.Manual,
                                cost = RaceFeatureCost.Hope
                            }
                        }
                    }
                }
            };
        }

        public static RaceDefinition GetRace(RaceType type)
        {
            if (races == null) Initialize();
            return races.TryGetValue(type, out var def) ? def : null;
        }

        /// <summary>Расы, которые может выбрать игрок при создании персонажа.</summary>
        public static IReadOnlyList<RaceType> PlayableRaces => new[]
        {
            RaceType.Human, RaceType.Elf, RaceType.Tiefling
        };
    }

    public enum RaceType
    {
        Human = 0,
        Elf = 1,
        Tiefling = 2,
        Orc = 3
    }
}
