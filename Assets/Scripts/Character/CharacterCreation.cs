using UnityEngine;
using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>
    /// Контроллер создания персонажа. Управляет UI и валидацией.
    /// </summary>
    public class CharacterCreation : MonoBehaviour
    {
        public static CharacterCreation Instance { get; private set; }

        [Header("Creation Settings")]
        [SerializeField] private int totalAttributePoints = 2;
        [SerializeField] private int minAttribute = -1;
        [SerializeField] private int maxAttribute = 2;
        [SerializeField] private int skillProficiencyPoints = 2;

        private PlayerCharacter character;
        private int spentPoints = 0;
        private int selectedProficiencies = 0;

        public PlayerCharacter Character => character;
        public int RemainingPoints => totalAttributePoints - spentPoints;
        public int RemainingProficiencies => skillProficiencyPoints - selectedProficiencies;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            RaceDatabase.Initialize();
            ClassDatabase.Initialize();
            ResetCreation();
        }

        public void ResetCreation()
        {
            character = new PlayerCharacter
            {
                characterId = "player",
                stats = new CharacterStats()
            };
            spentPoints = 0;
            selectedProficiencies = 0;
        }

        #region Name & Gender

        public void SetName(string name)
        {
            character.playerName = name;
            character.displayName = name;
        }

        public void SetGender(Gender gender)
        {
            character.gender = gender;
        }

        #endregion

        #region Race Selection

        public bool SelectRace(RaceType raceType)
        {
            var raceDef = RaceDatabase.GetRace(raceType);
            if (raceDef == null) return false;

            character.race = raceType;

            // Применяем расовые бонусы к базовым статам
            ApplyRaceBonuses();

            return true;
        }

        private void ApplyRaceBonuses()
        {
            var raceDef = RaceDatabase.GetRace(character.race);
            if (raceDef == null) return;

            foreach (AttributeType attr in Enum.GetValues(typeof(AttributeType)))
            {
                int bonus = raceDef.attributeBonuses.TryGetValue(attr, out int val) ? val : 0;
                character.stats.SetAttributeValue(attr, bonus);
            }
        }

        #endregion

        #region Class Selection

        public bool SelectClass(ClassType classType)
        {
            var classDef = ClassDatabase.GetClass(classType);
            if (classDef == null) return false;

            character.characterClass = classType;
            character.knownAbilities.Clear();
            character.knownAbilities.AddRange(classDef.startingAbilities);

            // Устанавливаем навыки класса
            character.stats.skills.Clear();
            foreach (var skill in classDef.classSkillOptions)
            {
                character.stats.skills.Add(new SkillEntry
                {
                    skillType = skill,
                    isProficient = false
                });
            }

            return true;
        }

        #endregion

        #region Attribute Distribution

        /// <summary>
        /// Point-buy система Веридии: -1 = -1 очко, 0 = 0 очков, +1 = 1 очко, +2 = 2 очка, +3 = 4 очка
        /// </summary>
        public int GetPointCost(int attributeValue)
        {
            if (attributeValue == -1) return -1;
            if (attributeValue == 0) return 0;
            if (attributeValue == 1) return 1;
            if (attributeValue == 2) return 2;
            if (attributeValue == 3) return 4;
            return 0;
        }

        public bool SetAttribute(AttributeType attribute, int value)
        {
            if (value < minAttribute || value > maxAttribute)
                return false;

            int currentCost = CalculateCurrentAttributeCost();
            int currentVal = character.stats.GetAttributeValue(attribute) - GetRaceBonus(attribute);

            currentCost -= GetPointCost(currentVal);
            currentCost += GetPointCost(value);

            if (currentCost > totalAttributePoints)
                return false;

            character.stats.SetAttributeValue(attribute, value + GetRaceBonus(attribute));
            spentPoints = currentCost;
            character.stats.RecalculateDerivedStats();
            return true;
        }

        private int GetRaceBonus(AttributeType attribute)
        {
            var raceDef = RaceDatabase.GetRace(character.race);
            if (raceDef == null) return 0;
            return raceDef.attributeBonuses.TryGetValue(attribute, out int bonus) ? bonus : 0;
        }

        private int CalculateCurrentAttributeCost()
        {
            int cost = 0;
            foreach (AttributeType attr in Enum.GetValues(typeof(AttributeType)))
            {
                int baseVal = character.stats.GetAttributeValue(attr) - GetRaceBonus(attr);
                cost += GetPointCost(baseVal);
            }
            return cost;
        }

        #endregion

        #region Skill Proficiencies

        public bool ToggleSkillProficiency(SkillType skill)
        {
            var entry = character.stats.skills.Find(s => s.skillType == skill);
            if (entry == null) return false;

            if (entry.isProficient)
            {
                entry.isProficient = false;
                selectedProficiencies--;
                return true;
            }
            else
            {
                if (selectedProficiencies >= skillProficiencyPoints)
                    return false;
                entry.isProficient = true;
                selectedProficiencies++;
                return true;
            }
        }

        #endregion

        #region Finalization

        public bool CanFinalize()
        {
            if (string.IsNullOrEmpty(character.playerName))
                return false;
            if (character.race == default && !Enum.IsDefined(typeof(RaceType), character.race))
                return false;
            if (character.characterClass == default && !Enum.IsDefined(typeof(ClassType), character.characterClass))
                return false;
            return true;
        }

        public PlayerCharacter FinalizeCharacter()
        {
            if (!CanFinalize())
            {
                Debug.LogError("[CharacterCreation] Cannot finalize: missing required fields");
                return null;
            }

            character.Initialize();
            character.stats.currentHP = character.stats.maxHP;
            character.stats.currentMP = character.stats.maxMP;

            Debug.Log($"[CharacterCreation] Character created: {character.playerName} " +
                      $"({character.race} {character.characterClass})");

            return character;
        }

        #endregion
    }
}
