using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Определения рас с их бонусами и особенностями
    /// </summary>
    [Serializable]
    public class RaceDefinition
    {
        public RaceType raceType;
        public string displayName;
        public string description;
        public Dictionary<AttributeType, int> attributeBonuses = new();
        public List<string> racialTraits = new();
        public List<string> racialAbilities = new();

        // Для LLM контекста
        [TextArea(2, 5)]
        public string personalityArchetype;
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
                        description = "Универсальные и адаптивные. Люди преуспевают во всём, за что берутся.",
                        attributeBonuses = new() {
                            { AttributeType.Strength, 1 }, { AttributeType.Dexterity, 1 },
                            { AttributeType.Constitution, 1 }, { AttributeType.Intelligence, 1 },
                            { AttributeType.Wisdom, 1 }, { AttributeType.Charisma, 1 }
                        },
                        racialTraits = new() { "versatile", "adaptable", "ambitious" },
                        racialAbilities = new() { "bonus_skill_proficiency" },
                        personalityArchetype = "Универсал без выраженных расовых особенностей"
                    }
                },
                {
                    RaceType.Elf, new RaceDefinition
                    {
                        raceType = RaceType.Elf,
                        displayName = "Эльф",
                        description = "Грациозные и долгоживущие. Эльфы обладают природной связью с магией.",
                        attributeBonuses = new() {
                            { AttributeType.Dexterity, 2 }, { AttributeType.Intelligence, 1 }
                        },
                        racialTraits = new() { "darkvision", "fey_ancestry", "keen_senses" },
                        racialAbilities = new() { "trance", "perception_proficiency" },
                        personalityArchetype = "Элегантный, несколько надменный, мудрый не по годам"
                    }
                },
                {
                    RaceType.Dwarf, new RaceDefinition
                    {
                        raceType = RaceType.Dwarf,
                        displayName = "Дварф",
                        description = "Крепкие и упрямые. Дварфы — непревзойдённые мастера и воины.",
                        attributeBonuses = new() {
                            { AttributeType.Constitution, 2 }, { AttributeType.Strength, 1 }
                        },
                        racialTraits = new() { "darkvision", "dwarven_resilience", "stonecunning" },
                        racialAbilities = new() { "poison_resistance", "tool_proficiency" },
                        personalityArchetype = "Прямолинейный, верный друзьям, не забывающий обид"
                    }
                },
                {
                    RaceType.HalfOrc, new RaceDefinition
                    {
                        raceType = RaceType.HalfOrc,
                        displayName = "Полуорк",
                        description = "Сильные и свирепые. Полуорки сочетают грубую силу с человеческой хитростью.",
                        attributeBonuses = new() {
                            { AttributeType.Strength, 2 }, { AttributeType.Constitution, 1 }
                        },
                        racialTraits = new() { "darkvision", "relentless_endurance", "savage_attacks" },
                        racialAbilities = new() { "intimidation_proficiency" },
                        personalityArchetype = "Грубый снаружи, но способный на глубокие чувства"
                    }
                },
                {
                    RaceType.HalfElf, new RaceDefinition
                    {
                        raceType = RaceType.HalfElf,
                        displayName = "Полуэльф",
                        description = "Обаятельные и универсальные. Полуэльфы — природные дипломаты.",
                        attributeBonuses = new() {
                            { AttributeType.Charisma, 2 }, { AttributeType.Dexterity, 1 }
                        },
                        racialTraits = new() { "darkvision", "fey_ancestry", "skill_versatility" },
                        racialAbilities = new() { "two_extra_skill_proficiencies" },
                        personalityArchetype = "Обаятельный, легко находит общий язык с кем угодно"
                    }
                },
                {
                    RaceType.Tiefling, new RaceDefinition
                    {
                        raceType = RaceType.Tiefling,
                        displayName = "Тифлинг",
                        description = "Потомки демонов. Тифлинги обладают тёмной магией и притягательной внешностью.",
                        attributeBonuses = new() {
                            { AttributeType.Charisma, 2 }, { AttributeType.Intelligence, 1 }
                        },
                        racialTraits = new() { "darkvision", "hellish_resistance", "infernal_legacy" },
                        racialAbilities = new() { "fire_resistance", "thaumaturgy_cantrip" },
                        personalityArchetype = "Загадочный, привыкший к подозрению окружающих, обаятельный"
                    }
                },
                {
                    RaceType.Halfling, new RaceDefinition
                    {
                        raceType = RaceType.Halfling,
                        displayName = "Полурослик",
                        description = "Маленькие и удачливые. Полурослики избегают неприятностей лучше всех.",
                        attributeBonuses = new() {
                            { AttributeType.Dexterity, 2 }, { AttributeType.Charisma, 1 }
                        },
                        racialTraits = new() { "lucky", "brave", "halfling_nimbleness" },
                        racialAbilities = new() { "reroll_ones", "advantage_vs_frightened" },
                        personalityArchetype = "Весёлый, любопытный, неожиданно храбрый"
                    }
                }
            };
        }

        public static RaceDefinition GetRace(RaceType type)
        {
            if (races == null) Initialize();
            return races.TryGetValue(type, out var def) ? def : null;
        }
    }

    public enum RaceType
    {
        Human,
        Elf,
        Dwarf,
        HalfOrc,
        HalfElf,
        Tiefling,
        Halfling
    }
}
