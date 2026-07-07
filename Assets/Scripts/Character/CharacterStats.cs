using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Все характеристики и навыки персонажа по системе RPG старой школы (Веридия).
    /// Используется и для игрока, и для компаньонов.
    /// Система бросков: 2d6 + Характеристика против СЛ (Сложности Проверки). При преимуществе: 3d6 (выбор 2 лучших).
    /// </summary>
    [Serializable]
    public class CharacterStats
    {
        [Header("Core Characteristics (-1 to +3)")]
        public int bodyPower = 0;          // Мощность тела
        public int attentiveness = 0;      // Внимательность (восприятие + проницательность)
        public int nature = 0;             // Природа
        public int trickery = 0;           // Плутовство (скрытность + обман)
        public int sleightOfHand = 0;      // Ловкость рук (воровство + взаимодействие с механизмами)
        public int bodyKnowledge = 0;      // Знания тела
        public int academicKnowledge = 0;  // Академические знания (религия, история и т.д.)
        public int magic = 0;              // Магия

        [Header("Derived Stats")]
        public int maxHP = 20;
        public int currentHP = 20;
        public int maxMP = 10;
        public int currentMP = 10;
        public int armorClass = 10;
        public int initiative = 0;

        [Header("Skills & Proficiencies")]
        public List<SkillEntry> skills = new();

        [Header("Progression")]
        public int level = 1;
        public int experience = 0;
        public int experienceToNextLevel = 100;

        // Legacy D&D aliases for backward compatibility with combat and utility scripts
        public int strength { get => bodyPower; set => bodyPower = value; }
        public int dexterity { get => sleightOfHand; set => sleightOfHand = value; }
        public int constitution { get => bodyKnowledge; set => bodyKnowledge = value; }
        public int intelligence { get => academicKnowledge; set => academicKnowledge = value; }
        public int wisdom { get => attentiveness; set => attentiveness = value; }
        public int charisma { get => trickery; set => trickery = value; }

        public int GetStrengthMod => bodyPower;
        public int GetDexterityMod => sleightOfHand;
        public int GetConstitutionMod => bodyKnowledge;
        public int GetIntelligenceMod => academicKnowledge;
        public int GetWisdomMod => attentiveness;
        public int GetCharismaMod => trickery;

        public static string GetRussianName(SkillType skill) => SkillEntry.GetRussianName(skill);

        public int GetAttributeModifier(AttributeType attribute)
        {
            return attribute switch
            {
                AttributeType.BodyPower => bodyPower,
                AttributeType.Attentiveness => attentiveness,
                AttributeType.Nature => nature,
                AttributeType.Trickery => trickery,
                AttributeType.SleightOfHand => sleightOfHand,
                AttributeType.BodyKnowledge => bodyKnowledge,
                AttributeType.AcademicKnowledge => academicKnowledge,
                AttributeType.Magic => magic,
                _ => 0
            };
        }

        public int GetAttributeValue(AttributeType attribute) => GetAttributeModifier(attribute);

        public void SetAttributeValue(AttributeType attribute, int value)
        {
            switch (attribute)
            {
                case AttributeType.BodyPower: bodyPower = value; break;
                case AttributeType.Attentiveness: attentiveness = value; break;
                case AttributeType.Nature: nature = value; break;
                case AttributeType.Trickery: trickery = value; break;
                case AttributeType.SleightOfHand: sleightOfHand = value; break;
                case AttributeType.BodyKnowledge: bodyKnowledge = value; break;
                case AttributeType.AcademicKnowledge: academicKnowledge = value; break;
                case AttributeType.Magic: magic = value; break;
            }
        }

        public int GetSkillBonus(SkillType skillType)
        {
            int baseMod = GetAttributeModifier((AttributeType)(int)skillType);
            var entry = skills.Find(s => s.skillType == skillType);
            int profBonus = (entry != null && entry.isProficient) ? GetProficiencyBonus() : 0;
            int miscBonus = entry != null ? entry.miscBonus : 0;

            return baseMod + profBonus + miscBonus;
        }

        public int GetProficiencyBonus()
        {
            // Бонус мастерства/уровня: +1 на 1 ур., +2 на 3 ур., +3 на 5 ур.
            return 1 + (level - 1) / 2;
        }

        public bool CheckSkill(SkillType skillType, int difficultyClass)
        {
            return CheckSkillDetailed(skillType, difficultyClass).success;
        }

        /// <summary>
        /// Проверка навыка/характеристики по правилам: 2d6 + характеристика против СЛ.
        /// При преимуществе кидается 3d6, выбираются 2 лучших.
        /// СЛ 5 = Лёгкая, СЛ 7 = Средняя, СЛ 9 = Сложная, СЛ 11+ = Очень сложная.
        /// </summary>
        public (bool success, int roll, int total) CheckSkillDetailed(SkillType skillType, int difficultyClass, bool hasAdvantage = false)
        {
            int bonus = GetSkillBonus(skillType);
            int roll;

            if (hasAdvantage)
            {
                // Кидаем 3d6, выбираем 2 лучших кубика (Преимущество)
                int d1 = UnityEngine.Random.Range(1, 7);
                int d2 = UnityEngine.Random.Range(1, 7);
                int d3 = UnityEngine.Random.Range(1, 7);
                int sum = d1 + d2 + d3;
                int minDie = Mathf.Min(d1, Mathf.Min(d2, d3));
                roll = sum - minDie;
            }
            else
            {
                // Стандартный бросок 2d6 (от 2 до 12)
                int d1 = UnityEngine.Random.Range(1, 7);
                int d2 = UnityEngine.Random.Range(1, 7);
                roll = d1 + d2;
            }

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
            maxHP = 20 + (bodyPower * 5) + (bodyKnowledge * 3) + (level * 4);
            maxMP = 10 + (magic * 5) + (academicKnowledge * 3) + (level * 3);
            armorClass = 10 + sleightOfHand + (bodyPower > 0 ? 1 : 0);
            initiative = attentiveness + sleightOfHand;
            if (currentHP == 0) currentHP = maxHP; // Начальная инициализация
            currentHP = Mathf.Clamp(currentHP, 1, maxHP);
            currentMP = Mathf.Clamp(currentMP, 0, maxMP);
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
            return (AttributeType)(int)skillType;
        }

        public static string GetRussianName(SkillType skill)
        {
            return skill switch
            {
                SkillType.BodyPower => "Мощность тела",
                SkillType.Attentiveness => "Внимательность",
                SkillType.Nature => "Природа",
                SkillType.Trickery => "Плутовство",
                SkillType.SleightOfHand => "Ловкость рук",
                SkillType.BodyKnowledge => "Знания тела",
                SkillType.AcademicKnowledge => "Академические знания",
                SkillType.Magic => "Магия",
                _ => skill.ToString()
            };
        }
    }

    /// <summary>
    /// 8 основных характеристик игрока в мире Веридия
    /// </summary>
    public enum AttributeType
    {
        BodyPower = 0,          // Мощность тела
        Attentiveness = 1,      // Внимательность (восприятие + проницательность)
        Nature = 2,             // Природа
        Trickery = 3,           // Плутовство (скрытность + обман)
        SleightOfHand = 4,      // Ловкость рук (воровство + взаимодействие с механизмами)
        BodyKnowledge = 5,      // Знания тела
        AcademicKnowledge = 6,  // Академические знания (религия, история и т.д.)
        Magic = 7               // Магия
    }

    /// <summary>
    /// Навыки в нашей системе 1:1 соответствуют 8 основным характеристикам
    /// </summary>
    public enum SkillType
    {
        BodyPower = 0,          // Мощность тела
        Attentiveness = 1,      // Внимательность
        Nature = 2,             // Природа
        Trickery = 3,           // Плутовство
        SleightOfHand = 4,      // Ловкость рук
        BodyKnowledge = 5,      // Знания тела
        AcademicKnowledge = 6,  // Академические знания
        Magic = 7               // Магия
    }
}
