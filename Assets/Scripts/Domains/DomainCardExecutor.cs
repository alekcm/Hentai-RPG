using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.Character;
using RPG.Combat;

namespace RPG.Domains
{
    /// <summary>
    /// Исполнитель активных карт доменов.
    ///
    /// Каждая карта — это метод `Cast_<cardId>(context)`. Вызов происходит:
    ///   1. UI собирает контекст (кто активирует, цель — если нужно, суб-выбор — если у карты несколько эффектов).
    ///   2. Показывается `AbilityConfirmDialog` с текстом стоимости.
    ///   3. При «Да» — списываем ресурсы, применяем эффект, шлём лог.
    ///
    /// Возвращает `CardResult` — использовалось ли действие юнита и был ли успех с Надеждой
    /// (чтобы CombatManager правильно решил, продолжает игрок ход или передаёт).
    /// </summary>
    public static class DomainCardExecutor
    {
        // --- Данные для карт с сохраняемым состоянием на бой ---

        /// <summary>Заряды карты «Высвобождение хаоса» (по юниту).</summary>
        private static readonly Dictionary<string, int> chaosCharges = new();
        /// <summary>Использованные-в-этом-бою карты уникального применения (Вдохновляющие слова, Подбадривание, Молебен и т.п.).</summary>
        private static readonly HashSet<string> perCombatUsed = new();

        public static void ResetForNewCombat(IEnumerable<CombatUnit> units)
        {
            chaosCharges.Clear();
            perCombatUsed.Clear();

            foreach (var u in units ?? new List<CombatUnit>())
            {
                if (u?.character == null) continue;
                // Инициализация зарядов «Высвобождение хаоса»
                if (PassiveEffectsRegistry.HasCard(u.character, "magic_1_chaos_release"))
                {
                    int level = Mathf.Max(1, u.stats.level);
                    chaosCharges[u.unitId] = 1 + level / 2;
                }
            }
        }

        public static int GetChaosCharges(string unitId) => chaosCharges.TryGetValue(unitId, out var v) ? v : 0;

        // ================================================================
        //   ДИСПЕТЧЕР
        // ================================================================

        public static bool CanUse(CombatUnit unit, string cardId, out string reasonIfNo)
        {
            reasonIfNo = null;
            if (unit == null || unit.character == null) { reasonIfNo = "нет юнита"; return false; }
            if (!PassiveEffectsRegistry.HasCard(unit.character, cardId)) { reasonIfNo = "карта не выбрана"; return false; }

            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) { reasonIfNo = "не в бою"; return false; }

            var card = DomainDatabase.GetCard(cardId);
            if (card == null) { reasonIfNo = "неизвестная карта"; return false; }

            // Проверка ресурсов
            if (card.cost.hope > 0 && cm.HopePool < card.cost.hope)
            { reasonIfNo = $"нужно {card.cost.hope} Надежды (у нас {cm.HopePool})"; return false; }
            if (card.cost.stamina > 0 && unit.stats.currentStamina < card.cost.stamina)
            { reasonIfNo = $"нужно {card.cost.stamina} Выносливости"; return false; }

            // Раз-в-бой
            if (IsPerCombat(cardId) && perCombatUsed.Contains(PerCombatKey(unit, cardId)))
            { reasonIfNo = "уже использовано в этом бою"; return false; }

            // Для «Высвобождение хаоса» — если заряды пусты и нет Выносливости на восстановление
            if (cardId == "magic_1_chaos_release")
            {
                int charges = GetChaosCharges(unit.unitId);
                if (charges <= 0 && unit.stats.currentStamina < 1)
                { reasonIfNo = "заряды кончились, и нет Выносливости на перезарядку"; return false; }
            }

            return true;
        }

        /// <summary>
        /// Выполнить карту. Успех/провал возвращается в CardResult.
        /// UI отвечает за подтверждение до вызова этого метода.
        /// </summary>
        public static CardResult Execute(CombatUnit unit, string cardId, CardContext ctx = null)
        {
            ctx = ctx ?? new CardContext();
            var card = DomainDatabase.GetCard(cardId);
            if (card == null) return CardResult.Fail("Неизвестная карта.");

            var cm = CombatManager.Instance;
            if (cm == null) return CardResult.Fail("Нет менеджера боя.");

            // Оплата стоимости (Надежда, Выносливость, заряд).
            if (card.cost.hope > 0 && !cm.TrySpendHope(card.cost.hope))
                return CardResult.Fail("Недостаточно Надежды.");
            if (card.cost.stamina > 0) unit.stats.SpendStamina(card.cost.stamina);

            // Диспетчеризация
            switch (cardId)
            {
                // Оружие
                case "weapon_1_whirlwind": return Cast_Whirlwind(unit, ctx);
                // Тело
                case "body_1_deft_maneuvers": return Cast_DeftManeuvers(unit, ctx);
                // Магия
                case "magic_1_book_of_ava": return Cast_BookOfAva(unit, ctx);
                case "magic_1_book_of_taifar": return Cast_BookOfTaifar(unit, ctx);
                case "magic_1_chaos_release": return Cast_ChaosRelease(unit, ctx);
                // Очарование
                case "charm_1_provoke": return Cast_Provoke(unit, ctx);
                case "charm_1_trick": return Cast_Trick(unit, ctx);
                case "charm_1_inspiring_words": return Cast_InspiringWords(unit, ctx);
                // Ужас
                case "terror_1_voice_of_dread": return Cast_VoiceOfDread(unit, ctx);
                case "terror_1_withering_strike": return Cast_WitheringStrike(unit, ctx);
                case "terror_1_expose_weakness": return Cast_ExposeWeakness(unit, ctx);
                // Природа
                case "nature_1_entangling_vines": return Cast_EntanglingVines(unit, ctx);
                case "nature_1_regeneration": return Cast_Regeneration(unit, ctx);
                case "nature_1_elemental_guardian": return Cast_ElementalGuardian(unit, ctx);
                // Свет
                case "light_1_arrow_of_light": return Cast_ArrowOfLight(unit, ctx);
                case "light_1_healing_touch": return Cast_HealingTouch(unit, ctx);
                // Защита
                case "defense_1_throwback": return Cast_Throwback(unit, ctx);

                // Ниже — карты без "активной" ветки (реагируют по триггеру или пассивны).
                // Они всё равно могут быть выбраны из UI как "переключатель готовности".
                case "weapon_1_second_wind":
                case "body_1_evasive":
                case "defense_1_i_am_your_shield":
                case "light_1_encourage":
                    return CardResult.Info($"Карта «{card.displayName}» сработает по триггеру — держите её наготове.");

                // Пассивные — активировать нельзя, лишь показать описание.
                case "weapon_1_no_way":
                case "body_1_elusive":
                case "defense_1_defensive_mastery":
                    return CardResult.Info($"«{card.displayName}» — пассивная карта, эффект уже применён.");
            }

            return CardResult.Fail($"Карта «{cardId}» пока не реализована в исполнителе.");
        }

        // ================================================================
        //   ОРУЖИЕ
        // ================================================================

        private static CardResult Cast_Whirlwind(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна первая цель.");
            var cm = CombatManager.Instance;
            var main = cm.PerformAttack(u, ctx.primaryTarget);
            if (!main.hit) return CardResult.Fail("Основной удар не прошёл — Вихрь не активируется.", consumeAction: true);

            var others = cm.AllUnits
                .Where(x => x != ctx.primaryTarget && !x.IsDead && x.side != u.side
                          && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 2)
                .ToList();

            int hitCount = 0;
            foreach (var t in others)
            {
                var sub = cm.PerformAttack(u, t, damageOverride: main.rawDamage / 2);
                if (sub.hit) hitCount++;
            }

            return CardResult.Ok($"Вихрь: попал по основной цели, поражено ещё {hitCount} врагов вокруг.",
                                  hopeSuccess: main.roll.HopeSide, consumeAction: true);
        }

        // ================================================================
        //   ТЕЛО
        // ================================================================

        private static CardResult Cast_DeftManeuvers(CombatUnit u, CardContext ctx)
        {
            u.buffAttackBonusNextRoll += 1;
            u.movementBudgetOverride = 12; // вдвое больше стандартного
            return CardResult.Ok("Ловкие манёвры: +1 к следующей Атаке, движение до 12 клеток.",
                                  hopeSuccess: true, consumeAction: false);
        }

        // ================================================================
        //   МАГИЯ
        // ================================================================

        private static CardResult Cast_BookOfAva(CombatUnit u, CardContext ctx)
        {
            var cm = CombatManager.Instance;
            switch (ctx.subChoice)
            {
                case "push":
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    var atk = cm.PerformAttack(u, ctx.primaryTarget,
                        damageDiceOverride: "d10+2", isSpell: true);
                    // Отталкивание на 12 клеток от кастера.
                    if (atk.hit)
                    {
                        var dir = ctx.primaryTarget.gridPosition - u.gridPosition;
                        if (dir.sqrMagnitude == 0) dir = Vector2Int.right;
                        ctx.primaryTarget.gridPosition += new Vector2Int(
                            Mathf.Clamp(dir.x, -1, 1) * 12,
                            Mathf.Clamp(dir.y, -1, 1) * 12);
                    }
                    return CardResult.Ok($"Силовой толчок: {atk.message}",
                                          hopeSuccess: atk.hit && atk.roll.HopeSide, consumeAction: true);

                case "ice_armor":
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    ctx.primaryTarget.stats.armorRating += 1;
                    return CardResult.Ok($"Доспехи льда: +1 к Показателю Брони {ctx.primaryTarget.displayName}.",
                                          hopeSuccess: true, consumeAction: true);

                case "frostbite":
                default:
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    var frost = cm.PerformAttack(u, ctx.primaryTarget,
                        damageDiceOverride: "d6", isSpell: true);
                    if (frost.hit)
                        ctx.primaryTarget.ApplyEffect(new StatusEffect
                        {
                            effectId = "immobilized_temp",
                            displayName = "Обездвижен (временно)",
                            effectType = EffectType.Immobilized,
                            remainingDuration = 1
                        });
                    return CardResult.Ok($"Обморожение: {frost.message}",
                                          hopeSuccess: frost.hit && frost.roll.HopeSide, consumeAction: true);
            }
        }

        private static CardResult Cast_BookOfTaifar(CombatUnit u, CardContext ctx)
        {
            var cm = CombatManager.Instance;
            switch (ctx.subChoice)
            {
                case "sleep":
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    var sleep = cm.PerformSpellCheck(u, ctx.primaryTarget.stats.evasion);
                    if (sleep.success)
                    {
                        ctx.primaryTarget.ApplyEffect(new StatusEffect
                        {
                            effectId = "asleep",
                            displayName = "Спит",
                            effectType = EffectType.Asleep,
                            remainingDuration = 99
                        });
                        return CardResult.Ok($"Дрёма: {ctx.primaryTarget.displayName} засыпает.",
                                              hopeSuccess: sleep.roll.HopeSide, consumeAction: true);
                    }
                    return CardResult.Fail("Дрёма не сработала.",
                                            consumeAction: true, hopeSuccess: sleep.roll.HopeSide);

                case "wildfire":
                    var enemies = cm.AllUnits.Where(x => x.side != u.side && !x.IsDead
                        && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 1).Take(3).ToList();
                    if (enemies.Count == 0) return CardResult.Fail("Никого рядом.");
                    int hit = 0; bool anyHope = false;
                    foreach (var t in enemies)
                    {
                        var atk = cm.PerformAttack(u, t, damageDiceOverride: "2d6", isSpell: true);
                        if (atk.hit) { hit++; t.stats.SpendStamina(1); }
                        if (atk.roll.HopeSide) anyHope = true;
                    }
                    return CardResult.Ok($"Дикое пламя: успехов {hit}/{enemies.Count}.",
                                          hopeSuccess: anyHope, consumeAction: true);

                case "mystic_fog":
                default:
                    var fog = cm.PerformSpellCheck(u, 13);
                    if (fog.success)
                        cm.SpawnMysticFog(u.gridPosition);
                    return CardResult.Ok(fog.success ? "Мистический туман окутывает поле боя."
                                                     : "Туман не собрался.",
                                          hopeSuccess: fog.roll.HopeSide, consumeAction: true);
            }
        }

        private static CardResult Cast_ChaosRelease(CombatUnit u, CardContext ctx)
        {
            int have = GetChaosCharges(u.unitId);
            if (have <= 0)
            {
                // Перезарядка ценой уже уплаченной Выносливости (cost.stamina=1 в модели карты).
                int level = Mathf.Max(1, u.stats.level);
                chaosCharges[u.unitId] = 1 + level / 2;
                return CardResult.Ok("Высвобождение хаоса перезаряжено.", hopeSuccess: true, consumeAction: false);
            }

            int spend = Mathf.Clamp(ctx.intParam <= 0 ? 1 : ctx.intParam, 1, have);
            chaosCharges[u.unitId] = have - spend;

            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            int damage = 0;
            for (int i = 0; i < spend; i++) damage += UnityEngine.Random.Range(1, 11);

            var atk = cm.PerformAttack(u, ctx.primaryTarget, damageOverride: damage, isSpell: true);
            return CardResult.Ok($"Высвобождение хаоса ({spend} заряда): {atk.message}",
                                  hopeSuccess: atk.roll.HopeSide, consumeAction: true);
        }

        // ================================================================
        //   ОЧАРОВАНИЕ
        // ================================================================

        private static CardResult Cast_Provoke(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var check = cm.PerformSpellCheck(u, ctx.primaryTarget.stats.evasion);
            if (!check.success)
                return CardResult.Fail("Провокация не удалась.",
                                        consumeAction: true, hopeSuccess: check.roll.HopeSide);

            ctx.primaryTarget.ApplyEffect(new StatusEffect
            {
                effectId = "provoked",
                displayName = "Спровоцирован",
                effectType = EffectType.Charm,
                remainingDuration = 3
            });
            return CardResult.Ok($"Провокация: {ctx.primaryTarget.displayName} получает помеху атак на других.",
                                  hopeSuccess: check.roll.HopeSide, consumeAction: true);
        }

        private static CardResult Cast_Trick(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            ctx.primaryTarget.ApplyEffect(new StatusEffect
            {
                effectId = "vulnerable_next_attack",
                displayName = "Уязвим (на следующую атаку)",
                effectType = EffectType.Vulnerable,
                remainingDuration = 1
            });
            return CardResult.Ok($"Уловка: {ctx.primaryTarget.displayName} уязвим для вашей следующей атаки.",
                                  hopeSuccess: true, consumeAction: false);
        }

        private static CardResult Cast_InspiringWords(CombatUnit u, CardContext ctx)
        {
            var key = PerCombatKey(u, "charm_1_inspiring_words");
            if (perCombatUsed.Contains(key)) return CardResult.Fail("Уже использовано в этом бою.");
            perCombatUsed.Add(key);

            switch (ctx.subChoice)
            {
                case "heal":
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    ctx.primaryTarget.stats.HealHealthSlots(1);
                    return CardResult.Ok("Вдохновляющие слова: восстановлена 1 шкала здоровья.",
                                          hopeSuccess: true, consumeAction: false);
                case "stamina":
                    if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
                    ctx.primaryTarget.stats.RestoreStamina(1);
                    return CardResult.Ok("Вдохновляющие слова: восстановлена Выносливость цели.",
                                          hopeSuccess: true, consumeAction: false);
                case "hope":
                default:
                    CombatManager.Instance.RefundHope(1);
                    return CardResult.Ok("Вдохновляющие слова: +1 Надежда.",
                                          hopeSuccess: true, consumeAction: false);
            }
        }

        // ================================================================
        //   УЖАС
        // ================================================================

        private static CardResult Cast_VoiceOfDread(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var check = cm.PerformSpellCheck(u, ctx.primaryTarget.stats.evasion);
            if (!check.success)
                return CardResult.Fail("Голос ужаса не сработал.",
                                        consumeAction: true, hopeSuccess: check.roll.HopeSide);

            ctx.primaryTarget.stats.SpendStamina(1);
            ctx.primaryTarget.ApplyEffect(new StatusEffect
            {
                effectId = "vulnerable_temp",
                displayName = "Уязвим (временно)",
                effectType = EffectType.Vulnerable,
                remainingDuration = 2
            });
            return CardResult.Ok($"Глас ужаса: {ctx.primaryTarget.displayName} теряет Выносливость и Уязвим.",
                                  hopeSuccess: check.roll.HopeSide, consumeAction: true);
        }

        private static CardResult Cast_WitheringStrike(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var atk = cm.PerformAttack(u, ctx.primaryTarget,
                damageDiceOverride: "d6+1", isSpell: true);
            if (atk.hit && atk.roll.FearSide)
            {
                // «Успех со Страхом» — вместо d6+1 наносим d10+1 (по ГДД). Уже нанесли, добавим дельту.
                int extra = cm.RollDamage("d10+1") - atk.rawDamage;
                if (extra > 0)
                    ctx.primaryTarget.stats.TakeDamage(extra);
            }
            return CardResult.Ok($"Удар увядания: {atk.message}",
                                  hopeSuccess: atk.roll.HopeSide, consumeAction: true);
        }

        private static CardResult Cast_ExposeWeakness(CombatUnit u, CardContext ctx)
        {
            var cm = CombatManager.Instance;
            var targets = cm.AllUnits.Where(x => x.side != u.side && !x.IsDead
                && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 2).ToList();
            if (targets.Count == 0) return CardResult.Fail("Никого рядом.");

            bool anyHope = false; int hits = 0;
            foreach (var t in targets)
            {
                var atk = cm.PerformAttack(u, t, damageDiceOverride: "d8+2", isSpell: true);
                if (atk.hit)
                {
                    hits++;
                    if (t.HasEffectId("vulnerable_temp") || t.HasEffectId("vulnerable_next_attack"))
                        t.stats.TakeDamage(cm.RollDamage("1d8"));
                }
                if (atk.roll.HopeSide) anyHope = true;
            }
            return CardResult.Ok($"Выявить слабость: попаданий {hits}/{targets.Count}.",
                                  hopeSuccess: anyHope, consumeAction: true);
        }

        // ================================================================
        //   ПРИРОДА
        // ================================================================

        private static CardResult Cast_EntanglingVines(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var atk = cm.PerformAttack(u, ctx.primaryTarget,
                damageDiceOverride: "1d8+1", isSpell: true);
            if (atk.hit)
                ctx.primaryTarget.ApplyEffect(new StatusEffect
                {
                    effectId = "immobilized_temp",
                    displayName = "Обездвижен (лозы)",
                    effectType = EffectType.Immobilized,
                    remainingDuration = 2
                });
            return CardResult.Ok($"Опутывающие лозы: {atk.message}",
                                  hopeSuccess: atk.roll.HopeSide, consumeAction: true);
        }

        private static CardResult Cast_Regeneration(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            const int slots = 3; // Стабильные 3 шкалы (по обновлённым правилам)
            ctx.primaryTarget.stats.HealHealthSlots(slots);
            ctx.primaryTarget.stats.RestoreStamina(1);
            return CardResult.Ok($"Регенерация: восстановлено {slots} шкал здоровья и Выносливость.",
                                  hopeSuccess: true, consumeAction: true);
        }

        private static CardResult Cast_ElementalGuardian(CombatUnit u, CardContext ctx)
        {
            switch (ctx.subChoice)
            {
                case "fire":
                    u.SetElementalGuardian(ElementalGuardianKind.Fire);
                    return CardResult.Ok("Хранитель стихий: Огонь — атакующий вплотную получит 1d10 маг. урона.",
                                          hopeSuccess: true, consumeAction: false);
                case "earth":
                    u.stats.damageThreshold += Mathf.Max(1, u.stats.level);
                    return CardResult.Ok("Хранитель стихий: Земля — +Порог Урона на уровень.",
                                          hopeSuccess: true, consumeAction: false);
                case "water":
                    u.SetElementalGuardian(ElementalGuardianKind.Water);
                    return CardResult.Ok("Хранитель стихий: Вода — при след. ударе враги вокруг цели теряют Выносливость.",
                                          hopeSuccess: true, consumeAction: false);
                case "air":
                default:
                    u.movementBudgetOverride = 12;
                    u.ignoreHazardsThisTurn = true;
                    return CardResult.Ok("Хранитель стихий: Воздух — движение удвоено, поверхности игнорируются.",
                                          hopeSuccess: true, consumeAction: false);
            }
        }

        // ================================================================
        //   СВЕТ
        // ================================================================

        private static CardResult Cast_ArrowOfLight(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var atk = cm.PerformAttack(u, ctx.primaryTarget,
                damageDiceOverride: "d8+2", isSpell: true);
            if (atk.hit)
                ctx.primaryTarget.ApplyEffect(new StatusEffect
                {
                    effectId = "vulnerable_temp",
                    displayName = "Уязвим (свет)",
                    effectType = EffectType.Vulnerable,
                    remainingDuration = 2
                });
            return CardResult.Ok($"Стрела света: {atk.message}",
                                  hopeSuccess: atk.roll.HopeSide, consumeAction: true);
        }

        private static CardResult Cast_HealingTouch(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            switch (ctx.subChoice)
            {
                case "two_slots":
                    ctx.primaryTarget.stats.HealHealthSlots(2);
                    return CardResult.Ok("Исцеляющее касание: +2 шкалы здоровья.",
                                          hopeSuccess: true, consumeAction: true);
                default:
                    ctx.primaryTarget.stats.HealHealthSlots(1);
                    ctx.primaryTarget.stats.RestoreStamina(1);
                    return CardResult.Ok("Исцеляющее касание: +1 шкала здоровья и Выносливость.",
                                          hopeSuccess: true, consumeAction: true);
            }
        }

        // ================================================================
        //   ЗАЩИТА
        // ================================================================

        private static CardResult Cast_Throwback(CombatUnit u, CardContext ctx)
        {
            if (ctx.primaryTarget == null) return CardResult.Fail("Нужна цель.");
            var cm = CombatManager.Instance;
            var atk = cm.PerformAttack(u, ctx.primaryTarget);
            if (atk.hit)
            {
                if (atk.roll.HopeSide)
                {
                    int extra = cm.RollDamage("d6");
                    ctx.primaryTarget.stats.TakeDamage(extra);
                }
                // Отбрасывание на 4 клетки.
                var dir = ctx.primaryTarget.gridPosition - u.gridPosition;
                if (dir.sqrMagnitude == 0) dir = Vector2Int.right;
                ctx.primaryTarget.gridPosition += new Vector2Int(
                    Mathf.Clamp(dir.x, -1, 1) * 4,
                    Mathf.Clamp(dir.y, -1, 1) * 4);
            }
            return CardResult.Ok($"Отбрасывание: {atk.message}",
                                  hopeSuccess: atk.roll.HopeSide, consumeAction: true);
        }

        // ================================================================
        //   Служебное
        // ================================================================

        private static bool IsPerCombat(string cardId) => cardId switch
        {
            "charm_1_inspiring_words" => true,
            "light_1_encourage" => true,
            _ => false
        };

        private static string PerCombatKey(CombatUnit u, string cardId) => $"{u.unitId}::{cardId}";

        public static bool MarkUsedOnce(CombatUnit u, string cardId)
        {
            var key = PerCombatKey(u, cardId);
            if (perCombatUsed.Contains(key)) return false;
            perCombatUsed.Add(key);
            return true;
        }
    }

    // ================================================================
    //   Модели контекста и результата
    // ================================================================

    /// <summary>Контекст, передаваемый в исполнитель карты из UI.</summary>
    public class CardContext
    {
        public CombatUnit primaryTarget;
        public List<CombatUnit> extraTargets = new();
        public string subChoice;        // например "push"/"ice_armor"/"frostbite" для Книги Авы
        public int intParam;            // напр. кол-во зарядов у Высвобождения хаоса
    }

    public class CardResult
    {
        public bool success;
        public string message;
        public bool consumeAction;      // тратит ли действие юнита
        public bool hopeSuccess;        // считается ли ход "успехом с Надеждой" (чтобы игрок сохранял ход)

        public static CardResult Ok(string msg, bool hopeSuccess = true, bool consumeAction = true)
            => new CardResult { success = true, message = msg, hopeSuccess = hopeSuccess, consumeAction = consumeAction };
        public static CardResult Fail(string msg, bool hopeSuccess = false, bool consumeAction = false)
            => new CardResult { success = false, message = msg, hopeSuccess = hopeSuccess, consumeAction = consumeAction };
        public static CardResult Info(string msg)
            => new CardResult { success = true, message = msg, hopeSuccess = false, consumeAction = false };
    }

    public enum ElementalGuardianKind { None, Fire, Water }
}
