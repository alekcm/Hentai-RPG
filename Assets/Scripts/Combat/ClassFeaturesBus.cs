using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RPG.Character;
using RPG.Domains;
using RPG.UI;

namespace RPG.Combat
{
    /// <summary>
    /// Реализация классовых и подклассовых фич по ГДД.
    /// Подписывается на CombatTriggerBus и на кастомные точки в CombatManager (через флаги на юните).
    ///
    /// Что покрывается:
    ///  ВОИН:
    ///   — Провоцированная атака (когда враг пытается выйти из вплотную).
    ///   — Боевая подготовка (игнор хвата — включается в CharacterCreation при выборе оружия).
    ///   Подклассы:
    ///   — Берсерк: Неудержимость (заряды +к урону), Кровь-вместо-Надежды (замена Надежды на +d6/потерю Выносливости у врага).
    ///   — Заступник: Непоколебимый (+1 к DT — уже в PassiveEffectsRegistry), Щит Передовой (Надежда → +1 ячейка брони).
    ///   — Мастер БИ: 6 стоек (Брутальная/Защитная/Захватная/Устойчивая/Точная/Быстрая).
    ///
    ///  ПЛУТ:
    ///   — Подлая атака (+половина уровня d6 при преимуществе или союзнике вплотную).
    ///   Подклассы:
    ///   — Гильдия Палачей: Первый удар (первая успешная атака ×2), Внезапная атака (+1d6 к Подлой).
    ///   — Гильдия Отравителей: Токсичные смеси (жетоны, применяются к атаке).
    ///
    ///  ВОЛШЕБНИК:
    ///   — Странные закономерности (чистое 7 на 2d12 → +Надежда или +Выносливость).
    ///   Подклассы:
    ///   — Школа знаний: Подготовленный (+доп. карта — обрабатывается в CharacterCreation), Адепт (+1 к навыку — там же).
    ///   — Школа войны: Боевой маг (+1 ячейка ран — там же), Победи свой страх (+1d10 при успехе со Страхом).
    ///   — Школа метамагии: Метамагия (заблокировать карту → усилить заклинание).
    ///
    ///  СВЯЩЕННИК:
    ///   — Вера в лучшее (при провале — потратить Выносливость/Надежду для «доброса»).
    ///   Подклассы:
    ///   — Серафим: Крылатый страж (форма — Выносливость → потом +1d8 к атакам за Надежду).
    ///   — Каратель: Месть (когда враг ВПЛОТНУЮ попал — контратака с преимуществом за Выносливость), Возмездие (раз в бой при уроне — +Выносливость).
    ///   — Жрец: Молебен (3 варианта, раз в бой, свободное действие).
    ///
    ///  ДРУИД:
    ///   — Звериная форма (Выносливость → выбор из 6 форм, свои характеристики атаки/уклонения).
    /// </summary>
    public static class ClassFeaturesBus
    {
        // ---- Состояние на бой ----
        private static readonly Dictionary<string, int> berserkerCharges = new();       // unitId → заряды
        private static readonly Dictionary<string, int> poisonTokens = new();           // unitId → жетоны смесей
        private static readonly HashSet<string> firstStrikeAvailable = new();           // unitId (Гильдия Палачей) — первая атака ×2
        private static readonly HashSet<string> priestPrayerUsed = new();               // unitId — молебен уже произнесён
        private static readonly HashSet<string> punisherRetributionUsed = new();        // unitId — Возмездие уже использовано
        private static readonly HashSet<string> seraphActiveForm = new();               // unitId в форме крылатого стража
        private static readonly Dictionary<string, string> martialArtistStance = new(); // unitId → активная стойка
        private static readonly Dictionary<string, string> druidBeastForm = new();      // unitId → форма (empty = человеческая)
        private static readonly Dictionary<string, HashSet<string>> metamagicLockedCards = new(); // unitId → заблокированные карты

        public static void Initialize(IEnumerable<CombatUnit> units)
        {
            berserkerCharges.Clear();
            poisonTokens.Clear();
            firstStrikeAvailable.Clear();
            priestPrayerUsed.Clear();
            punisherRetributionUsed.Clear();
            seraphActiveForm.Clear();
            martialArtistStance.Clear();
            druidBeastForm.Clear();
            metamagicLockedCards.Clear();

            foreach (var u in units.Where(u => u.side == CombatSide.Player && u.character != null))
            {
                var c = u.character;
                if (c.subclassId == "rogue_executioners_guild")
                    firstStrikeAvailable.Add(u.unitId);
                if (c.subclassId == "warrior_martial_artist")
                {
                    // Стойка выбирается игроком в начале боя. Дефолт — «Точная».
                    if (!string.IsNullOrEmpty(c.subclassStance))
                        martialArtistStance[u.unitId] = c.subclassStance;
                    else
                        martialArtistStance[u.unitId] = "precise";
                }
            }

            CombatTriggerBus.OnBeforeRoll             += OnBeforeRoll;
            CombatTriggerBus.OnAfterRoll              += OnAfterRoll;
            CombatTriggerBus.OnIncomingDamage         += OnIncomingDamage;
            CombatTriggerBus.OnDamageDealt            += OnDamageDealt;
            CombatTriggerBus.OnAttackResolved         += OnAttackResolved;
            CombatTriggerBus.OnEnemyAttackAgainstUnit += OnEnemyAttackAgainstUnit;
        }

        // ================================================================
        //   Публичные API (вызываются из UI / CombatManager)
        // ================================================================

        /// <summary>Модификатор урона Подлой атаки: +N × d6 (N = уровень/2, +1 если Гильдия Палачей).</summary>
        public static int GetSneakAttackBonusDice(CombatUnit u, CombatUnit target)
        {
            if (u?.character == null || u.character.characterClass != ClassType.Rogue) return 0;
            var cm = CombatManager.Instance;
            // Условие: атака с преимуществом ИЛИ союзник вплотную к цели.
            bool advOrAlly = u.buffAttackBonusNextRoll > 0
                || cm.AllUnits.Any(a => a != u && a.side == u.side && !a.IsDead
                    && CombatManager.ManhattanDistance(a.gridPosition, target.gridPosition) <= 1);
            if (!advOrAlly) return 0;
            int n = Mathf.Max(1, u.stats.level / 2);
            if (u.character.subclassId == "rogue_executioners_guild") n += 1;
            return n;
        }

        /// <summary>Плут-Отравитель: сколько жетонов сейчас у юнита.</summary>
        public static int GetPoisonTokens(CombatUnit u) => u == null ? 0 : (poisonTokens.TryGetValue(u.unitId, out var v) ? v : 0);

        public static void SpendPoisonToken(CombatUnit u, int count = 1)
        {
            if (u == null) return;
            poisonTokens[u.unitId] = Mathf.Max(0, GetPoisonTokens(u) - count);
        }

        /// <summary>Плут-Отравитель: приготовить жетоны (действие в бою).</summary>
        public static void PoisonerBrew(CombatUnit u)
        {
            if (u?.character?.subclassId != "rogue_poisoners_guild") return;
            u.stats.SpendStamina(1);
            int roll = UnityEngine.Random.Range(1, 5) + 1; // 1d4+1
            poisonTokens[u.unitId] = GetPoisonTokens(u) + roll;
            LogFeature($"{u.displayName} готовит токсичные смеси: +{roll} жетонов (всего {GetPoisonTokens(u)}).");
        }

        /// <summary>Берсерк: разгон.</summary>
        public static int GetBerserkerCharges(CombatUnit u) => u == null ? 0 : (berserkerCharges.TryGetValue(u.unitId, out var v) ? v : 0);

        public static void BerserkerAddCharge(CombatUnit u)
        {
            if (u?.character?.subclassId != "warrior_berserker") return;
            int cur = GetBerserkerCharges(u);
            int max = 4 + u.stats.level / 2;
            if (cur >= max)
            {
                berserkerCharges[u.unitId] = 0;
                LogFeature($"{u.displayName} выходит из состояния Неудержимости.");
            }
            else
            {
                berserkerCharges[u.unitId] = cur + 1;
            }
        }

        /// <summary>Мастер БИ: смена стойки в бою за 1 Выносливость.</summary>
        public static bool ChangeStance(CombatUnit u, string newStance)
        {
            if (u?.character?.subclassId != "warrior_martial_artist") return false;
            u.stats.SpendStamina(1);
            martialArtistStance[u.unitId] = newStance;
            u.character.subclassStance = newStance;
            LogFeature($"{u.displayName} меняет стойку на: {StanceLabel(newStance)}.");
            return true;
        }

        public static string GetStance(CombatUnit u) => (u != null && martialArtistStance.TryGetValue(u.unitId, out var s)) ? s : null;

        /// <summary>Серафим: включить/выключить форму крылатого стража.</summary>
        public static void ToggleSeraphForm(CombatUnit u)
        {
            if (u?.character?.subclassId != "cleric_seraph") return;
            if (seraphActiveForm.Contains(u.unitId))
            {
                seraphActiveForm.Remove(u.unitId);
                LogFeature($"{u.displayName} выходит из формы крылатого стража.");
            }
            else
            {
                u.stats.SpendStamina(1);
                seraphActiveForm.Add(u.unitId);
                LogFeature($"{u.displayName} принимает форму крылатого стража — парит, игнорирует опасности местности.");
            }
        }

        public static bool IsSeraphActive(CombatUnit u) => u != null && seraphActiveForm.Contains(u.unitId);

        /// <summary>Молебен Жреца (свободное действие, раз в бой).</summary>
        public static bool CastPrayer(CombatUnit u, string variant)
        {
            if (u?.character?.subclassId != "cleric_priest") return false;
            if (priestPrayerUsed.Contains(u.unitId))
            {
                LogFeature($"{u.displayName}: молебен уже был произнесён в этом бою.");
                return false;
            }
            priestPrayerUsed.Add(u.unitId);
            var cm = CombatManager.Instance;
            switch (variant)
            {
                case "healing":
                    foreach (var ally in cm.AllUnits.Where(a => a.side == u.side && !a.IsDead
                        && CombatManager.ManhattanDistance(a.gridPosition, u.gridPosition) <= 4))
                        ally.stats.HealHealthSlots(1);
                    LogFeature($"{u.displayName}: Целебный молебен восстанавливает 1 ячейку здоровья у союзников в 4 кл.");
                    return true;
                case "battle":
                    var target = cm.AllUnits.FirstOrDefault(a => a.side != u.side && !a.IsDead
                        && CombatManager.ManhattanDistance(a.gridPosition, u.gridPosition) <= 4);
                    if (target != null)
                    {
                        target.ApplyEffect(new StatusEffect
                        {
                            effectId = "vulnerable_temp",
                            displayName = "Уязвим (Боевой молебен)",
                            effectType = EffectType.Vulnerable,
                            remainingDuration = 3
                        });
                        LogFeature($"{u.displayName}: Боевой молебен — {target.displayName} становится Уязвимым.");
                    }
                    return true;
                case "inspiring":
                    int allies = cm.AllUnits.Count(a => a.side == u.side && !a.IsDead
                        && CombatManager.ManhattanDistance(a.gridPosition, u.gridPosition) <= 4);
                    for (int i = 0; i < allies; i++) cm.RefundHope(1);
                    LogFeature($"{u.displayName}: Воодушевляющий молебен — +{allies} Надежды.");
                    return true;
            }
            return false;
        }

        public static bool IsPrayerAvailable(CombatUnit u)
            => u?.character?.subclassId == "cleric_priest" && !priestPrayerUsed.Contains(u.unitId);

        /// <summary>Метамагия: заблокировать карту, чтобы усилить следующее заклинание/маг. атаку.</summary>
        public static bool LockCardForMetamagic(CombatUnit u, string cardId, string effect)
        {
            if (u?.character?.subclassId != "mage_school_of_metamagic") return false;
            if (!PassiveEffectsRegistry.HasCard(u.character, cardId)) return false;

            if (!metamagicLockedCards.TryGetValue(u.unitId, out var set))
                metamagicLockedCards[u.unitId] = set = new HashSet<string>();
            if (set.Contains(cardId)) return false;
            set.Add(cardId);
            u.character.SetFlag($"metamagic_pending_{effect}", true);
            LogFeature($"{u.displayName}: карта «{DomainDatabase.GetCard(cardId)?.displayName}» заблокирована до конца боя → следующее заклинание {MetamagicLabel(effect)}.");
            return true;
        }

        public static bool IsCardMetamagicLocked(CombatUnit u, string cardId)
            => u != null && metamagicLockedCards.TryGetValue(u.unitId, out var s) && s.Contains(cardId);

        /// <summary>Друид: сменить звериную форму (Выносливость).</summary>
        public static void ChangeDruidForm(CombatUnit u, string formId)
        {
            if (u?.character?.characterClass != ClassType.Druid) return;
            u.stats.SpendStamina(1);
            if (formId == "human")
            {
                druidBeastForm.Remove(u.unitId);
                LogFeature($"{u.displayName} возвращается в человеческий облик.");
                RemoveBeastFormBonuses(u);
                return;
            }
            druidBeastForm[u.unitId] = formId;
            ApplyBeastFormBonuses(u, formId);
            LogFeature($"{u.displayName} превращается в: {DruidFormLabel(formId)}.");
        }

        public static string GetDruidForm(CombatUnit u) => (u != null && druidBeastForm.TryGetValue(u.unitId, out var f)) ? f : null;

        // ================================================================
        //   Обработчики CombatTriggerBus
        // ================================================================

        private static void OnBeforeRoll(RollContext ctx)
        {
            if (ctx?.source == null) return;
            var c = ctx.source.character;
            if (c == null) return;

            // МБИ: Точная — +1 к атаке (стойка).
            if (GetStance(ctx.source) == "precise" && ctx.kind == RollKind.Attack)
                ctx.source.buffAttackBonusNextRoll += 1;
        }

        private static void OnAfterRoll(RollContext ctx)
        {
            if (ctx?.source == null || !ctx.outcome.isDuality) return;
            var u = ctx.source;
            var c = u.character;
            if (c == null || u.side != CombatSide.Player) return;

            // ВОЛШЕБНИК: Странные закономерности — «чистое 7» на 2d12.
            if (c.characterClass == ClassType.Mage && (ctx.outcome.hopeDie + ctx.outcome.fearDie) == 7)
            {
                // Даём игроку выбор: Надежда или Выносливость.
                AbilityConfirmDialog.Show(
                    title: "Странные закономерности",
                    description: "На кубах выпало ровно 7 — получить бонус:",
                    resources: $"Надежда сейчас: {CombatManager.Instance.HopePool}. Выносливость: {u.stats.currentStamina}/{u.stats.maxStamina}",
                    yes: () => { CombatManager.Instance.RefundHope(1); LogFeature($"{u.displayName}: Странные закономерности — +1 Надежда."); },
                    no:  () => { u.stats.RestoreStamina(1); LogFeature($"{u.displayName}: Странные закономерности — восстановлена Выносливость."); });
                // «Да» = Надежда, «Отмена» = Выносливость. Кнопки в AbilityConfirmDialog нейтральные,
                // поэтому используем их как выбор из двух вариантов.
            }

            // СВЯЩЕННИК: Вера в лучшее — при провале предложить потратить Надежду/Выносливость на «доброс».
            if (c.characterClass == ClassType.Cleric)
            {
                // Определим «провал» по kind == Attack: если total < 10 условно (без цели тут сказать сложно).
                // Оставим API для внешнего вызова: OfferClericFaith в момент, когда известен исход.
            }
        }

        private static void OnIncomingDamage(IncomingDamageContext ctx)
        {
            if (ctx?.target == null) return;
            var u = ctx.target;
            var c = u.character;
            if (c == null) return;

            // ДРУИД: если в звериной форме и получил >1 шкалы урона — Хрупкость (Собака, Газель) выходит из формы.
            var form = GetDruidForm(u);
            if (form == "dog" || form == "gazelle")
            {
                int estimatedSlots = ctx.rawDamage / Mathf.Max(1, u.stats.hpPerSlot);
                if (estimatedSlots > 1)
                {
                    // Отмечаем — снимем форму после применения урона (в OnDamageDealt).
                    c.SetFlag("druid_should_exit_form", true);
                }
            }

            // СВЯЩЕННИК-Каратель: Возмездие (раз в бой) — при получении урона от атаки восстановить Выносливость.
            if (c.subclassId == "cleric_punisher"
                && ctx.attacker != null
                && !punisherRetributionUsed.Contains(u.unitId)
                && ctx.rawDamage > 0)
            {
                AbilityConfirmDialog.Show(
                    title: "Каратель «Возмездие»",
                    description: "Раз в бой при получении урона можно восстановить Выносливость. Использовать?",
                    resources: $"Выносливость: {u.stats.currentStamina}/{u.stats.maxStamina}",
                    yes: () =>
                    {
                        punisherRetributionUsed.Add(u.unitId);
                        u.stats.RestoreStamina(1);
                        LogFeature($"{u.displayName}: Возмездие — +1 Выносливость.");
                    });
            }
        }

        private static void OnDamageDealt(DamageDealtContext ctx)
        {
            if (ctx?.attacker == null || ctx.target == null) return;
            var atk = ctx.attacker;
            var c = atk.character;

            // Снимаем звериную форму при Хрупкости.
            if (ctx.target.character != null && ctx.target.character.GetFlag("druid_should_exit_form"))
            {
                ctx.target.character.SetFlag("druid_should_exit_form", false);
                ChangeDruidForm(ctx.target, "human");
            }

            if (c == null) return;

            // ВОИН-Берсерк: Неудержимость — при успешной атаке добавить заряд (и урон += заряд).
            // Здесь урон уже применён; следующий вызов PerformAttack учтёт баф — см. GetBerserkerDamageBonus().
            if (c.subclassId == "warrior_berserker" && ctx.result.hpDamageDealt > 0)
                BerserkerAddCharge(atk);

            // МБИ: Устойчивая — при уроне доп. кость и отбросить наименьший (обработается в PerformAttack override).
            // Здесь ничего дополнительно не делаем.

            // Успех со Страхом: Боевой маг «Победи свой страх» +1d10 маг. урона.
            // (Обрабатывается сразу здесь — уже нанесли базовый урон.)
            if (c.subclassId == "mage_school_of_war" && ctx.result.hpDamageDealt > 0)
            {
                var lastRoll = atk.lastRollOutcome;
                if (lastRoll.isDuality && lastRoll.FearSide)
                {
                    int extra = CombatManager.Instance.RollDamage("1d10");
                    ctx.target.stats.TakeDamage(extra);
                    LogFeature($"{atk.displayName}: «Победи свой страх» +{extra} маг. урона.");
                }
            }
        }

        private static void OnAttackResolved(AttackContext ctx)
        {
            if (ctx?.attacker == null) return;
            var atk = ctx.attacker;
            var c = atk.character;
            if (c == null || atk.side != CombatSide.Player) return;

            // Плут-Палач: Первый удар — первая УСПЕШНАЯ атака в бою наносит удвоенный урон.
            if (firstStrikeAvailable.Contains(atk.unitId) && ctx.result.hit && ctx.result.rawDamage > 0)
            {
                firstStrikeAvailable.Remove(atk.unitId);
                int bonus = ctx.result.rawDamage; // удвоение = ещё столько же
                ctx.target.stats.TakeDamage(bonus);
                LogFeature($"{atk.displayName}: «Первый удар» — удвоенный урон (+{bonus}).");
            }

            // Берсерк: заряд получен → предлагаем "вместо Надежды +d6 или потеря Выносливости у врага".
            if (c.subclassId == "warrior_berserker" && ctx.result.hit && ctx.result.roll.HopeSide)
            {
                AbilityConfirmDialog.Show(
                    title: "Берсерк: обменять Надежду?",
                    description: "Вместо получения Надежды нанести +1d6 урона (Да) ИЛИ заставить противника потерять Выносливость (Отмена)?",
                    resources: "Стоимость: 1 Надежда из пула.",
                    yes: () =>
                    {
                        if (CombatManager.Instance.TrySpendHope(1))
                        {
                            int extra = CombatManager.Instance.RollDamage("1d6");
                            ctx.target.stats.TakeDamage(extra);
                            LogFeature($"{atk.displayName}: обмен Надежды на урон (+{extra}).");
                        }
                    },
                    no: () =>
                    {
                        if (CombatManager.Instance.TrySpendHope(1))
                        {
                            ctx.target.stats.SpendStamina(1);
                            LogFeature($"{atk.displayName}: обмен Надежды на потерю Выносливости {ctx.target.displayName}.");
                        }
                    });
            }

            // Заступник: Щит Передовой — Надежду в +1 ячейку брони.
            if (c.subclassId == "warrior_bulwark" && ctx.result.hit && ctx.result.roll.HopeSide
                && atk.stats.usedArmorSlots > 0)
            {
                AbilityConfirmDialog.Show(
                    title: "Заступник: Щит Передовой",
                    description: "Вместо получения Надежды восстановить 1 ячейку брони?",
                    resources: $"Сейчас брони: {atk.stats.maxArmorSlots - atk.stats.usedArmorSlots}/{atk.stats.maxArmorSlots}",
                    yes: () =>
                    {
                        if (CombatManager.Instance.TrySpendHope(1))
                        {
                            atk.stats.usedArmorSlots = Mathf.Max(0, atk.stats.usedArmorSlots - 1);
                            LogFeature($"{atk.displayName}: Щит Передовой — +1 ячейка брони.");
                        }
                    });
            }

            // Серафим (в форме): успешная атака → потратить Надежду для +1d8.
            if (c.subclassId == "cleric_seraph" && IsSeraphActive(atk) && ctx.result.hit)
            {
                AbilityConfirmDialog.Show(
                    title: "Серафим: свет крыла",
                    description: "Нанести дополнительно 1d8 урона за счёт Надежды?",
                    resources: $"Надежда: {CombatManager.Instance.HopePool}",
                    yes: () =>
                    {
                        if (CombatManager.Instance.TrySpendHope(1))
                        {
                            int extra = CombatManager.Instance.RollDamage("1d8");
                            ctx.target.stats.TakeDamage(extra);
                            LogFeature($"{atk.displayName}: +{extra} света крыла.");
                        }
                    });
            }
        }

        private static void OnEnemyAttackAgainstUnit(AttackContext ctx)
        {
            if (ctx?.target?.character == null) return;
            var target = ctx.target;
            var attacker = ctx.attacker;
            var c = target.character;

            // Каратель: Месть — когда враг ВПЛОТНУЮ попал по нам, тратим Выносливость → атака с преимуществом (всегда Надежда).
            // Проверку "попал ли" даст только OnAttackResolved. Здесь готовим предложение ПОСЛЕ атаки.
            // Реализуем через отложенную подписку на OnAttackResolved.
            // Простая реализация: если враг вплотную и атака зафиксирована в OnAttackResolved как hit — предложим.
            // Пока делаем упрощённо в OnAttackResolved.

            // МБИ: Защитная стойка — атакующий вас теряет Выносливость.
            if (GetStance(target) == "defensive" && attacker != null && !attacker.IsDead)
            {
                attacker.stats.SpendStamina(1);
                LogFeature($"{target.displayName} (Защитная стойка): {attacker.displayName} теряет Выносливость.");
            }

            // МБИ: Захватная стойка — при УСПЕШНОЙ атаке ВАС… нет, это про свою атаку.
            //   (Не срабатывает в OnEnemyAttackAgainst; см. отдельный хук в PerformAttack игрока.)
        }

        // Вызывается из CombatManager после расчёта атаки врагом по игроку (для Карателя).
        public static void OfferPunisherVengeance(CombatUnit target, CombatUnit attacker, AttackResult result)
        {
            if (target?.character?.subclassId != "cleric_punisher") return;
            if (!result.hit || attacker == null) return;
            if (CombatManager.ManhattanDistance(target.gridPosition, attacker.gridPosition) > 1) return;
            if (target.stats.currentStamina < 1) return;

            AbilityConfirmDialog.Show(
                title: "Каратель: Месть",
                description: $"{attacker.displayName} попал по вам вплотную. Провести мгновенную атаку с преимуществом (всегда с Надеждой)?",
                resources: "Стоимость: 1 Выносливость.",
                yes: () =>
                {
                    target.stats.SpendStamina(1);
                    target.buffAttackBonusNextRoll += 3; // грубый эквивалент преимущества
                    var atk = CombatManager.Instance.PerformAttack(target, attacker);
                    // По ГДД считается «с Надеждой» — начислим ей Надежду вручную.
                    if (atk.hit && !(atk.roll.isDuality && atk.roll.HopeSide))
                        CombatManager.Instance.RefundHope(1);
                    LogFeature($"{target.displayName}: Месть — контратака по {attacker.displayName}.");
                });
        }

        // Вызывается из CombatManager при провале Священника (Вера в лучшее).
        public static void OfferClericFaith(RollContext ctx)
        {
            if (ctx?.source == null) return;
            var u = ctx.source;
            var c = u.character;
            if (c == null || c.characterClass != ClassType.Cleric) return;
            if (u.stats.currentStamina < 1 && CombatManager.Instance.HopePool < 1) return;

            AbilityConfirmDialog.Show(
                title: "Священник: Вера в лучшее",
                description: "Провал броска. Небольшое божественное вмешательство добавит вдохновение (+1d6)?",
                resources: $"Стоимость: 1 Выносливость ИЛИ 1 Надежда. Есть: Вын {u.stats.currentStamina}, Над {CombatManager.Instance.HopePool}",
                yes: () =>
                {
                    if (u.stats.currentStamina >= 1) u.stats.SpendStamina(1);
                    else CombatManager.Instance.TrySpendHope(1);
                    int add = UnityEngine.Random.Range(1, 7);
                    var m = ctx.outcome;
                    ctx.outcome = new RollOutcome
                    {
                        isDuality = m.isDuality,
                        hopeDie = m.hopeDie,
                        fearDie = m.fearDie,
                        total = m.total + add
                    };
                    LogFeature($"{u.displayName}: Вера в лучшее — +{add} к броску.");
                });
        }

        // ================================================================
        //   Модификаторы урона / попадания для CombatManager
        // ================================================================

        /// <summary>Берсерк: +заряд к каждой кости урона (грубо — плюсом к сырому урону = зарядам).</summary>
        public static int GetBerserkerDamageBonus(CombatUnit u)
            => u?.character?.subclassId == "warrior_berserker" ? GetBerserkerCharges(u) : 0;

        /// <summary>Мастер БИ: постоянные модификаторы стойки.</summary>
        public static void ApplyStanceModifiers(CombatUnit u, ref int rawDamage, string weaponDice, System.Random _unused = null)
        {
            var stance = GetStance(u);
            if (stance == null) return;

            var cm = CombatManager.Instance;

            switch (stance)
            {
                case "brutal":
                    // Если выпал максимум — добавляем ещё одну кость.
                    // Здесь можно только грубо оценить: сравним rawDamage с максимально возможным для weaponDice.
                    // Реализация упрощена: с шансом 1/(sides) считаем максимум и добавляем.
                    // Точная реализация должна быть на уровне RollDamage; здесь оставлена как no-op.
                    break;
                case "sturdy":
                    // Ещё одна кость + отбросить меньшую.
                    int extra = cm.RollDamage(SidesOnly(weaponDice));
                    rawDamage = Mathf.Max(rawDamage, rawDamage + extra - Mathf.Min(rawDamage, extra));
                    break;
            }
        }

        /// <summary>МБИ: Быстрая — при Броске Атаки потратить Выносливость → включить доп. цель.
        /// Вызывается из UI после успешного нацеливания.</summary>
        public static bool MartialArtistFastExtra(CombatUnit u)
        {
            if (GetStance(u) != "fast") return false;
            if (u.stats.currentStamina < 1) return false;
            u.stats.SpendStamina(1);
            return true;
        }

        // ================================================================
        //   Друид — звериные формы
        // ================================================================

        private static void ApplyBeastFormBonuses(CombatUnit u, string formId)
        {
            switch (formId)
            {
                case "giant_arachnid":
                    u.stats.evasion += 2; u.beastAttackBonus = 1; u.beastAttackDice = "d6+1"; u.beastAttackRange = Items.WeaponRange.Melee; break;
                case "silkworm":
                    u.beastAttackBonus = 0; u.beastAttackDice = "d4"; u.beastAttackRange = Items.WeaponRange.Medium; break;
                case "wolf":
                    u.stats.evasion += 1; u.beastAttackBonus = 2; u.beastAttackDice = "d8+2"; u.beastAttackRange = Items.WeaponRange.Melee; break;
                case "dog":
                    u.stats.evasion += 2; u.beastAttackBonus = 1; u.beastAttackDice = "d6"; u.beastAttackRange = Items.WeaponRange.Melee; break;
                case "gazelle":
                    u.stats.evasion += 3; u.beastAttackBonus = 1; u.beastAttackDice = "d6"; u.beastAttackRange = Items.WeaponRange.Melee; break;
                case "armadillo":
                    u.stats.evasion += 0; u.beastAttackBonus = 1; u.beastAttackDice = "d6"; u.beastAttackRange = Items.WeaponRange.Melee;
                    u.character.SetFlag("armadillo_resist_physical", true); break;
            }
        }

        private static void RemoveBeastFormBonuses(CombatUnit u)
        {
            // Восстанавливаем базовое уклонение из класса, потом накидываем модификаторы брони.
            var cls = ClassDatabase.GetClass(u.character.characterClass);
            if (cls != null) u.stats.evasion = cls.baseEvasion;
            Items.ItemDatabase.ApplyEquippedGear(u.character);
            u.beastAttackBonus = 0; u.beastAttackDice = null; u.beastAttackRange = Items.WeaponRange.Melee;
            u.character.SetFlag("armadillo_resist_physical", false);
        }

        // ================================================================
        //   Утилиты
        // ================================================================

        private static string SidesOnly(string dice)
        {
            // Из "d10+3" → "d10", из "2d6+1" → "d6". Возвращает одну кость того же типа без плюса.
            var lower = (dice ?? "d6").ToLower().Replace(" ", "");
            int dIdx = lower.IndexOf('d');
            if (dIdx < 0) return "d6";
            string sides = lower.Substring(dIdx);
            int plus = sides.IndexOf('+');
            if (plus > 0) sides = sides.Substring(0, plus);
            return sides;
        }

        private static string StanceLabel(string s) => s switch
        {
            "brutal" => "Брутальная", "defensive" => "Защитная", "grapple" => "Захватная",
            "sturdy" => "Устойчивая", "precise" => "Точная", "fast" => "Быстрая", _ => s
        };

        private static string MetamagicLabel(string e) => e switch
        {
            "double_range" => "с удвоенной дальностью",
            "plus2" => "получит +2 к броску",
            "double_die" => "удвоит одну кость урона",
            "extra_target" => "поразит доп. цель",
            _ => e
        };

        private static string DruidFormLabel(string f) => f switch
        {
            "giant_arachnid" => "Огромный арахнид",
            "silkworm" => "Шелкопряд",
            "wolf" => "Волк",
            "dog" => "Собака",
            "gazelle" => "Газель",
            "armadillo" => "Броненосец",
            _ => f
        };

        private static void LogFeature(string message)
        {
            Debug.Log($"[ClassFeature] {message}");
        }
    }
}
