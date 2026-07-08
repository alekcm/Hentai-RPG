using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Характеристики персонажа по ГДД:
    ///  — 8 навыков (-1..+3), никаких «атрибутов» отдельно от навыков нет.
    ///  — Проверка: 2d6 + бонус навыка против СЛ (5 лёгкая / 7 средняя / 9 сложная / 11+ очень сложная).
    ///  — Здоровье: N шкал (обычно 6), в каждой X HP от брони.
    ///  — Броня: N ячеек (по броне), Порог Урона (DT) — если урон < DT, атака не наносит ничего; иначе ломает 1 ячейку и остаток идёт по HP.
    ///  — Уклонение (Evasion) — «класс защиты», от класса + модификатор брони + другие бонусы.
    ///  — Выносливость (Stamina) — уникальный ресурс юнита, по умолчанию max=1.
    /// </summary>
    [Serializable]
    public class CharacterStats
    {
        [Header("Skills (-1..+3)")]
        public int bodyPower = 0;          // Мощность тела
        public int attentiveness = 0;      // Внимательность (восприятие + проницательность)
        public int nature = 0;             // Природа
        public int trickery = 0;           // Плутовство (скрытность + обман)
        public int sleightOfHand = 0;      // Ловкость рук
        public int bodyKnowledge = 0;      // Знания тела (медицина)
        public int academicKnowledge = 0;  // Академические знания
        public int magic = 0;              // Магия

        [Header("Progression")]
        public int level = 1;

        [Header("Health (Hit Slots)")]
        [Tooltip("Максимальное число ячеек здоровья. По умолчанию 6.")]
        public int maxHealthSlots = 6;
        [Tooltip("Сколько ячеек здоровья уже сломано.")]
        public int usedHealthSlots = 0;
        [Tooltip("Сколько единиц HP в одной ячейке — зависит от надетой брони.")]
        public int hpPerSlot = 6;
        [Tooltip("Текущий HP внутри текущей (частично сломанной) ячейки.")]
        public int currentSlotHp = 6;

        [Header("Armor")]
        [Tooltip("Ячейки брони. Заполняются при экипировке брони.")]
        public int maxArmorSlots = 0;
        public int usedArmorSlots = 0;
        [Tooltip("Порог Урона. Если урон меньше этого значения — атака ноль.")]
        public int damageThreshold = 0;
        [Tooltip("Показатель Брони — сколько урона поглощает пробитая ячейка (вычитается перед уроном по HP при желании; в базовой ГДД-модели используем DT).")]
        public int armorRating = 0;

        [Header("Defense")]
        public int evasion = 10;

        [Header("Stamina")]
        public int maxStamina = 2;
        public int currentStamina = 2;

        [Header("Skill entries (proficiencies etc — не используется в базовой ГДД-модели, оставлено под расширения)")]
        public List<SkillEntry> skills = new();

        // ================================================================
        //   ПРОВЕРКИ 2d6 + БОНУС (для диалоговых и вне-боевых бросков)
        // ================================================================

        public int GetSkillBonus(SkillType skill) => skill switch
        {
            SkillType.BodyPower          => bodyPower,
            SkillType.Attentiveness      => attentiveness,
            SkillType.Nature             => nature,
            SkillType.Trickery           => trickery,
            SkillType.SleightOfHand      => sleightOfHand,
            SkillType.BodyKnowledge      => bodyKnowledge,
            SkillType.AcademicKnowledge  => academicKnowledge,
            SkillType.Magic              => magic,
            _ => 0
        };

        public void SetSkillBonus(SkillType skill, int value)
        {
            switch (skill)
            {
                case SkillType.BodyPower:         bodyPower = value; break;
                case SkillType.Attentiveness:     attentiveness = value; break;
                case SkillType.Nature:            nature = value; break;
                case SkillType.Trickery:          trickery = value; break;
                case SkillType.SleightOfHand:     sleightOfHand = value; break;
                case SkillType.BodyKnowledge:     bodyKnowledge = value; break;
                case SkillType.AcademicKnowledge: academicKnowledge = value; break;
                case SkillType.Magic:             magic = value; break;
            }
        }

        public bool CheckSkill(SkillType skill, int difficultyClass, bool advantage = false, bool disadvantage = false)
            => CheckSkillDetailed(skill, difficultyClass, advantage, disadvantage).success;

        /// <summary>
        /// Стандартный вне-боевой бросок 2d6 + бонус vs СЛ. Преимущество/помеха — +d6/-d6.
        /// </summary>
        public (bool success, int roll, int total, int bonus) CheckSkillDetailed(
            SkillType skill, int difficultyClass, bool advantage = false, bool disadvantage = false)
        {
            int bonus = GetSkillBonus(skill);
            int d1 = UnityEngine.Random.Range(1, 7);
            int d2 = UnityEngine.Random.Range(1, 7);
            int roll = d1 + d2;
            if (advantage && !disadvantage) roll += UnityEngine.Random.Range(1, 7);
            else if (disadvantage && !advantage) roll -= UnityEngine.Random.Range(1, 7);
            int total = roll + bonus;
            return (total >= difficultyClass, roll, total, bonus);
        }

        // ================================================================
        //   ЗДОРОВЬЕ / БРОНЯ (ГДД-модель)
        // ================================================================

        /// <summary>Полное максимальное HP = слоты × HP в слоте.</summary>
        public int TotalMaxHP => maxHealthSlots * hpPerSlot;
        /// <summary>Текущее HP суммарно.</summary>
        public int TotalCurrentHP => Mathf.Max(0, (maxHealthSlots - usedHealthSlots - 1) * hpPerSlot + Mathf.Max(0, currentSlotHp));

        public bool IsAlive => usedHealthSlots < maxHealthSlots;

        /// <summary>
        /// Наносит урон по ГДД-правилам.
        ///   1) Если брони >= 1: сравниваем rawDamage с DT.
        ///      Не пробили → 0 урона.
        ///      Пробили → тратим 1 ячейку брони, остаток HP-урона = rawDamage - hpPerSlot (одна шкала).
        ///   2) Иначе: весь rawDamage идёт по HP.
        /// Возвращает разбивку урона для лога.
        /// </summary>
        public DamageResult TakeDamage(int rawDamage, bool bypassArmor = false)
        {
            var result = new DamageResult { rawDamage = rawDamage };

            if (rawDamage <= 0) return result;

            if (!bypassArmor && usedArmorSlots < maxArmorSlots)
            {
                if (rawDamage < damageThreshold)
                {
                    // Пороги удержали удар — 0 урона.
                    result.blockedByThreshold = true;
                    return result;
                }

                // Ломается 1 ячейка брони, остаток = rawDamage - "одна шкала здоровья" (по ГДД пример с латами 11 урона).
                usedArmorSlots++;
                result.armorSlotBroken = true;
                int leftover = Mathf.Max(0, rawDamage - hpPerSlot);
                ApplyHpDamage(leftover, result);
            }
            else
            {
                ApplyHpDamage(rawDamage, result);
            }

            return result;
        }

        private void ApplyHpDamage(int amount, DamageResult result)
        {
            if (amount <= 0) return;
            result.hpDamageDealt += amount;
            currentSlotHp -= amount;
            while (currentSlotHp <= 0 && usedHealthSlots < maxHealthSlots)
            {
                usedHealthSlots++;
                result.healthSlotsBroken++;
                if (usedHealthSlots >= maxHealthSlots) { currentSlotHp = 0; break; }
                currentSlotHp += hpPerSlot;
            }
            if (currentSlotHp > hpPerSlot) currentSlotHp = hpPerSlot;
        }

        /// <summary>Восстанавливает N шкал здоровья (для эффекта «восстановить X шкал»).</summary>
        public void HealHealthSlots(int slots)
        {
            if (slots <= 0) return;
            usedHealthSlots = Mathf.Max(0, usedHealthSlots - slots);
            currentSlotHp = hpPerSlot;
        }

        /// <summary>Восстанавливает броню полностью — вызывается в начале каждого боя.</summary>
        public void ResetArmorAtCombatStart()
        {
            usedArmorSlots = 0;
        }

        public void SpendStamina(int amount = 1)
        {
            if (currentStamina >= amount) currentStamina -= amount;
            else
            {
                // Если Выносливости не хватает — тратим ячейку HP (правило ГДД).
                int deficit = amount - currentStamina;
                currentStamina = 0;
                for (int i = 0; i < deficit && IsAlive; i++)
                {
                    usedHealthSlots++;
                    if (usedHealthSlots >= maxHealthSlots) break;
                    currentSlotHp = hpPerSlot;
                }
            }
        }

        public void RestoreStamina(int amount = 1)
        {
            currentStamina = Mathf.Clamp(currentStamina + amount, 0, maxStamina);
        }

        public void ResetStaminaAtCombatStart()
        {
            currentStamina = maxStamina;
        }

        // ================================================================
        //   ЛОКАЛИЗАЦИЯ
        // ================================================================

        public static string GetRussianName(SkillType skill) => skill switch
        {
            SkillType.BodyPower         => "Мощность тела",
            SkillType.Attentiveness     => "Внимательность",
            SkillType.Nature            => "Природа",
            SkillType.Trickery          => "Плутовство",
            SkillType.SleightOfHand     => "Ловкость рук",
            SkillType.BodyKnowledge     => "Знания тела",
            SkillType.AcademicKnowledge => "Академические знания",
            SkillType.Magic             => "Магия",
            _ => skill.ToString()
        };
    }

    [Serializable]
    public class SkillEntry
    {
        public SkillType skillType;
        public int miscBonus;

        /// <summary>Прокси на CharacterStats.GetRussianName для обратной совместимости со старым кодом.</summary>
        public static string GetRussianName(SkillType skill) => CharacterStats.GetRussianName(skill);
    }

    /// <summary>8 навыков ГДД.</summary>
    public enum SkillType
    {
        BodyPower = 0,
        Attentiveness = 1,
        Nature = 2,
        Trickery = 3,
        SleightOfHand = 4,
        BodyKnowledge = 5,
        AcademicKnowledge = 6,
        Magic = 7
    }

    public struct DamageResult
    {
        public int rawDamage;
        public bool blockedByThreshold;
        public bool armorSlotBroken;
        public int hpDamageDealt;
        public int healthSlotsBroken;
    }
}
