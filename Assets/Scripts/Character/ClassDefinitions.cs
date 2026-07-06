using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Определения классов персонажей
    /// </summary>
    [Serializable]
    public class ClassDefinition
    {
        public ClassType classType;
        public string displayName;
        public string description;
        public AttributeType primaryAttribute;
        public AttributeType secondaryAttribute;
        public int hitDie; // d8, d10, d12
        public List<string> startingProficiencies = new();
        public List<string> startingAbilities = new();
        public List<SkillType> classSkillOptions = new();
        public int skillChoicesCount = 2;

        [TextArea(2, 5)]
        public string personalityArchetype;

        // Начальные статы (базовые до расы)
        public CharacterStats GetStartingStats()
        {
            var stats = new CharacterStats();
            stats.maxHP = hitDie + stats.GetConstitutionMod;
            stats.currentHP = stats.maxHP;
            return stats;
        }
    }

    public static class ClassDatabase
    {
        private static Dictionary<ClassType, ClassDefinition> classes;

        public static void Initialize()
        {
            classes = new Dictionary<ClassType, ClassDefinition>
            {
                {
                    ClassType.Warrior, new ClassDefinition
                    {
                        classType = ClassType.Warrior,
                        displayName = "Воин",
                        description = "Мастер ближнего боя. Несокрушимая сила и стальная воля.",
                        primaryAttribute = AttributeType.Strength,
                        secondaryAttribute = AttributeType.Constitution,
                        hitDie = 12,
                        startingProficiencies = new() { "all_armor", "all_weapons", "shields" },
                        startingAbilities = new() { "second_wind", "action_surge" },
                        classSkillOptions = new() { SkillType.Athletics, SkillType.Intimidation, SkillType.Perception, SkillType.Survival },
                        skillChoicesCount = 2,
                        personalityArchetype = "Решительный защитник, предпочитает действовать, а не говорить"
                    }
                },
                {
                    ClassType.Rogue, new ClassDefinition
                    {
                        classType = ClassType.Rogue,
                        displayName = "Плут",
                        description = "Мастер теней. Скрытность, хитрость и смертоносные удары.",
                        primaryAttribute = AttributeType.Dexterity,
                        secondaryAttribute = AttributeType.Charisma,
                        hitDie = 8,
                        startingProficiencies = new() { "light_armor", "medium_armor", "simple_weapons", "thieves_tools" },
                        startingAbilities = new() { "sneak_attack", "expertise", "thieves_cant" },
                        classSkillOptions = new() { SkillType.Stealth, SkillType.SleightOfHand, SkillType.Acrobatics, SkillType.Deception, SkillType.Investigation, SkillType.Perception },
                        skillChoicesCount = 4,
                        personalityArchetype = "Скрытный и расчётливый, всегда имеет план Б"
                    }
                },
                {
                    ClassType.Mage, new ClassDefinition
                    {
                        classType = ClassType.Mage,
                        displayName = "Маг",
                        description = "Повелитель арканной магии. Знание — величайшее оружие.",
                        primaryAttribute = AttributeType.Intelligence,
                        secondaryAttribute = AttributeType.Wisdom,
                        hitDie = 6,
                        startingProficiencies = new() { "light_armor", "daggers", "staves", "arcanum" },
                        startingAbilities = new() { "spellcasting", "arcane_recovery", "ritual_casting" },
                        classSkillOptions = new() { SkillType.Arcana, SkillType.History, SkillType.Investigation, SkillType.Insight },
                        skillChoicesCount = 2,
                        personalityArchetype = "Любознательный учёный, ценит знания превыше всего"
                    }
                },
                {
                    ClassType.Cleric, new ClassDefinition
                    {
                        classType = ClassType.Cleric,
                        displayName = "Жрец",
                        description = "Служитель богов. Целитель и защитник веры.",
                        primaryAttribute = AttributeType.Wisdom,
                        secondaryAttribute = AttributeType.Charisma,
                        hitDie = 8,
                        startingProficiencies = new() { "light_armor", "medium_armor", "shields", "simple_weapons" },
                        startingAbilities = new() { "divine_spellcasting", "channel_divinity", "turn_undead" },
                        classSkillOptions = new() { SkillType.Medicine, SkillType.Religion, SkillType.Insight, SkillType.Persuasion },
                        skillChoicesCount = 2,
                        personalityArchetype = "Сострадательный, но непреклонный в своих убеждениях"
                    }
                },
                {
                    ClassType.Druid, new ClassDefinition
                    {
                        classType = ClassType.Druid,
                        displayName = "Друид",
                        description = "Хранитель природы. Оборотень и повелитель стихий.",
                        primaryAttribute = AttributeType.Wisdom,
                        secondaryAttribute = AttributeType.Constitution,
                        hitDie = 8,
                        startingProficiencies = new() { "light_armor", "medium_armor", "shields", "druidic_focus" },
                        startingAbilities = new() { "wild_shape", "druidic_spellcasting", "druidic_language" },
                        classSkillOptions = new() { SkillType.Nature, SkillType.AnimalHandling, SkillType.Survival, SkillType.Perception, SkillType.Medicine },
                        skillChoicesCount = 2,
                        personalityArchetype = "Связан с природой, терпелив, но опасен когда разгневан"
                    }
                },
                {
                    ClassType.Bard, new ClassDefinition
                    {
                        classType = ClassType.Bard,
                        displayName = "Бард",
                        description = "Мастер слова и музыки. Вдохновитель и обманщик.",
                        primaryAttribute = AttributeType.Charisma,
                        secondaryAttribute = AttributeType.Dexterity,
                        hitDie = 8,
                        startingProficiencies = new() { "light_armor", "simple_weapons", "musical_instruments" },
                        startingAbilities = new() { "bardic_inspiration", "spellcasting", "jack_of_all_trades" },
                        classSkillOptions = new() { SkillType.Performance, SkillType.Persuasion, SkillType.Deception, SkillType.Acrobatics, SkillType.Insight, SkillType.Intimidation },
                        skillChoicesCount = 3,
                        personalityArchetype = "Обаятельный болтун, всегда готов к флирту и приключениям"
                    }
                }
            };
        }

        public static ClassDefinition GetClass(ClassType type)
        {
            if (classes == null) Initialize();
            return classes.TryGetValue(type, out var def) ? def : null;
        }
    }

    public enum ClassType
    {
        Warrior,
        Rogue,
        Mage,
        Cleric,
        Druid,
        Bard
    }
}
