using UnityEngine;
using System.Collections.Generic;
using RPG.Character;
using RPG.Combat;

namespace RPG.Domains
{
    /// <summary>
    /// Применяет пассивные карты доменов и классовые пассивки к статам юнита при старте боя.
    /// Также ставит флаги на юните для карт, требующих спец-обработки в ходе боя
    /// (например, «Так не пойдёт» — при броске урона перекатить 1 и 2).
    /// </summary>
    public static class PassiveEffectsRegistry
    {
        /// <summary>Флаг «Так не пойдёт»: перекатывать 1 и 2 в урон.</summary>
        public const string FlagNoWay = "flag_weapon_no_way";
        /// <summary>Флаг «Неуловимый»: бонус к уклонению = level/2.</summary>
        public const string FlagElusive = "flag_body_elusive";
        /// <summary>Флаг «Защитное мастерство»: +DT = level.</summary>
        public const string FlagDefensiveMastery = "flag_defense_mastery";

        // Классовые
        public const string FlagWarriorProvoke = "flag_class_warrior_provoke";
        public const string FlagRogueSneakAttack = "flag_class_rogue_sneak";
        public const string FlagMageStrangePatterns = "flag_class_mage_strange_patterns";
        public const string FlagClericFaith = "flag_class_cleric_faith";
        public const string FlagDruidBeastForm = "flag_class_druid_beast_form";

        // Подклассы (только те, что можно "включить" в первой итерации)
        public const string FlagBerserker = "flag_sub_berserker";
        public const string FlagBulwark = "flag_sub_bulwark";
        public const string FlagExecutionerFirstStrike = "flag_sub_first_strike";
        public const string FlagPoisonerMixtures = "flag_sub_toxic_mixtures";
        public const string FlagPriestPrayer = "flag_sub_prayer_available";
        public const string FlagPunisherRetribution = "flag_sub_retribution_used";

        // Проверка знания карты
        public static bool HasCard(CharacterBase c, string cardId)
            => c != null && c.knownDomainCards != null && c.knownDomainCards.Contains(cardId);

        /// <summary>Применить все пассивки: подкрутить статы + расставить флаги.</summary>
        public static void ApplyOnCombatStart(CombatUnit unit)
        {
            if (unit == null || unit.character == null) return;
            var c = unit.character;
            var s = unit.stats;
            int level = Mathf.Max(1, s.level);

            // ---------- Пассивные карты ----------
            if (HasCard(c, "weapon_1_no_way"))
                c.SetFlag(FlagNoWay, true);

            if (HasCard(c, "body_1_elusive"))
                s.evasion += level / 2;

            if (HasCard(c, "defense_1_defensive_mastery"))
                s.damageThreshold += level;

            // ---------- Классовые фичи (декларативно — просто ставим флаги) ----------
            switch (c.characterClass)
            {
                case ClassType.Warrior: c.SetFlag(FlagWarriorProvoke, true); break;
                case ClassType.Rogue:   c.SetFlag(FlagRogueSneakAttack, true); break;
                case ClassType.Mage:    c.SetFlag(FlagMageStrangePatterns, true); break;
                case ClassType.Cleric:  c.SetFlag(FlagClericFaith, true); break;
                case ClassType.Druid:   c.SetFlag(FlagDruidBeastForm, true); break;
            }

            // ---------- Подклассовые фичи ----------
            switch (c.subclassId)
            {
                case "warrior_berserker": c.SetFlag(FlagBerserker, true); break;
                case "warrior_bulwark":   c.SetFlag(FlagBulwark, true); s.damageThreshold += 1; break;
                case "rogue_executioners_guild": c.SetFlag(FlagExecutionerFirstStrike, true); break;
                case "rogue_poisoners_guild":    c.SetFlag(FlagPoisonerMixtures, true); break;
                case "cleric_priest": c.SetFlag(FlagPriestPrayer, true); break;
                case "cleric_punisher": c.SetFlag(FlagPunisherRetribution, false); break; // false = ещё не использовано в этом бою
            }
        }
    }
}
