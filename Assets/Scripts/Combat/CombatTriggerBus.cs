using System;
using System.Collections.Generic;
using RPG.Character;

namespace RPG.Combat
{
    /// <summary>
    /// Общая шина боевых триггеров. К ней подписываются:
    ///  — исполнитель карт доменов (Reaction-карты),
    ///  — расовые особенности (Бесстрашный, Пугающий, Адаптивность и т.п.),
    ///  — классовые фичи (Возмездие Карателя и т.п.),
    ///  — спец-действия врагов (Импульс).
    ///
    /// Все подписчики получают событие ДО того, как эффект вступает в силу (кроме OnDamageDealt),
    /// и могут показать модальное окно `AbilityConfirmDialog` игроку.
    /// Если игрок соглашается — событие мутирует контекст (например, снижает урон, меняет тип броска).
    /// </summary>
    public static class CombatTriggerBus
    {
        // --- Броски ---
        public static event Action<RollContext> OnBeforeRoll;
        public static event Action<RollContext> OnAfterRoll;

        // --- Урон ---
        public static event Action<IncomingDamageContext> OnIncomingDamage;      // до применения урона
        public static event Action<DamageDealtContext> OnDamageDealt;            // после применения
        public static event Action<AllyDamageContext> OnAllyIncomingDamage;     // союзник получает урон

        // --- Атаки ---
        public static event Action<AttackContext> OnEnemyAttackAgainstUnit;      // враг атакует юнита (используется для Тифлинг/Пугающий)
        public static event Action<AttackContext> OnAttackResolved;             // после расчёта атаки

        // --- Ходы ---
        public static event Action<CombatUnit> OnUnitActivated;
        public static event Action<CombatSide> OnRoundStartForSide;
        public static event Action OnCombatStarted;
        public static event Action<bool> OnCombatEnded;

        // ----------------- API диспетчеризации -----------------
        public static void RaiseBeforeRoll(RollContext ctx) => OnBeforeRoll?.Invoke(ctx);
        public static void RaiseAfterRoll(RollContext ctx) => OnAfterRoll?.Invoke(ctx);
        public static void RaiseIncomingDamage(IncomingDamageContext ctx) => OnIncomingDamage?.Invoke(ctx);
        public static void RaiseDamageDealt(DamageDealtContext ctx) => OnDamageDealt?.Invoke(ctx);
        public static void RaiseAllyIncomingDamage(AllyDamageContext ctx) => OnAllyIncomingDamage?.Invoke(ctx);
        public static void RaiseEnemyAttackAgainstUnit(AttackContext ctx) => OnEnemyAttackAgainstUnit?.Invoke(ctx);
        public static void RaiseAttackResolved(AttackContext ctx) => OnAttackResolved?.Invoke(ctx);
        public static void RaiseUnitActivated(CombatUnit u) => OnUnitActivated?.Invoke(u);
        public static void RaiseRoundStartForSide(CombatSide s) => OnRoundStartForSide?.Invoke(s);
        public static void RaiseCombatStarted() => OnCombatStarted?.Invoke();
        public static void RaiseCombatEnded(bool won) => OnCombatEnded?.Invoke(won);

        public static void ResetAllSubscriptions()
        {
            OnBeforeRoll = null;
            OnAfterRoll = null;
            OnIncomingDamage = null;
            OnDamageDealt = null;
            OnAllyIncomingDamage = null;
            OnEnemyAttackAgainstUnit = null;
            OnAttackResolved = null;
            OnUnitActivated = null;
            OnRoundStartForSide = null;
            OnCombatStarted = null;
            OnCombatEnded = null;
        }
    }

    // ================================================================
    //   Контексты событий
    // ================================================================

    /// <summary>Что кидаем и почему. Позволяет подписчику модифицировать бросок ДО и оценивать ПОСЛЕ.</summary>
    public class RollContext
    {
        public CombatUnit source;
        public RollKind kind;                     // атака / заклинание / короткая передышка / прочее
        public SkillType? skill;                  // если это скилл-бросок
        public bool advantage;
        public bool disadvantage;
        public bool convertFearToHope;            // Тифлинг/Бесстрашный: подмена результата
        public bool grantHopeOnConversion;        // если Тифлинг — false (Надежда не начисляется по ГДД)
        public RollOutcome outcome;               // заполняется до OnAfterRoll
    }

    public enum RollKind { Attack, Spellcast, ShortRest, Reaction, Other }

    public class IncomingDamageContext
    {
        public CombatUnit target;
        public CombatUnit attacker;                // может быть null (окружение)
        public int rawDamage;                     // модифицируемый! (Вернуться в строй — минус шкала)
        public bool bypassArmor;
        public bool absorbedByAlly;               // «Я твой щит»
        public CombatUnit absorbingAlly;          // кто взял на себя
        public bool cancelled;                    // атака ушла в ноль (Увёртливость, "промазала после броска")
    }

    public class DamageDealtContext
    {
        public CombatUnit attacker;
        public CombatUnit target;
        public DamageResult result;
        public bool wasSeriousDamage;             // сломана хотя бы одна шкала здоровья
    }

    public class AllyDamageContext
    {
        public CombatUnit ally;                   // союзник, который вот-вот получит урон
        public CombatUnit attacker;
        public int rawDamage;
        public bool tookOverByPlayerUnit;         // «Я твой щит» перехватил
        public CombatUnit tookOverBy;
    }

    public class AttackContext
    {
        public CombatUnit attacker;
        public CombatUnit target;
        public AttackResult result;               // заполняется CombatManager после расчёта
    }
}
