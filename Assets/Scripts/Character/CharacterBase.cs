using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;

namespace RPG.Character
{
    /// <summary>
    /// Базовый класс для любого персонажа (игрок, компаньон, NPC)
    /// </summary>
    [Serializable]
    public class CharacterBase
    {
        public string characterId;
        public string displayName;
        public RaceType race;
        public ClassType characterClass;
        public CharacterStats stats;
        public Gender gender;

        // Инвентарь
        public List<string> inventory = new();
        public List<string> equippedItems = new();

        // Активные эффекты
        public List<StatusEffect> activeEffects = new();

        // Флаги персонажа
        public Dictionary<string, bool> characterFlags = new();

        public CharacterBase()
        {
            stats = new CharacterStats();
        }

        public virtual void Initialize()
        {
            ApplyRaceBonuses();
            stats.RecalculateDerivedStats();
        }

        protected void ApplyRaceBonuses()
        {
            var raceDef = RaceDatabase.GetRace(race);
            if (raceDef == null) return;

            foreach (var bonus in raceDef.attributeBonuses)
            {
                switch (bonus.Key)
                {
                    case AttributeType.Strength: stats.strength += bonus.Value; break;
                    case AttributeType.Dexterity: stats.dexterity += bonus.Value; break;
                    case AttributeType.Constitution: stats.constitution += bonus.Value; break;
                    case AttributeType.Intelligence: stats.intelligence += bonus.Value; break;
                    case AttributeType.Wisdom: stats.wisdom += bonus.Value; break;
                    case AttributeType.Charisma: stats.charisma += bonus.Value; break;
                }
            }
        }

        public void SetFlag(string flag, bool value = true)
        {
            characterFlags[flag] = value;
        }

        public bool GetFlag(string flag)
        {
            return characterFlags.TryGetValue(flag, out bool value) && value;
        }

        public void AddItem(string itemId)
        {
            if (!inventory.Contains(itemId))
                inventory.Add(itemId);
        }

        public void RemoveItem(string itemId)
        {
            inventory.Remove(itemId);
        }

        public bool HasItem(string itemId)
        {
            return inventory.Contains(itemId);
        }

        public void ApplyEffect(StatusEffect effect)
        {
            activeEffects.Add(effect);
            effect.OnApply(this);
        }

        public void RemoveEffect(string effectId)
        {
            var effect = activeEffects.Find(e => e.effectId == effectId);
            if (effect != null)
            {
                effect.OnRemove(this);
                activeEffects.Remove(effect);
            }
        }

        public void TickEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                activeEffects[i].OnTick(this);
                activeEffects[i].remainingDuration--;
                if (activeEffects[i].remainingDuration <= 0)
                {
                    activeEffects[i].OnRemove(this);
                    activeEffects.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Класс игрока с дополнительной функциональностью
    /// </summary>
    [Serializable]
    public class PlayerCharacter : CharacterBase
    {
        public string playerName;
        public string background;
        public List<string> knownSpells = new();
        public List<string> knownAbilities = new();
        public int gold = 50;

        // Репутация у фракций
        public Dictionary<string, int> factionReputation = new();

        public override void Initialize()
        {
            base.Initialize();
            ApplyClassAbilities();
        }

        private void ApplyClassAbilities()
        {
            var classDef = ClassDatabase.GetClass(characterClass);
            if (classDef == null) return;

            knownAbilities.AddRange(classDef.startingAbilities);

            // Создаём записи навыков
            foreach (var skill in classDef.classSkillOptions)
            {
                var entry = new SkillEntry
                {
                    skillType = skill,
                    isProficient = false
                };
                if (!stats.skills.Exists(s => s.skillType == skill))
                    stats.skills.Add(entry);
            }
        }

        public void SetSkillProficiency(SkillType skill, bool proficient = true)
        {
            var entry = stats.skills.Find(s => s.skillType == skill);
            if (entry != null)
                entry.isProficient = proficient;
            else
                stats.skills.Add(new SkillEntry { skillType = skill, isProficient = proficient });
        }

        public int GetFactionReputation(string factionId)
        {
            return factionReputation.TryGetValue(factionId, out int rep) ? rep : 0;
        }

        public void ModifyFactionReputation(string factionId, int change)
        {
            if (!factionReputation.ContainsKey(factionId))
                factionReputation[factionId] = 0;
            factionReputation[factionId] += change;
        }

        public void AddGold(int amount)
        {
            gold += amount;
        }

        public bool SpendGold(int amount)
        {
            if (gold >= amount)
            {
                gold -= amount;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Эффект статуса (бафф/дебафф)
    /// </summary>
    [Serializable]
    public class StatusEffect
    {
        public string effectId;
        public string displayName;
        public string description;
        public int remainingDuration; // в ходах или минутах
        public EffectType effectType;
        public int magnitude;

        public virtual void OnApply(CharacterBase character) { }
        public virtual void OnRemove(CharacterBase character) { }
        public virtual void OnTick(CharacterBase character) { }
    }

    public enum EffectType
    {
        Buff,
        Debuff,
        DamageOverTime,
        HealOverTime,
        Stun,
        Charm,
        Fear,
        Poison,
        Curse
    }

    public enum Gender
    {
        Male,
        Female,
        Other
    }
}
