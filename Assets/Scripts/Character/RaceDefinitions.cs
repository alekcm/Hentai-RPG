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
                            { AttributeType.BodyPower, 1 }, { AttributeType.AcademicKnowledge, 1 }
                        },
                        racialTraits = new() { "versatile", "adaptable", "ambitious" },
                        racialAbilities = new() { "bonus_skill_proficiency" },
                        personalityArchetype = "Амбициозный, легко адаптируется к любым условиям Веридии"
                    }
                },
                {
                    RaceType.Elf, new RaceDefinition
                    {
                        raceType = RaceType.Elf,
                        displayName = "Эльф",
                        description = "Грациозные и долгоживущие. Эльфы обладают природной связью с магией.",
                        attributeBonuses = new() {
                            { AttributeType.SleightOfHand, 1 }, { AttributeType.Magic, 1 }
                        },
                        racialTraits = new() { "darkvision", "fey_ancestry", "keen_senses" },
                        racialAbilities = new() { "trance", "perception_proficiency" },
                        personalityArchetype = "Элегантный, мудрый не по годам, тонко чувствует потоки магии"
                    }
                },
                {
                    RaceType.Dwarf, new RaceDefinition
                    {
                        raceType = RaceType.Dwarf,
                        displayName = "Дварф",
                        description = "Крепкие и упрямые. Дварфы — непревзойдённые мастера и воины.",
                        attributeBonuses = new() {
                            { AttributeType.BodyKnowledge, 1 }, { AttributeType.BodyPower, 1 }
                        },
                        racialTraits = new() { "darkvision", "dwarven_resilience", "stonecunning" },
                        racialAbilities = new() { "poison_resistance", "tool_proficiency" },
                        personalityArchetype = "Прямолинейный, верный друзьям, невероятно стойкий"
                    }
                },
                {
                    RaceType.HalfOrc, new RaceDefinition
                    {
                        raceType = RaceType.HalfOrc,
                        displayName = "Орк / Полуорк",
                        description = "Сильные и свирепые. Воплощение первобытной мощи и стойкости.",
                        attributeBonuses = new() {
                            { AttributeType.BodyPower, 1 }, { AttributeType.BodyKnowledge, 1 }
                        },
                        racialTraits = new() { "darkvision", "relentless_endurance", "savage_attacks" },
                        racialAbilities = new() { "intimidation_proficiency" },
                        personalityArchetype = "Гордый воин, уважающий только силу, скрывающий боль за яростью"
                    }
                },
                {
                    RaceType.HalfElf, new RaceDefinition
                    {
                        raceType = RaceType.HalfElf,
                        displayName = "Полуэльф",
                        description = "Обаятельные и универсальные. Природные дипломаты и хитрецы.",
                        attributeBonuses = new() {
                            { AttributeType.Trickery, 1 }, { AttributeType.Attentiveness, 1 }
                        },
                        racialTraits = new() { "darkvision", "fey_ancestry", "skill_versatility" },
                        racialAbilities = new() { "two_extra_skill_proficiencies" },
                        personalityArchetype = "Обаятельный, легко находит общий язык в любой ситуации"
                    }
                },
                {
                    RaceType.Tiefling, new RaceDefinition
                    {
                        raceType = RaceType.Tiefling,
                        displayName = "Тифлинг",
                        description = "Потомки бездны. Обладают врожденной магией и притягательным плутовством.",
                        attributeBonuses = new() {
                            { AttributeType.Trickery, 1 }, { AttributeType.Magic, 1 }
                        },
                        racialTraits = new() { "darkvision", "hellish_resistance", "infernal_legacy" },
                        racialAbilities = new() { "fire_resistance", "thaumaturgy_cantrip" },
                        personalityArchetype = "Загадочный, обаятельный, привыкший к подозрению инквизиции"
                    }
                },
                {
                    RaceType.Halfling, new RaceDefinition
                    {
                        raceType = RaceType.Halfling,
                        displayName = "Полурослик",
                        description = "Маленькие и удачливые. Ловкость рук помогает им избегать неприятностей.",
                        attributeBonuses = new() {
                            { AttributeType.SleightOfHand, 1 }, { AttributeType.Trickery, 1 }
                        },
                        racialTraits = new() { "lucky", "brave", "halfling_nimbleness" },
                        racialAbilities = new() { "reroll_ones", "advantage_vs_frightened" },
                        personalityArchetype = "Весёлый, любопытный, мастерски взаимодействует с механизмами"
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
