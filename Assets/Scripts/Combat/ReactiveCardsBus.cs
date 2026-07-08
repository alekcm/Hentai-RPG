using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPG.Character;
using RPG.Domains;
using RPG.UI;

namespace RPG.Combat
{
    /// <summary>
    /// Слушает CombatTriggerBus и предлагает игроку активировать подходящие реактивные карты доменов.
    /// Все траты (Надежда/Выносливость) идут через AbilityConfirmDialog — ничего не тратится автоматически.
    /// </summary>
    public static class ReactiveCardsBus
    {
        private static List<CombatUnit> playerUnits;
        private static readonly HashSet<string> lightEncourageUsed = new();     // per-combat, ключ = unitId

        public static void Initialize(IEnumerable<CombatUnit> units)
        {
            playerUnits = units.Where(u => u.side == CombatSide.Player && u.character != null).ToList();
            lightEncourageUsed.Clear();

            CombatTriggerBus.OnIncomingDamage       += OnIncomingDamage;
            CombatTriggerBus.OnAllyIncomingDamage   += OnAllyIncomingDamage;
            CombatTriggerBus.OnAfterRoll            += OnAfterRoll;
        }

        // ---------------- Обработчики ----------------

        /// <summary>«Вернуться в строй», «Увёртливость».</summary>
        private static void OnIncomingDamage(IncomingDamageContext ctx)
        {
            if (ctx == null || ctx.target?.side != CombatSide.Player) return;
            var unit = ctx.target;
            var ch = unit.character;
            if (ch == null) return;

            // Увёртливость — если атака не вплотную и есть Выносливость.
            if (PassiveEffectsRegistry.HasCard(ch, "body_1_evasive")
                && ctx.attacker != null
                && CombatManager.ManhattanDistance(ctx.attacker.gridPosition, unit.gridPosition) > 1
                && unit.stats.currentStamina >= 1
                && !ctx.cancelled)
            {
                AbilityConfirmDialog.Show(
                    title: "Использовать «Увёртливость»?",
                    description: $"Полностью уклониться от атаки {ctx.attacker.displayName} ({ctx.rawDamage} сырого урона)?",
                    resources: $"Стоимость: 1 Выносливость (у вас {unit.stats.currentStamina}).",
                    yes: () =>
                    {
                        unit.stats.SpendStamina(1);
                        ctx.cancelled = true;
                    });
                if (ctx.cancelled) return;
            }

            // Вернуться в строй — при СЕРЬЁЗНОМ уроне (пробивает броню и уходит по здоровью).
            bool willBeSerious = ctx.rawDamage >= unit.stats.damageThreshold
                              && ctx.rawDamage >= unit.stats.hpPerSlot;
            if (PassiveEffectsRegistry.HasCard(ch, "weapon_1_second_wind")
                && willBeSerious
                && unit.stats.currentStamina >= 1
                && !ctx.cancelled)
            {
                AbilityConfirmDialog.Show(
                    title: "Использовать «Вернуться в строй»?",
                    description: $"Снизить урон на одну шкалу ({unit.stats.hpPerSlot} HP)?",
                    resources: $"Стоимость: 1 Выносливость (у вас {unit.stats.currentStamina}).",
                    yes: () =>
                    {
                        unit.stats.SpendStamina(1);
                        ctx.rawDamage = Mathf.Max(0, ctx.rawDamage - unit.stats.hpPerSlot);
                    });
            }
        }

        /// <summary>«Я твой щит» — принять урон союзника на себя.</summary>
        private static void OnAllyIncomingDamage(AllyDamageContext ctx)
        {
            if (ctx == null || ctx.ally == null) return;
            // Ищем защитника: своего юнита с картой, вплотную (близкая дистанция ≤ 2 кл.), с Выносливостью.
            var defender = playerUnits.FirstOrDefault(u =>
                u != ctx.ally
                && !u.IsDead
                && u.character != null
                && PassiveEffectsRegistry.HasCard(u.character, "defense_1_i_am_your_shield")
                && u.stats.currentStamina >= 1
                && CombatManager.ManhattanDistance(u.gridPosition, ctx.ally.gridPosition) <= 2);
            if (defender == null) return;

            AbilityConfirmDialog.Show(
                title: $"«Я твой щит» — {defender.displayName}",
                description: $"Принять на себя удар по {ctx.ally.displayName} ({ctx.rawDamage} сырого урона)? Весь урон уйдёт по вашей броне.",
                resources: $"Стоимость: 1 Выносливость (у {defender.displayName}: {defender.stats.currentStamina}).",
                yes: () =>
                {
                    defender.stats.SpendStamina(1);
                    // Финт: применяем урон защитнику прямо здесь, а атаку по цели «поглотим».
                    // Проще — модифицируем следующий IncomingDamageContext, но у нас другое событие.
                    // Реализация: сразу наносим урон защитнику (весь по броне) и помечаем оригинальную цель как «поглощённую».
                    var absorbedRes = defender.stats.TakeDamage(ctx.rawDamage, bypassArmor: false);
                    ctx.tookOverByPlayerUnit = true;
                    ctx.tookOverBy = defender;
                    Debug.Log($"[Card/Я твой щит] {defender.displayName} принял {absorbedRes.hpDamageDealt} урона вместо {ctx.ally.displayName}.");
                });
        }

        /// <summary>«Подбадривание» — переброс броска союзника (если провал/со Страхом).</summary>
        private static void OnAfterRoll(RollContext ctx)
        {
            if (ctx == null || ctx.source == null) return;
            if (ctx.source.side != CombatSide.Player) return;
            if (!(ctx.outcome.isDuality && (ctx.outcome.FearSide))) return;

            // Ищем любого союзника с картой «Подбадривание», кто ещё не использовал её в этом бою.
            var supporter = playerUnits.FirstOrDefault(u =>
                u.character != null
                && u != ctx.source
                && PassiveEffectsRegistry.HasCard(u.character, "light_1_encourage")
                && !lightEncourageUsed.Contains(u.unitId));

            if (supporter == null) return;

            var self = ctx.source;
            AbilityConfirmDialog.Show(
                title: $"«Подбадривание» от {supporter.displayName}?",
                description: $"{self.displayName} бросил кости со Страхом. Перебросить?",
                resources: "Стоимость: 1 раз за бой.",
                yes: () =>
                {
                    lightEncourageUsed.Add(supporter.unitId);
                    // Перебросить: обновить outcome в контексте (грубо, но эффективно).
                    var reroll = CombatManager.Instance.RollDuality(0);
                    ctx.outcome = new RollOutcome
                    {
                        isDuality = true,
                        hopeDie = reroll.hopeDie,
                        fearDie = reroll.fearDie,
                        // total пересчитываем как разницу к исходному (просто сумма кубов),
                        // бонусы навыка уже были в исходном total — сохраним разницу.
                        total = ctx.outcome.total - (ctx.outcome.hopeDie + ctx.outcome.fearDie)
                              + reroll.hopeDie + reroll.fearDie
                    };
                });
        }
    }
}
