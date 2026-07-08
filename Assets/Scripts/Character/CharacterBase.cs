using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;
using RPG.Domains;

namespace RPG.Character
{
    /// <summary>
    /// Базовый класс для любого персонажа (игрок, компаньон, NPC-союзник).
    /// Приведён к ГДД: 8 навыков, ячейки здоровья/брони, Выносливость, домены+карты.
    /// </summary>
    [Serializable]
    public class CharacterBase
    {
        public string characterId;
        public string displayName;
        public RaceType race;
        public ClassType characterClass;
        public string subclassId;
        /// <summary>Активная стойка Мастера БИ (brutal/defensive/grapple/sturdy/precise/fast).</summary>
        public string subclassStance;
        public Gender gender;
        public CharacterStats stats;

        // Выбранные домены (по ГДД — обычно 2).
        public List<DomainType> chosenDomains = new();

        // Взятые карты доменов (id из DomainDatabase).
        public List<string> knownDomainCards = new();

        // Инвентарь и снаряжение.
        public List<string> inventory = new();
        public string equippedMainWeaponId;
        public string equippedOffhandId;
        public string equippedArmorId;

        // Активные эффекты (бафф/дебафф/состояния).
        public List<StatusEffect> activeEffects = new();

        // Флаги персонажа.
        public Dictionary<string, bool> characterFlags = new();

        public CharacterBase()
        {
            stats = new CharacterStats();
        }

        public virtual void Initialize()
        {
            // По ГДД у рас нет бонусов к навыкам — только особенности, которые обрабатываются в бою.
            // Здесь можно позже отработать «пассивные» расовые эффекты (например, у Человека maxStamina+1).
            ApplyRacePassives();
        }

        private void ApplyRacePassives()
        {
            var race = RaceDatabase.GetRace(this.race);
            if (race == null) return;

            foreach (var f in race.features)
            {
                if (f.trigger != RaceFeatureTrigger.Passive) continue;
                switch (f.featureId)
                {
                    case "human_high_stamina":
                        stats.maxStamina = Mathf.Max(stats.maxStamina, 2);
                        stats.currentStamina = stats.maxStamina;
                        break;
                    // "orc_hardy" (стойкий) обрабатывается в бою по условию.
                }
            }
        }

        // ---------- Флаги/инвентарь ----------

        public void SetFlag(string flag, bool value = true) => characterFlags[flag] = value;
        public bool GetFlag(string flag) => characterFlags.TryGetValue(flag, out var v) && v;

        public void AddItem(string itemId)
        {
            if (!inventory.Contains(itemId)) inventory.Add(itemId);
        }
        public void RemoveItem(string itemId) => inventory.Remove(itemId);
        public bool HasItem(string itemId) => inventory.Contains(itemId);

        // ---------- Эффекты ----------

        public void ApplyEffect(StatusEffect effect)
        {
            activeEffects.Add(effect);
            effect.OnApply(this);
        }

        public bool HasEffect(string effectId) => activeEffects.Exists(e => e.effectId == effectId);

        public void RemoveEffect(string effectId)
        {
            var e = activeEffects.Find(x => x.effectId == effectId);
            if (e != null) { e.OnRemove(this); activeEffects.Remove(e); }
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
    /// Класс игрока с дополнительной функциональностью.
    /// </summary>
    [Serializable]
    public class PlayerCharacter : CharacterBase
    {
        public string playerName;
        public string background;
        public int gold = 0;

        public Dictionary<string, int> factionReputation = new();

        public override void Initialize()
        {
            base.Initialize();
            // Классовые особенности сейчас декларативны (описания) — сработают в CombatManager.
        }

        public int GetFactionReputation(string factionId)
            => factionReputation.TryGetValue(factionId, out int rep) ? rep : 0;

        public void ModifyFactionReputation(string factionId, int change)
        {
            factionReputation.TryGetValue(factionId, out int r);
            factionReputation[factionId] = r + change;
        }

        public void AddGold(int amount) => gold += amount;
        public bool SpendGold(int amount) { if (gold < amount) return false; gold -= amount; return true; }
    }

    [Serializable]
    public class StatusEffect
    {
        public string effectId;
        public string displayName;
        public string description;
        public int remainingDuration;
        public EffectType effectType;
        public int magnitude;

        public virtual void OnApply(CharacterBase c) { }
        public virtual void OnRemove(CharacterBase c) { }
        public virtual void OnTick(CharacterBase c) { }
    }

    public enum EffectType
    {
        Buff, Debuff,
        DamageOverTime, HealOverTime,
        Stun, Charm, Fear, Poison, Curse,
        Vulnerable, Immobilized, Asleep, Ignited, Restrained
    }

    public enum Gender { Male, Female }
}
