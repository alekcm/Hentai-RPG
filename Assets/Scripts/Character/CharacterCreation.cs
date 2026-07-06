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
        [SerializeField] private int totalAttributePoints = 27;
        [SerializeField] private int minAttribute = 8;
        [SerializeField] private int maxAttribute = 15;
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

            // Сначала сбрасываем к базовым
            foreach (var bonus in raceDef.attributeBonuses)
            {
                switch (bonus.Key)
                {
                    case AttributeType.Strength:
                        character.stats.strength = 10 + bonus.Value;
                        break;
                    case AttributeType.Dexterity:
                        character.stats.dexterity = 10 + bonus.Value;
                        break;
                    case AttributeType.Constitution:
                        character.stats.constitution = 10 + bonus.Value;
                        break;
                    case AttributeType.Intelligence:
                        character.stats.intelligence = 10 + bonus.Value;
                        break;
                    case AttributeType.Wisdom:
                        character.stats.wisdom = 10 + bonus.Value;
                        break;
                    case AttributeType.Charisma:
                        character.stats.charisma = 10 + bonus.Value;
                        break;
                }
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
        /// Point-buy система: 8=0 очков, 9=1, 10=2, 11=3, 12=4, 13=5, 14=7, 15=9
        /// </summary>
        public int GetPointCost(int attributeValue)
        {
            if (attributeValue < minAttribute) return 0;
            if (attributeValue <= 13) return attributeValue - 8;
            if (attributeValue == 14) return 7;
            if (attributeValue == 15) return 9;
            return int.MaxValue;
        }

        public bool SetAttribute(AttributeType attribute, int value)
        {
            if (value < minAttribute || value > maxAttribute)
                return false;

            // Вычисляем текущую стоимость
            int currentCost = CalculateCurrentAttributeCost();
            int currentAttrValue = character.stats.GetAttributeValue(attribute);

            // Убираем стоимость текущего значения
            int removeCost = GetPointCost(currentAttrValue);
            currentCost -= removeCost;

            // Добавляем стоимость нового значения
            int addCost = GetPointCost(value);
            currentCost += addCost;

            if (currentCost > totalAttributePoints)
                return false;

            // Применяем
            switch (attribute)
            {
                case AttributeType.Strength:
                    character.stats.strength = value + GetRaceBonus(AttributeType.Strength);
                    break;
                case AttributeType.Dexterity:
                    character.stats.dexterity = value + GetRaceBonus(AttributeType.Dexterity);
                    break;
                case AttributeType.Constitution:
                    character.stats.constitution = value + GetRaceBonus(AttributeType.Constitution);
                    break;
                case AttributeType.Intelligence:
                    character.stats.intelligence = value + GetRaceBonus(AttributeType.Intelligence);
                    break;
                case AttributeType.Wisdom:
                    character.stats.wisdom = value + GetRaceBonus(AttributeType.Wisdom);
                    break;
                case AttributeType.Charisma:
                    character.stats.charisma = value + GetRaceBonus(AttributeType.Charisma);
                    break;
            }

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
            cost += GetPointCost(character.stats.strength - GetRaceBonus(AttributeType.Strength));
            cost += GetPointCost(character.stats.dexterity - GetRaceBonus(AttributeType.Dexterity));
            cost += GetPointCost(character.stats.constitution - GetRaceBonus(AttributeType.Constitution));
            cost += GetPointCost(character.stats.intelligence - GetRaceBonus(AttributeType.Intelligence));
            cost += GetPointCost(character.stats.wisdom - GetRaceBonus(AttributeType.Wisdom));
            cost += GetPointCost(character.stats.charisma - GetRaceBonus(AttributeType.Charisma));
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
