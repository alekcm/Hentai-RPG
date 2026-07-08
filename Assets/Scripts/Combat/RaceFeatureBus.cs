using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPG.Character;
using RPG.UI;

namespace RPG.Combat
{
    /// <summary>
    /// Слушает CombatTriggerBus и предлагает игроку активировать расовые особенности:
    ///  — Тифлинг: «Бесстрашный» (перед броском со Страхом), «Пугающий» (когда враг атакует рядом).
    ///  — Эльф: «Многовековая практика» — активируется вручную из UI (см. кнопку «Раса»).
    ///           «Магически одарённый» — вручную через UI.
    ///  — Человек: «Адаптивность» (при провале — Надежда → +1 Выносливость).
    ///             «Высокая выносливость» — пассивная, обрабатывается CharacterBase.Initialize.
    ///  — Орк: «Стойкий» — пассивная модификация уклонения при 1 шкале.
    ///          «Бивни» — вручную.
    /// </summary>
    public static class RaceFeatureBus
    {
        private static List<CombatUnit> playerUnits;

        public static void Initialize(IEnumerable<CombatUnit> units)
        {
            playerUnits = units.Where(u => u.side == CombatSide.Player && u.character != null).ToList();

            CombatTriggerBus.OnBeforeRoll               += OnBeforeRoll;
            CombatTriggerBus.OnAfterRoll                += OnAfterRoll;
            CombatTriggerBus.OnEnemyAttackAgainstUnit   += OnEnemyAttackAgainst;
            CombatTriggerBus.OnAllyIncomingDamage       += OnAllyIncomingDamage;
        }

        // ---------------- Тифлинг: Бесстрашный ----------------
        // По ГДД: если бросок со Страхом — потратьте 1 Выносливость, чтобы обменять его на Надежду.
        // Мы даём выбор ПОСЛЕ броска (после того как игрок увидел кости).
        private static void OnAfterRoll(RollContext ctx)
        {
            if (ctx?.source == null) return;
            if (ctx.source.side != CombatSide.Player) return;
            if (!ctx.outcome.isDuality || !ctx.outcome.FearSide) return;

            var ch = ctx.source.character;
            if (ch == null || ch.race != RaceType.Tiefling) return;
            if (ctx.source.stats.currentStamina < 1) return;

            AbilityConfirmDialog.Show(
                title: "Тифлинг «Бесстрашный»",
                description: "Заменить бросок со Страхом на бросок с Надеждой (Надежда в пул НЕ начисляется)?",
                resources: $"Стоимость: 1 Выносливость (у вас {ctx.source.stats.currentStamina}).",
                yes: () =>
                {
                    ctx.source.stats.SpendStamina(1);
                    ctx.convertFearToHope = true;
                    ctx.grantHopeOnConversion = false;
                });
        }

        // ---------------- Заглушка BeforeRoll (для будущих карт вроде "Провидение") ----------------
        private static void OnBeforeRoll(RollContext ctx) { }

        // ---------------- Тифлинг: Пугающий ----------------
        // По ГДД: потратьте Надежду, чтобы дать помеху на атаку, совершаемую в 2 клетках от вас.
        private static void OnEnemyAttackAgainst(AttackContext ctx)
        {
            if (ctx?.attacker == null) return;
            var cm = CombatManager.Instance;
            if (cm == null || cm.HopePool < 1) return;

            // Ищем тифлинга-игрока в 2 клетках от атакующего врага.
            var tiefling = playerUnits.FirstOrDefault(u =>
                u.character != null && u.character.race == RaceType.Tiefling
                && CombatManager.ManhattanDistance(u.gridPosition, ctx.attacker.gridPosition) <= 2);

            if (tiefling == null) return;

            AbilityConfirmDialog.Show(
                title: "Тифлинг «Пугающий»",
                description: $"Дать помеху атаке {ctx.attacker.displayName} по {ctx.target.displayName}?",
                resources: $"Стоимость: 1 Надежда (у нас {cm.HopePool}).",
                yes: () =>
                {
                    if (cm.TrySpendHope(1))
                    {
                        // Мутируем следующий Roll врага. Проще всего — установить помеху через RollContext на этот раунд.
                        // В текущем CombatManager враги кидают d20 внутри PerformAttack, но атака уже "решается".
                        // Реалистичнее — прикрепить эффект на атакующего до конца его следующего действия.
                        // Здесь как минимум логируем эффект (полноценная реализация — в следующей итерации, когда
                        // враги начнут кидать через отдельный метод с преимуществом/помехой).
                        Debug.Log($"[Race/Пугающий] {tiefling.displayName} даёт помеху {ctx.attacker.displayName}.");
                    }
                });
        }

        // ---------------- Заглушка для «Я твой щит» / для будущего «Заступничества» ----------------
        private static void OnAllyIncomingDamage(AllyDamageContext ctx) { }

        // ---------------- Человек: Адаптивность ----------------
        /// <summary>
        /// «Провалив бросок, потратьте 1 Надежду, чтобы восстановить 1 Выносливость».
        /// Вызывается из внешнего кода после того, как бросок объявлен провалом (например, из PerformAttack).
        /// Здесь оставлен как публичный API — интегрируется в CombatManager позже (когда добавится общий "провал броска").
        /// </summary>
        public static void OfferHumanAdaptability(CombatUnit unit)
        {
            if (unit == null || unit.character == null) return;
            if (unit.character.race != RaceType.Human) return;
            var cm = CombatManager.Instance;
            if (cm == null || cm.HopePool < 1) return;
            if (unit.stats.currentStamina >= unit.stats.maxStamina) return;

            AbilityConfirmDialog.Show(
                title: "Человек «Адаптивность»",
                description: "Вы провалили бросок. Потратить 1 Надежду, чтобы восстановить 1 Выносливость?",
                resources: $"Стоимость: 1 Надежда (у нас {cm.HopePool}).",
                yes: () =>
                {
                    if (cm.TrySpendHope(1)) unit.stats.RestoreStamina(1);
                });
        }
    }
}
