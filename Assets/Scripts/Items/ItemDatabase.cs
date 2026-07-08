using System;
using System.Collections.Generic;
using RPG.Character;

namespace RPG.Items
{
    /// <summary>
    /// Общий интерфейс предмета инвентаря.
    /// </summary>
    public interface IItem
    {
        string ItemId { get; }
        string DisplayName { get; }
        string Description { get; }
    }

    // ============================================================
    //   ОРУЖИЕ
    // ============================================================

    public enum WeaponSlot { Main, Offhand }
    public enum WeaponHand { OneHanded, TwoHanded }
    public enum WeaponRange { Melee, Close, Medium, Far, VeryFar } // 0/2/6/12/24 клеток
    public enum DamageKind { Physical, Magical, Any }

    [Serializable]
    public class WeaponDefinition : IItem
    {
        public string itemId;
        public string displayName;
        public string description;

        public WeaponSlot slot = WeaponSlot.Main;
        public WeaponHand hand = WeaponHand.OneHanded;

        public SkillType attackSkill = SkillType.BodyPower; // «Характеристика» по ГДД
        public WeaponRange range = WeaponRange.Melee;

        public string damageDice = "d8";     // например "d8", "d10+3", "d12+2"
        public DamageKind damageKind = DamageKind.Physical;

        public string specialText;           // текстовое описание свойств
        public int evasionMod;               // модификатор к Уклонению от оружия
        public int armorRatingMod;           // модификатор к Показателю Брони (для щитов)
        public int attackRollBonus;          // например Палаш +1 к атаке
        /// <summary>«Мощное»/«с преимуществом кости»: бросить доп. кость урона и отбросить наименьший результат.</summary>
        public bool damageAdvantage;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
    }

    public static class RangeInfo
    {
        public static int Tiles(WeaponRange r) => r switch
        {
            WeaponRange.Melee => 1,
            WeaponRange.Close => 2,
            WeaponRange.Medium => 6,
            WeaponRange.Far => 12,
            WeaponRange.VeryFar => 24,
            _ => 1
        };
    }

    // ============================================================
    //   БРОНЯ
    // ============================================================

    [Serializable]
    public class ArmorDefinition : IItem
    {
        public string itemId;
        public string displayName;
        public string description;

        public int damageThreshold;   // Базовый Порог Урона
        public int armorRating;       // Базовый Показатель Брони
        public int hpPerSlot;         // Сколько HP в одной шкале при этой броне
        public int armorSlots = 3;    // Ячейки брони

        public int evasionMod;        // например Латы -2 к Уклонению
        public int acrobaticsMod;     // Латы -1 к Акробатике (в нашей системе → к SleightOfHand)

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
    }

    // ============================================================
    //   РЕЕСТР
    // ============================================================

    public static class ItemDatabase
    {
        private static Dictionary<string, IItem> byId;

        public static void Initialize()
        {
            if (byId != null) return;
            byId = new Dictionary<string, IItem>();

            // ---- Броня (по ГДД) ----
            Register(new ArmorDefinition {
                itemId = "armor_padded", displayName = "Стёганый доспех",
                damageThreshold = 5, armorRating = 3, hpPerSlot = 6, armorSlots = 3, evasionMod = 1
            });
            Register(new ArmorDefinition {
                itemId = "armor_leather", displayName = "Кожаный доспех",
                damageThreshold = 6, armorRating = 3, hpPerSlot = 6, armorSlots = 3
            });
            Register(new ArmorDefinition {
                itemId = "armor_chain", displayName = "Кольчужный доспех",
                damageThreshold = 7, armorRating = 4, hpPerSlot = 7, armorSlots = 4, evasionMod = -1
            });
            Register(new ArmorDefinition {
                itemId = "armor_plate", displayName = "Латный доспех",
                damageThreshold = 8, armorRating = 4, hpPerSlot = 8, armorSlots = 4,
                evasionMod = -2, acrobaticsMod = -1
            });

            // ---- Основное оружие (нужное для класса-стартов и для стражников) ----
            Register(new WeaponDefinition {
                itemId = "weapon_long_sword", displayName = "Длинный меч",
                slot = WeaponSlot.Main, hand = WeaponHand.TwoHanded,
                attackSkill = SkillType.SleightOfHand, range = WeaponRange.Melee,
                damageDice = "d10+3", damageKind = DamageKind.Physical
            });
            Register(new WeaponDefinition {
                itemId = "weapon_two_handed_sword", displayName = "Двуручный меч",
                slot = WeaponSlot.Main, hand = WeaponHand.TwoHanded,
                attackSkill = SkillType.BodyPower, range = WeaponRange.Melee,
                damageDice = "d10+3", damageKind = DamageKind.Physical,
                evasionMod = -1,
                damageAdvantage = true,
                specialText = "−1 к Уклонению; при успешной атаке бросьте доп. кость урона и отбросьте наименьший результат."
            });
            Register(new WeaponDefinition {
                itemId = "weapon_dagger", displayName = "Кинжал",
                slot = WeaponSlot.Main, hand = WeaponHand.OneHanded,
                attackSkill = SkillType.SleightOfHand, range = WeaponRange.Melee,
                damageDice = "d8+1", damageKind = DamageKind.Physical
            });
            Register(new WeaponDefinition {
                itemId = "weapon_mace", displayName = "Булава",
                slot = WeaponSlot.Main, hand = WeaponHand.OneHanded,
                attackSkill = SkillType.BodyPower, range = WeaponRange.Melee,
                damageDice = "d8+1", damageKind = DamageKind.Physical
            });
            Register(new WeaponDefinition {
                itemId = "weapon_two_handed_staff", displayName = "Двуручный посох",
                slot = WeaponSlot.Main, hand = WeaponHand.TwoHanded,
                attackSkill = SkillType.AcademicKnowledge, range = WeaponRange.VeryFar,
                damageDice = "d6", damageKind = DamageKind.Magical,
                damageAdvantage = true,
                specialText = "Мощное: при успехе бросьте доп. кость урона и отбросьте наименьший."
            });
            Register(new WeaponDefinition {
                itemId = "weapon_short_staff", displayName = "Короткий посох",
                slot = WeaponSlot.Main, hand = WeaponHand.OneHanded,
                attackSkill = SkillType.Nature, range = WeaponRange.Medium,
                damageDice = "d8+1", damageKind = DamageKind.Magical
            });
            Register(new WeaponDefinition {
                itemId = "weapon_halberd", displayName = "Алебарда",
                slot = WeaponSlot.Main, hand = WeaponHand.TwoHanded,
                attackSkill = SkillType.BodyPower, range = WeaponRange.Close,
                damageDice = "d10+2", damageKind = DamageKind.Physical
            });
            Register(new WeaponDefinition {
                itemId = "weapon_heavy_crossbow", displayName = "Тяжёлый арбалет",
                slot = WeaponSlot.Main, hand = WeaponHand.TwoHanded,
                attackSkill = SkillType.SleightOfHand, range = WeaponRange.Far,
                damageDice = "d10+1", damageKind = DamageKind.Physical
            });

            // ---- Вторичное оружие ----
            Register(new WeaponDefinition {
                itemId = "weapon_round_shield", displayName = "Круглый щит",
                slot = WeaponSlot.Offhand, hand = WeaponHand.OneHanded,
                attackSkill = SkillType.BodyPower, range = WeaponRange.Melee,
                damageDice = "d4", damageKind = DamageKind.Physical,
                armorRatingMod = 1,
                specialText = "+1 к Показателю Брони"
            });
            Register(new WeaponDefinition {
                itemId = "weapon_small_dagger_off", displayName = "Малый кинжал",
                slot = WeaponSlot.Offhand, hand = WeaponHand.OneHanded,
                attackSkill = SkillType.SleightOfHand, range = WeaponRange.Melee,
                damageDice = "d8", damageKind = DamageKind.Physical,
                specialText = "+2 к урону основного оружия по целям вплотную."
            });
        }

        private static void Register(IItem item) => byId[item.ItemId] = item;

        public static IItem GetItem(string id)
        {
            if (byId == null) Initialize();
            if (string.IsNullOrEmpty(id)) return null;
            return byId.TryGetValue(id, out var it) ? it : null;
        }

        public static WeaponDefinition GetWeapon(string id) => GetItem(id) as WeaponDefinition;
        public static ArmorDefinition  GetArmor(string id)  => GetItem(id) as ArmorDefinition;

        // ============================================================
        //   ПРИМЕНЕНИЕ ЭКИПИРОВКИ К СТАТАМ
        // ============================================================

        public static void ApplyEquippedGear(CharacterBase c)
        {
            if (c == null) return;
            var armor = GetArmor(c.equippedArmorId);
            if (armor != null)
            {
                c.stats.maxArmorSlots  = armor.armorSlots;
                c.stats.armorRating    = armor.armorRating;
                c.stats.damageThreshold= armor.damageThreshold;
                c.stats.hpPerSlot      = armor.hpPerSlot;
                c.stats.currentSlotHp  = armor.hpPerSlot;
                c.stats.evasion       += armor.evasionMod;
                // Штраф к SleightOfHand за латы — по ГДД аналог "Акробатики"
                c.stats.sleightOfHand += armor.acrobaticsMod;
            }
            else
            {
                c.stats.maxArmorSlots = 0;
                c.stats.armorRating = 0;
                c.stats.damageThreshold = 0;
                if (c.stats.hpPerSlot <= 0) c.stats.hpPerSlot = 4;
                if (c.stats.currentSlotHp <= 0) c.stats.currentSlotHp = c.stats.hpPerSlot;
            }

            // Модификатор щита к брони.
            var offhand = GetWeapon(c.equippedOffhandId);
            if (offhand != null)
            {
                c.stats.armorRating  += offhand.armorRatingMod;
                c.stats.evasion      += offhand.evasionMod;
            }
        }
    }
}
