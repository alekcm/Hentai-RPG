using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Все характеристики и навыки персонажа. Используется и для игрока, и для компаньонов.
    /// </summary>
    [Serializable]
    public class CharacterStats
    {
        [Header("Core Attributes (1-20)")]
        public int strength = 10;       // Сила - ближний бой, грузоподъёмность
        public int dexterity = 10;      // Ловкость - дальний бой, скрытность, инициатива
        public int constitution = 10;   // Телосложение - HP, выносливость
        public int intelligence = 10;   // Интеллект - магия знаний, расследование
        public int wisdom = 10;         // Мудрость - восприятие, выживание, магия природы
        public int charisma = 10;       // Харизма - убеждение, обман, запугивание

        [Header("Derived Stats")]
        public int maxHP = 20;
        public int currentHP = 20;
        public int maxMP = 10;
        public int currentMP = 10;
        public int armorClass = 10;
        public int initiative = 0;

        [Header("Skills (proficiency bonus applied)")]
        public List<SkillEntry> skills = new();

        [Header("Progression")]
        public int level = 1;
        public int experience = 0;
        public int experienceToNextLevel = 100;

        // Модификатор характеристики
        public int GetModifier(int statValue)
        {
            return Mathf.FloorToInt((statValue - 10) / 2f);
        }

        public int GetStrengthMod => GetModifier(strength);
        public int GetDexterityMod => GetModifier(dexterity);
        public int GetConstitutionMod => GetModifier(constitution);
        public int GetIntelligenceMod => GetModifier(intelligence);
        public int GetWisdomMod => GetModifier(wisdom);
        public int GetCharismaMod => GetModifier(charisma);

        public int GetAttributeModifier(AttributeType attribute)
        {
            return attribute switch
            {
                AttributeType.Strength => GetStrengthMod,
                AttributeType.Dexterity => GetDexterityMod,
                AttributeType.Constitution => GetConstitutionMod,
                AttributeType.Intelligence => GetIntelligenceMod,
                AttributeType.Wisdom => GetWisdomMod,
                AttributeType.Charisma => GetCharismaMod,
                _ => 0
            };
        }

        public int GetAttributeValue(AttributeType attribute)
        {
            return attribute switch
            {
                AttributeType.Strength => strength,
                AttributeType.Dexterity => dexterity,
                AttributeType.Constitution => constitution,
                AttributeType.Intelligence => intelligence,
                AttributeType.Wisdom => wisdom,
                AttributeType.Charisma => charisma,
                _ => 10
            };
        }

        public int GetSkillBonus(SkillType skillType)
        {
            var entry = skills.Find(s => s.skillType == skillType);
            if (entry == null) return 0;

            int baseMod = GetAttributeModifier(entry.GetGoverningAttribute());
            int profBonus = entry.isProficient ? GetProficiencyBonus() : 0;
            int expertiseBonus = entry.hasExpertise ? GetProficiencyBonus() : 0;

            return baseMod + profBonus + expertiseBonus + entry.miscBonus;
        }

        public int GetProficiencyBonus()
        {
            // Прогрессия бонуса мастерства: 2, 2, 2, 2, 3, 3, 3, 3, 4...
            return 2 + (level - 1) / 4;
        }

        public bool CheckSkill(SkillType skillType, int difficultyClass)
        {
            int bonus = GetSkillBonus(skillType);
            int roll = UnityEngine.Random.Range(1, 21); // 1-20
            int total = roll + bonus;
            return total >= difficultyClass;
        }

        public (bool success, int roll, int total) CheckSkillDetailed(SkillType skillType, int difficultyClass)
        {
            int bonus = GetSkillBonus(skillType);
            int roll = UnityEngine.Random.Range(1, 21);
            int total = roll + bonus;
            return (total >= difficultyClass, roll, total);
        }

        public void TakeDamage(int amount)
        {
            currentHP = Mathf.Max(0, currentHP - amount);
        }

        public void Heal(int amount)
        {
            currentHP = Mathf.Min(maxHP, currentHP + amount);
        }

        public void SpendMP(int amount)
        {
            currentMP = Mathf.Max(0, currentMP - amount);
        }

        public void RestoreMP(int amount)
        {
            currentMP = Mathf.Min(maxMP, currentMP + amount);
        }

        public bool IsAlive => currentHP > 0;

        public void RecalculateDerivedStats()
        {
            maxHP = 20 + (GetConstitutionMod * level);
            maxMP = 10 + (GetIntelligenceMod * level);
            armorClass = 10 + GetDexterityMod;
            initiative = GetDexterityMod;
        }

        public bool TryLevelUp()
        {
            if (experience >= experienceToNextLevel)
            {
                experience -= experienceToNextLevel;
                level++;
                experienceToNextLevel = level * 100;
                RecalculateDerivedStats();
                currentHP = maxHP;
                currentMP = maxMP;
                return true;
            }
            return false;
        }
    }

    [Serializable]
    public class SkillEntry
    {
        public SkillType skillType;
        public bool isProficient;
        public bool hasExpertise;
        public int miscBonus;

        public AttributeType GetGoverningAttribute()
        {
            return skillType switch
            {
                SkillType.Athletics => AttributeType.Strength,
                SkillType.Acrobatics => AttributeType.Dexterity,
                SkillType.SleightOfHand => AttributeType.Dexterity,
                SkillType.Stealth => AttributeType.Dexterity,
                SkillType.Arcana => AttributeType.Intelligence,
                SkillType.History => AttributeType.Intelligence,
                SkillType.Investigation => AttributeType.Intelligence,
                SkillType.Nature => AttributeType.Intelligence,
                SkillType.Religion => AttributeType.Intelligence,
                SkillType.AnimalHandling => AttributeType.Wisdom,
                SkillType.Insight => AttributeType.Wisdom,
                SkillType.Medicine => AttributeType.Wisdom,
                SkillType.Perception => AttributeType.Wisdom,
                SkillType.Survival => AttributeType.Wisdom,
                SkillType.Deception => AttributeType.Charisma,
                SkillType.Intimidation => AttributeType.Charisma,
                SkillType.Performance => AttributeType.Charisma,
                SkillType.Persuasion => AttributeType.Charisma,
                _ => AttributeType.Strength
            };
        }
    }

    public enum AttributeType
    {
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma
    }

    public enum SkillType
    {
        // Strength
        Athletics,
        // Dexterity
        Acrobatics,
        SleightOfHand,
        Stealth,
        // Intelligence
        Arcana,
        History,
        Investigation,
        Nature,
        Religion,
        // Wisdom
        AnimalHandling,
        Insight,
        Medicine,
        Perception,
        Survival,
        // Charisma
        Deception,
        Intimidation,
        Performance,
        Persuasion
    }
}
