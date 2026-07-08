using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using RPG.Domains;
using RPG.Items;

namespace RPG.Character
{
    /// <summary>
    /// Создание персонажа по ГДД.
    /// Порядок шагов:
    ///   1. Имя, пол.
    ///   2. Раса (Человек / Эльф / Тифлинг).
    ///   3. Класс (Воин / Плут / Волшебник / Друид / Священник).
    ///   4. Подкласс класса.
    ///   5. Распределение навыков: +2, +1, +1, -1, остальные 0.
    ///   6. Выбор первого домена (из стартовых для класса) и второго (любой).
    ///   7. Выбор 2 стартовых карт из 6 доступных (3 карты каждого выбранного домена).
    ///   8. FinalizeCharacter().
    /// </summary>
    public class CharacterCreation : MonoBehaviour
    {
        public static CharacterCreation Instance { get; private set; }

        [Header("Правила распределения навыков (по ГДД)")]
        [SerializeField] private int slotsAtPlusTwo  = 1;
        [SerializeField] private int slotsAtPlusOne  = 2;
        [SerializeField] private int slotsAtMinusOne = 1;

        [SerializeField] private int startingDomainCards = 2;

        private PlayerCharacter character;
        private Dictionary<SkillType, int> skillAssignments = new();

        public PlayerCharacter Character => character;

        public int RequiredPlusTwo  => slotsAtPlusTwo;
        public int RequiredPlusOne  => slotsAtPlusOne;
        public int RequiredMinusOne => slotsAtMinusOne;

        public int RemainingCardPicks =>
            Mathf.Max(0, startingDomainCards - character.knownDomainCards.Count);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            RaceDatabase.Initialize();
            ClassDatabase.Initialize();
            DomainDatabase.Initialize();
            ItemDatabase.Initialize();
            ResetCreation();
        }

        public void ResetCreation()
        {
            character = new PlayerCharacter { characterId = "player" };
            skillAssignments.Clear();
            foreach (SkillType s in Enum.GetValues(typeof(SkillType)))
                skillAssignments[s] = 0;
            SyncSkillsIntoStats();
        }

        // ---------- 1. Имя, пол ----------
        public void SetName(string name)
        {
            character.playerName = name;
            character.displayName = name;
        }
        public void SetGender(Gender g) => character.gender = g;

        // ---------- 2. Раса ----------
        public bool SelectRace(RaceType race)
        {
            if (!RaceDatabase.PlayableRaces.Contains(race)) return false;
            character.race = race;
            return true;
        }

        // ---------- 3. Класс ----------
        public bool SelectClass(ClassType cls)
        {
            if (!ClassDatabase.PlayableClasses.Contains(cls)) return false;
            character.characterClass = cls;
            character.subclassId = null;
            character.chosenDomains.Clear();
            character.knownDomainCards.Clear();
            var def = ClassDatabase.GetClass(cls);
            character.stats.evasion = def.baseEvasion;
            character.stats.maxHealthSlots = def.baseHealthSlots;
            character.stats.usedHealthSlots = 0;
            return true;
        }

        // ---------- 4. Подкласс ----------
        public bool SelectSubclass(string subclassId)
        {
            var cls = ClassDatabase.GetClass(character.characterClass);
            if (cls == null) return false;
            if (!cls.subclasses.Any(s => s.subclassId == subclassId)) return false;
            character.subclassId = subclassId;
            return true;
        }

        // ---------- 5. Навыки ----------

        /// <summary>Устанавливает значение бонуса навыка (в -1 / 0 / +1 / +2). Возвращает false, если превышена квота ГДД.</summary>
        public bool SetSkill(SkillType skill, int value)
        {
            if (value != -1 && value != 0 && value != 1 && value != 2) return false;

            var snapshot = new Dictionary<SkillType, int>(skillAssignments);
            snapshot[skill] = value;

            int plusTwo  = snapshot.Values.Count(v => v == 2);
            int plusOne  = snapshot.Values.Count(v => v == 1);
            int minusOne = snapshot.Values.Count(v => v == -1);

            if (plusTwo  > slotsAtPlusTwo)  return false;
            if (plusOne  > slotsAtPlusOne)  return false;
            if (minusOne > slotsAtMinusOne) return false;

            skillAssignments = snapshot;
            SyncSkillsIntoStats();
            return true;
        }

        public int GetSkill(SkillType skill) => skillAssignments.TryGetValue(skill, out var v) ? v : 0;

        public bool IsSkillDistributionComplete()
        {
            int plusTwo  = skillAssignments.Values.Count(v => v == 2);
            int plusOne  = skillAssignments.Values.Count(v => v == 1);
            int minusOne = skillAssignments.Values.Count(v => v == -1);
            return plusTwo == slotsAtPlusTwo && plusOne == slotsAtPlusOne && minusOne == slotsAtMinusOne;
        }

        private void SyncSkillsIntoStats()
        {
            foreach (var kv in skillAssignments)
                character.stats.SetSkillBonus(kv.Key, kv.Value);
        }

        // ---------- 6. Домены ----------

        /// <summary>Первый домен — обязательно из starterDomains класса.</summary>
        public bool SelectFirstDomain(DomainType d)
        {
            var cls = ClassDatabase.GetClass(character.characterClass);
            if (cls == null) return false;
            if (!cls.starterDomains.Contains(d)) return false;
            if (character.chosenDomains.Count == 0) character.chosenDomains.Add(d);
            else character.chosenDomains[0] = d;
            character.knownDomainCards.Clear();
            return true;
        }

        /// <summary>Второй домен — любой из 8, кроме первого.</summary>
        public bool SelectSecondDomain(DomainType d)
        {
            if (character.chosenDomains.Count == 0) return false;
            if (character.chosenDomains[0] == d) return false;
            if (character.chosenDomains.Count == 1) character.chosenDomains.Add(d);
            else character.chosenDomains[1] = d;
            character.knownDomainCards.Clear();
            return true;
        }

        // ---------- 7. Карты доменов ----------

        public List<DomainCard> GetAvailableCards()
        {
            var result = new List<DomainCard>();
            foreach (var d in character.chosenDomains)
                result.AddRange(DomainDatabase.GetCardsForDomain(d));
            return result;
        }

        public bool ToggleCard(string cardId)
        {
            if (character.knownDomainCards.Contains(cardId))
            {
                character.knownDomainCards.Remove(cardId);
                return true;
            }
            if (character.knownDomainCards.Count >= startingDomainCards) return false;

            var card = DomainDatabase.GetCard(cardId);
            if (card == null) return false;
            if (!character.chosenDomains.Contains(card.domain)) return false;

            character.knownDomainCards.Add(cardId);
            return true;
        }

        /// <summary>Применить перманентные модификаторы подкласса (Боевой маг +1 ячейка ран, Адепт +1 к навыку и т.п.).</summary>
        private void ApplySubclassCreationEffects(ClassDefinition cls)
        {
            if (cls == null || string.IsNullOrEmpty(character.subclassId)) return;
            switch (character.subclassId)
            {
                case "mage_school_of_war":
                    // "Боевой маг": +1 ячейка ран.
                    character.stats.maxHealthSlots += 1;
                    break;
                // Прочие подклассовые перманентки применяются в бою через флаги PassiveEffectsRegistry.
            }
        }

        // ---------- 8. Финализация ----------

        public bool CanFinalize()
        {
            if (string.IsNullOrEmpty(character.playerName)) return false;
            if (!RaceDatabase.PlayableRaces.Contains(character.race)) return false;
            if (!ClassDatabase.PlayableClasses.Contains(character.characterClass)) return false;
            if (string.IsNullOrEmpty(character.subclassId)) return false;
            if (!IsSkillDistributionComplete()) return false;
            if (character.chosenDomains.Count != 2) return false;
            if (character.knownDomainCards.Count != startingDomainCards) return false;
            return true;
        }

        public PlayerCharacter FinalizeCharacter()
        {
            if (!CanFinalize())
            {
                Debug.LogError("[CharacterCreation] Cannot finalize: missing required fields");
                return null;
            }

            // Стартовое снаряжение — из класса.
            var cls = ClassDatabase.GetClass(character.characterClass);
            foreach (var itemId in cls.startingEquipment)
            {
                character.AddItem(itemId);
                var item = ItemDatabase.GetItem(itemId);
                if (item is WeaponDefinition w)
                {
                    if (w.slot == WeaponSlot.Main) character.equippedMainWeaponId = itemId;
                    else                            character.equippedOffhandId  = itemId;
                }
                else if (item is ArmorDefinition)
                {
                    character.equippedArmorId = itemId;
                }
            }

            // Применяем броню к статам.
            ItemDatabase.ApplyEquippedGear(character);

            // Подклассовые модификаторы, применяемые вне боя (перманентные).
            ApplySubclassCreationEffects(cls);

            // Раса — пассивные особенности (например, +1 к макс. Выносливости у человека).
            character.Initialize();

            // На старте — полный запас Выносливости и целые шкалы здоровья.
            character.stats.ResetArmorAtCombatStart();
            character.stats.ResetStaminaAtCombatStart();
            character.stats.currentSlotHp = character.stats.hpPerSlot;
            character.stats.usedHealthSlots = 0;

            Debug.Log($"[CharacterCreation] Готов: {character.playerName} " +
                      $"({character.race}/{character.characterClass}/{character.subclassId}) " +
                      $"HP шкал {character.stats.maxHealthSlots}×{character.stats.hpPerSlot}, " +
                      $"броня {character.stats.maxArmorSlots} (DT {character.stats.damageThreshold}), " +
                      $"Уклонение {character.stats.evasion}, " +
                      $"Выносливость {character.stats.currentStamina}/{character.stats.maxStamina}");
            return character;
        }
    }
}
