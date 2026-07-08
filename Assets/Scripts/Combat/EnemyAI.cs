using System.Linq;
using UnityEngine;
using RPG.Character;

namespace RPG.Combat
{
    /// <summary>
    /// Спец-действия врагов по ГДД, распознаются по тегам EnemyDefinition.tags:
    ///  — "ranged" / "keep_distance" / "retreat":  предпочитает держать дистанцию.
    ///  — "grapple":         «Схватить и Притянуть» (Мораль Похоти / Защитник Корней).
    ///  — "psychic":         психо-удар с потерей Выносливости (Мораль Страха).
    ///  — "swarm_bonus":     Зомби — при наличии другого зомби рядом с целью, атаки по цели с преимуществом.
    ///  — "swarm":           стайное поведение (Гигантские комары/Крысы).
    ///  — "flying":          +2 к Сложности (уклонение).
    ///  — "bloodsucker":     Комары — попадание может забрать доп. шкалу за счёт Выносливости.
    ///  — "pack_tactics":    Лесной солдат / Лютоволк — усиленный урон при друге вплотную к цели.
    ///  — "crippling_strike":Лютоволк — Уязвимость цели.
    ///  — "poison":          Скорпион — Отравить цель за счёт Страха/Выносливости.
    ///  — "double_strike":   Скорпион — двойной удар по двум целям вплотную.
    ///  — "impulse":         генерирует Страх при успешной атаке (уже реализовано в PerformAttack).
    ///  — "magic_steel":     атаки считаются физ+маг (для будущего сопротивления).
    ///  — "suppression_burst":Заклинатель-мечник — залп по группе.
    ///  — "leader":          может активировать до 5 союзников за 2 Страха.
    ///  — "chaos_flow":      Колдун — Поток Хаоса по до 3 целей вплотную.
    ///  — "curse":           Колдун — Проклятие цели.
    ///  — "hold_tight":      Коленолом — Обездвиженные им получают двойной урон от других.
    ///  — "pin_down":        Коленолом — прижать (Обездвижен + Уязвим вместо урона).
    ///  — "forest_control":  Лесной солдат — опрокинуть дерево.
    ///  — "ground_slam":     Защитник Корней — отбросить всех в 2 кл. на 12 кл. и потеря Выносливости.
    ///  — "creeping_flame":  Слизь — за собой оставляет огненный след.
    ///  — "ignite":          Слизь — поджечь цель.
    ///  — "split_on_low_hp": Слизь — разделиться при ≤2 шкал здоровья.
    ///  — "relentless_2":    может активироваться до 2 раз за раунд (уже — через AI).
    ///  — "scorched_earth":  Малый огненный элементаль — выжженная земля.
    ///  — "fire_burst":      элементаль — взрыв пламени (Страх).
    ///  — "fuel_absorb":     элементаль — восстанавливается на легковоспламеняющемся.
    /// </summary>
    public static class EnemyAI
    {
        public static bool TryPerformSpecialAction(CombatUnit u, CombatUnit target, CombatManager cm)
        {
            if (u == null || target == null || cm == null) return false;
            var tags = u.enemyTags;
            if (tags == null || tags.Count == 0) return false;

            int dist = CombatManager.ManhattanDistance(u.gridPosition, target.gridPosition);

            // -------- «Схватить и Притянуть» --------
            if (tags.Contains("grapple") && !target.HasEffectId("immobilized_temp") && dist <= 6 && cm.TrySpendFear(1))
            {
                target.gridPosition = u.gridPosition + new Vector2Int(1, 0);
                target.ApplyEffect(new StatusEffect
                {
                    effectId = "immobilized_temp",
                    displayName = "Обездвижен (схвачен)",
                    effectType = EffectType.Immobilized,
                    remainingDuration = 2
                });
                var atk = cm.PerformAttack(u, target, damageDiceOverride: "1d6+2");
                Debug.Log($"[AI] {u.displayName}: Схватить и Притянуть — {atk.message}");
                return true;
            }

            // -------- Психо-удар (Мораль Страха) --------
            if (tags.Contains("psychic") && dist <= 6)
            {
                bool boosted = cm.TrySpendFear(1);
                var atk = cm.PerformAttack(u, target);
                if (atk.hit) target.stats.SpendStamina(1);
                if (boosted && atk.hit)
                {
                    int bonus = cm.RollDamage("1d6");
                    target.stats.TakeDamage(bonus);
                }
                return true;
            }

            // -------- Стрелок --------
            if (tags.Contains("ranged") || tags.Contains("keep_distance") || tags.Contains("retreat"))
            {
                if (dist <= 12 && dist >= 3) { cm.PerformAttack(u, target); return true; }
                var away = u.gridPosition - target.gridPosition;
                if (away.sqrMagnitude == 0) away = Vector2Int.right;
                u.gridPosition += new Vector2Int(Mathf.Clamp(away.x, -1, 1) * 3, Mathf.Clamp(away.y, -1, 1) * 3);
                cm.PerformAttack(u, target);
                return true;
            }

            // -------- Скорпион: Двойной удар --------
            if (tags.Contains("double_strike"))
            {
                var second = cm.AllUnits.FirstOrDefault(x => x != target && x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(u.gridPosition, x.gridPosition) <= 1);
                if (second != null && u.stats.currentStamina >= 1)
                {
                    u.stats.SpendStamina(1);
                    cm.PerformAttack(u, target);
                    cm.PerformAttack(u, second);
                    return true;
                }
            }

            // -------- Скорпион: Ядовитое жало --------
            if (tags.Contains("poison") && dist <= 2 && u.stats.currentStamina >= 1)
            {
                var atk = cm.PerformAttack(u, target, damageDiceOverride: "1d4+4");
                if (atk.hit)
                {
                    u.stats.SpendStamina(1);
                    target.ApplyEffect(new StatusEffect
                    {
                        effectId = "poisoned_temp",
                        displayName = "Отравлен",
                        effectType = EffectType.Poison,
                        remainingDuration = 5
                    });
                    Debug.Log($"[AI] {u.displayName}: Ядовитое жало — цель Отравлена.");
                }
                return true;
            }

            // -------- Колдун: Проклятие --------
            if (tags.Contains("curse") && dist <= 12 && !target.HasEffectId("cursed_temp"))
            {
                target.ApplyEffect(new StatusEffect
                {
                    effectId = "cursed_temp",
                    displayName = "Проклят",
                    effectType = EffectType.Curse,
                    remainingDuration = 3
                });
                Debug.Log($"[AI] {u.displayName}: Проклятие на {target.displayName}.");
                return true;
            }

            // -------- Колдун: Поток Хаоса --------
            if (tags.Contains("chaos_flow") && u.stats.currentStamina >= 1)
            {
                var targets = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 2).Take(3).ToList();
                if (targets.Count > 0)
                {
                    u.stats.SpendStamina(1);
                    foreach (var t in targets) cm.PerformAttack(u, t, damageDiceOverride: "2d6+3");
                    return true;
                }
            }

            // -------- Коленолом: Прижать к Земле --------
            if (tags.Contains("pin_down") && dist <= 1)
            {
                var atk = cm.PerformAttack(u, target, damageOverride: 0);
                if (atk.hit)
                {
                    target.ApplyEffect(new StatusEffect
                    {
                        effectId = "immobilized_temp",
                        displayName = "Обездвижен (прижат)",
                        effectType = EffectType.Immobilized,
                        remainingDuration = 3
                    });
                    target.ApplyEffect(new StatusEffect
                    {
                        effectId = "vulnerable_temp",
                        displayName = "Уязвим (прижат)",
                        effectType = EffectType.Vulnerable,
                        remainingDuration = 3
                    });
                    Debug.Log($"[AI] {u.displayName}: Прижать к Земле — {target.displayName} Обездвижен и Уязвим.");
                }
                return true;
            }

            // -------- Защитник Корней: Удар об Землю --------
            if (tags.Contains("ground_slam"))
            {
                var around = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 2).ToList();
                if (around.Count >= 2)
                {
                    foreach (var t in around)
                    {
                        var dir = t.gridPosition - u.gridPosition;
                        if (dir.sqrMagnitude == 0) dir = Vector2Int.right;
                        t.gridPosition += new Vector2Int(Mathf.Clamp(dir.x, -1, 1) * 12, Mathf.Clamp(dir.y, -1, 1) * 12);
                        t.stats.SpendStamina(1);
                    }
                    Debug.Log($"[AI] {u.displayName}: Удар об Землю — {around.Count} целей отброшено.");
                    return true;
                }
            }

            // -------- Заклинатель-мечник: Подавляющий Взрыв --------
            if (tags.Contains("suppression_burst") && u.stats.currentStamina >= 1)
            {
                var group = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 12).ToList();
                if (group.Count >= 2)
                {
                    u.stats.SpendStamina(1);
                    foreach (var t in group) cm.PerformAttack(u, t, damageDiceOverride: "1d8+2", isSpell: true);
                    Debug.Log($"[AI] {u.displayName}: Подавляющий Взрыв по {group.Count} целям.");
                    return true;
                }
            }

            // -------- Огненный элементаль: Выжженная земля --------
            if (tags.Contains("scorched_earth") && u.stats.currentStamina >= 1)
            {
                u.stats.SpendStamina(1);
                CombatZones.Add(new CombatZone
                {
                    kind = ZoneKind.Fire,
                    center = target.gridPosition,
                    radius = 2,
                    roundsLeft = 3
                });
                foreach (var t in cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, target.gridPosition) <= 2))
                    t.stats.TakeDamage(cm.RollDamage("2d8"));
                Debug.Log($"[AI] {u.displayName}: Выжженная земля в {target.gridPosition}.");
                return true;
            }

            // -------- Огненный элементаль: Взрыв пламени --------
            if (tags.Contains("fire_burst") && cm.TrySpendFear(1))
            {
                var around = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 6).ToList();
                foreach (var t in around)
                {
                    var atk = cm.PerformAttack(u, t, damageDiceOverride: "1d8");
                    if (atk.hit)
                    {
                        var dir = t.gridPosition - u.gridPosition;
                        if (dir.sqrMagnitude == 0) dir = Vector2Int.right;
                        t.gridPosition += new Vector2Int(Mathf.Clamp(dir.x, -1, 1) * 12, Mathf.Clamp(dir.y, -1, 1) * 12);
                    }
                }
                Debug.Log($"[AI] {u.displayName}: Взрыв пламени по {around.Count} целям.");
                return true;
            }

            // -------- Лесной солдат: Контроль Леса --------
            if (tags.Contains("forest_control") && cm.TrySpendFear(1))
            {
                var t = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 6).FirstOrDefault();
                if (t != null)
                {
                    t.stats.TakeDamage(cm.RollDamage("1d10"));
                    Debug.Log($"[AI] {u.displayName}: Контроль Леса — на {t.displayName} падает дерево.");
                    return true;
                }
            }

            // -------- Слизь: Воспламенить --------
            if (tags.Contains("ignite") && dist <= 2)
            {
                var atk = cm.PerformAttack(u, target, damageDiceOverride: "1d8", isSpell: true);
                if (atk.hit)
                    target.ApplyEffect(new StatusEffect
                    {
                        effectId = "ignited",
                        displayName = "Воспламенён",
                        effectType = EffectType.Ignited,
                        remainingDuration = 5
                    });
                return true;
            }

            // -------- Лидер: Действовать как единое целое --------
            if (tags.Contains("leader") && cm.TrySpendFear(2))
            {
                var allies = cm.AllUnits.Where(x => x.side == u.side && !x.IsDead && x != u
                    && CombatManager.ManhattanDistance(x.gridPosition, u.gridPosition) <= 12).Take(5).ToList();
                foreach (var a in allies)
                {
                    var t = cm.AllUnits.Where(x => x.side == CombatSide.Player && !x.IsDead)
                        .OrderBy(x => CombatManager.ManhattanDistance(x.gridPosition, a.gridPosition)).FirstOrDefault();
                    if (t != null) cm.PerformAttack(a, t);
                }
                Debug.Log($"[AI] {u.displayName}: активировал {allies.Count} союзников.");
                return true;
            }

            return false;
        }
    }
}
