using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.Core;
using RPG.Character;
using RPG.Domains;
using RPG.Items;
using RPG.Companion;
using RPG.UI;

namespace RPG.Combat
{
    /// <summary>
    /// Пошаговый бой по правилам ГДД.
    /// Основные принципы:
    ///  — Игрок и союзники бросают Кости Дуальности (2d12): Hope die vs Fear die + бонус навыка.
    ///    Успех = total >= сложность. Если Hope > Fear → успех/провал "с Надеждой" (даёт +1 в пул).
    ///    Если Fear > Hope → "со Страхом" (даёт +1 Страха врагам).
    ///    Hope == Fear → критический успех (+1 Надежда и +1 Выносливость активирующему юниту).
    ///    Преимущество/помеха — ±d6 к сумме.
    ///  — Враги бросают d20 без Дуальности.
    ///  — Действие юнита: переместиться до 6 клеток и совершить Действие (Атака / Заклинание / Способность / Короткая передышка).
    ///  — Если бросок успешен и с Надеждой — сторона игрока продолжает активировать других юнитов.
    ///    Иначе ход переходит противнику, и противник ходит столько же раз, сколько ходила сторона игрока в этой связке.
    ///  — Урон идёт по броне: rawDamage vs DamageThreshold. Не пробили → 0. Пробили → ломается ячейка брони,
    ///    остаток = rawDamage - hpPerSlot по здоровью.
    ///  — В начале каждого боя броня и Выносливость всех юнитов восстанавливаются, HP не меняется.
    ///  — Все траты Надежды/Выносливости идут через отдельный подтверждающий UI (см. AbilityConfirmDialog).
    /// </summary>
    public class CombatManager : MonoBehaviour, ISaveable
    {
        public static CombatManager Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private int gridWidth = 16;
        [SerializeField] private int gridHeight = 12;

        [Header("Movement")]
        [SerializeField] private int baseMovementTiles = 6;

        // --- Состояние боя ---
        private bool isCombatActive;
        private List<CombatUnit> allUnits = new();
        private int hopePool;
        private int fearPool;
        private CombatSide activeSide = CombatSide.Player;

        /// <summary>Тактическая карта текущего боя (может быть null для «пустой» арены).</summary>
        private BattleMap currentMap;
        public BattleMap CurrentMap => currentMap;

        /// <summary>Осталось очков движения у выбранного юнита в этот ход.</summary>
        private int currentUnitMoveBudget;
        public int CurrentUnitMoveBudget => currentUnitMoveBudget;

        /// <summary>Сколько подряд активировала текущая сторона в этой "связке" ходов.</summary>
        private int consecutiveActivationsThisPass;

        /// <summary>Юниты, уже активировавшиеся в этом раунде на своей стороне.</summary>
        private HashSet<string> activatedThisRound = new();

        /// <summary>Ждём выбор игрока: какой юнит активировать / какое действие выполнить.</summary>
        private CombatUnit selectedUnit;

        public bool IsCombatActive => isCombatActive;
        public int HopePool => hopePool;
        public int FearPool => fearPool;
        public CombatSide ActiveSide => activeSide;
        public IReadOnlyList<CombatUnit> AllUnits => allUnits;
        public CombatUnit SelectedUnit => selectedUnit;

        public string SaveKey => "CombatManager";

        // --- События (для UI/логов) ---
        public event Action OnCombatStart;
        public event Action<bool> OnCombatEnd;                              // true = победа игрока
        public event Action<CombatUnit> OnUnitActivated;
        public event Action<CombatSide> OnTurnPassedToSide;
        public event Action<CombatLogEntry> OnCombatEvent;
        public event Action OnResourcesChanged;                             // Hope/Fear pools

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            GameManager.Instance.SaveManager.RegisterSaveable(this);
        }

        // ============================================================
        //  Запуск боя
        // ============================================================

        /// <summary>Запустить бой по id энкаунтера, найдя его в StreamingAssets/Encounters/&lt;id&gt;.json.</summary>
        public void StartCombat(string encounterId)
        {
            if (string.IsNullOrEmpty(encounterId))
            {
                Debug.LogError("[Combat] Пустой encounterId");
                return;
            }
            var enc = EncounterLoader.Load(encounterId);
            if (enc == null)
            {
                Debug.LogError($"[Combat] Не найден энкаунтер: {encounterId}");
                return;
            }
            StartCombat(enc);
        }

        public void StartCombat(CombatEncounter encounter)
        {
            if (isCombatActive) { Debug.LogWarning("[Combat] Already active"); return; }
            if (encounter == null) { Debug.LogError("[Combat] Encounter is null"); return; }

            allUnits.Clear();
            activatedThisRound.Clear();
            hopePool = 2;               // Стартовые 2 Надежды по ГДД (одна на игрока для маневра)
            fearPool = 1;               // Небольшой стартовый пул Страха
            activeSide = CombatSide.Player;
            consecutiveActivationsThisPass = 0;

            // Загружаем карту арены. Если в энкаунтере не указана — берём его id как fallback,
            // если и такой файл не найден — генерируем пустую 16x12.
            currentMap = null;
            if (!string.IsNullOrEmpty(encounter.arenaId))
                currentMap = BattleMapLoader.Load(encounter.arenaId);
            if (currentMap == null)
                currentMap = BattleMapLoader.Load(encounter.encounterId);
            if (currentMap == null)
                currentMap = BattleMapLoader.CreateDefault(gridWidth, gridHeight);

            var occupied = new HashSet<Vector2Int>();

            Vector2Int PickStart(Vector2Int desired)
            {
                if (currentMap.IsWalkable(desired) && !occupied.Contains(desired))
                    return desired;
                // Если занята — ищем ближайшую свободную walkable клетку.
                for (int r = 1; r < 10; r++)
                    for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        var p = desired + new Vector2Int(dx, dy);
                        if (currentMap.IsWalkable(p) && !occupied.Contains(p)) return p;
                    }
                return desired;
            }

            // --- Игрок ---
            var player = CharacterCreation.Instance?.Character;
            int playerStartIdx = 0;
            if (player != null)
            {
                var u = CreateUnitFromCharacter(player, CombatSide.Player);
                var pos = currentMap.playerStarts.Count > 0
                    ? currentMap.playerStarts[0]
                    : new Vector2Int(2, currentMap.height / 2);
                u.gridPosition = PickStart(pos);
                occupied.Add(u.gridPosition);
                allUnits.Add(u);
                playerStartIdx = 1;
            }

            // --- Компаньоны ---
            var companions = CompanionManager.Instance?.GetPartyMembers();
            if (companions != null)
            {
                int i = 0;
                foreach (var c in companions)
                {
                    var u = CreateUnitFromCompanion(c);
                    var pos = playerStartIdx + i < currentMap.playerStarts.Count
                        ? currentMap.playerStarts[playerStartIdx + i]
                        : new Vector2Int(1, currentMap.height / 2 + (i - 1));
                    u.gridPosition = PickStart(pos);
                    occupied.Add(u.gridPosition);
                    allUnits.Add(u);
                    i++;
                }
            }

            // --- Враги ---
            foreach (var e in encounter.enemies)
            {
                var u = CreateUnitFromEnemy(e);
                // Стартовая позиция: сначала карта (по enemyId), потом encounter.spawnPosition.
                Vector2Int pos = e.spawnPosition;
                if (currentMap.enemyStarts.TryGetValue(e.enemyId, out var mapPos))
                    pos = mapPos;
                u.gridPosition = PickStart(pos);
                occupied.Add(u.gridPosition);
                allUnits.Add(u);
            }

            // Стартовые ресурсы каждого юнита.
            foreach (var u in allUnits)
            {
                u.stats.ResetArmorAtCombatStart();
                u.stats.ResetStaminaAtCombatStart();
                u.ClearOneShotBuffs();
            }

            // Сбрасываем шину триггеров прошлого боя и переинициализируем регистры.
            CombatTriggerBus.ResetAllSubscriptions();
            CombatZones.Clear();
            DomainCardExecutor.ResetForNewCombat(allUnits);

            // Применяем пассивные карты доменов и классовые фичи.
            foreach (var u in allUnits)
                PassiveEffectsRegistry.ApplyOnCombatStart(u);

            // Инициализируем шины (расовые триггеры, реактивные карты, классовые фичи).
            RaceFeatureBus.Initialize(allUnits);
            ReactiveCardsBus.Initialize(allUnits);
            ClassFeaturesBus.Initialize(allUnits);

            isCombatActive = true;
            GameManager.Instance.SetGameState(GameState.Combat);
            GameManager.Instance.EventBus.RaiseCombatStarted();
            CombatTriggerBus.RaiseCombatStarted();
            OnCombatStart?.Invoke();
            Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"Начался бой: {encounter.encounterId}." });
            OnTurnPassedToSide?.Invoke(activeSide);
        }

        public void EndCombat(bool playerWon)
        {
            if (!isCombatActive) return;
            isCombatActive = false;
            CombatTriggerBus.RaiseCombatEnded(playerWon);
            CombatTriggerBus.ResetAllSubscriptions();
            GameManager.Instance.EventBus.RaiseCombatEnded(playerWon);
            OnCombatEnd?.Invoke(playerWon);
            GameManager.Instance.SetGameState(GameState.Dialogue);
            Log(new CombatLogEntry { kind = CombatLogKind.System, message = playerWon ? "Победа." : "Поражение." });
        }

        // ============================================================
        //  Активация юнитов
        // ============================================================

        /// <summary>Игрок вручную активирует своего юнита (клик по портрету/спрайту).</summary>
        public bool ActivatePlayerUnit(string unitId)
        {
            if (!isCombatActive || activeSide != CombatSide.Player) return false;
            var u = allUnits.Find(x => x.unitId == unitId && x.side == CombatSide.Player && !x.IsDead);
            if (u == null) return false;
            if (activatedThisRound.Contains(u.unitId)) return false;
            selectedUnit = u;
            // Свежий бюджет движения на активации.
            currentUnitMoveBudget = u.movementBudgetOverride > 0 ? u.movementBudgetOverride : baseMovementTiles;
            OnUnitActivated?.Invoke(u);
            return true;
        }

        /// <summary>Уменьшить оставшийся бюджет движения (после каждой пройденной клетки).</summary>
        public void ConsumeMovement(int cost)
        {
            currentUnitMoveBudget = Mathf.Max(0, currentUnitMoveBudget - cost);
        }

        private CombatUnit PickAIActivation()
        {
            // Простейший AI: любой ещё не активированный враг, живой.
            return allUnits.FirstOrDefault(u => u.side == CombatSide.Enemy && !u.IsDead
                                             && !activatedThisRound.Contains(u.unitId));
        }

        // ============================================================
        //  Действия
        // ============================================================

        /// <summary>Игрок или AI пытается атаковать цель. Возвращает результат — попал/нет, урон и т.д.</summary>
        public AttackResult PerformAttack(CombatUnit attacker, CombatUnit target,
            int damageOverride = -1, string damageDiceOverride = null, bool isSpell = false)
        {
            if (attacker == null || target == null || target.IsDead)
                return new AttackResult { message = "Некорректная цель." };

            // Друид в звериной форме — берём характеристики формы вместо оружия.
            WeaponDefinition weapon;
            bool inBeastForm = attacker.beastAttackDice != null;
            if (inBeastForm)
            {
                weapon = new WeaponDefinition
                {
                    itemId = "beast_form",
                    displayName = "Звериная форма",
                    attackSkill = SkillType.Nature,
                    range = attacker.beastAttackRange,
                    damageDice = attacker.beastAttackDice,
                    attackRollBonus = attacker.beastAttackBonus,
                    damageKind = DamageKind.Physical
                };
            }
            else
            {
                weapon = ItemDatabase.GetWeapon(attacker.character?.equippedMainWeaponId)
                         ?? attacker.fallbackWeapon;
            }
            if (weapon == null)
                return new AttackResult { message = $"У {attacker.displayName} нет оружия." };

            int distance = ManhattanDistance(attacker.gridPosition, target.gridPosition);
            // Для заклинаний дистанция игнорируется на этом уровне (карта сама решает).
            int reach = RangeInfo.Tiles(weapon.range);
            if (!isSpell && distance > reach)
                return new AttackResult { message = $"Цель слишком далеко ({distance} > {reach})." };

            // Метамагия: удвоение дальности — pending флаг.
            if (isSpell && attacker.character != null && attacker.character.GetFlag("metamagic_pending_double_range"))
            {
                reach *= 2;
                attacker.character.SetFlag("metamagic_pending_double_range", false);
            }

            // --- Бросок попадания ---
            var rollCtx = new RollContext
            {
                source = attacker,
                kind = isSpell ? RollKind.Spellcast : RollKind.Attack,
                skill = weapon.attackSkill
            };
            CombatTriggerBus.RaiseBeforeRoll(rollCtx);

            // Мистический туман — атаки в/из области с помехой.
            if (CombatZones.AttackObscuredByFog(attacker.gridPosition, target.gridPosition))
                rollCtx.disadvantage = true;

            // Линия видимости для дальних атак/заклинаний.
            bool needsLos = weapon.range != WeaponRange.Melee || isSpell;
            if (needsLos && currentMap != null
                && !Pathfinding.HasLineOfSight(currentMap, attacker.gridPosition, target.gridPosition))
            {
                return new AttackResult { message = $"{attacker.displayName}: нет линии видимости до {target.displayName}." };
            }

            // Полу-укрытие — помеха на дальнобойные атаки, если цель за клеткой-укрытием.
            if (needsLos && currentMap != null)
            {
                // Простейшая эвристика: если между атакующим и целью есть клетка HalfCover, дать помеху.
                if (IsHalfCoverBetween(attacker.gridPosition, target.gridPosition))
                    rollCtx.disadvantage = true;
            }

            RollOutcome roll;
            if (attacker.side == CombatSide.Player)
            {
                int bonus = attacker.stats.GetSkillBonus(weapon.attackSkill)
                          + weapon.attackRollBonus
                          + attacker.buffAttackBonusNextRoll;
                // Метамагия: +2 к результату
                if (isSpell && attacker.character != null && attacker.character.GetFlag("metamagic_pending_plus2"))
                {
                    bonus += 2;
                    attacker.character.SetFlag("metamagic_pending_plus2", false);
                }
                roll = RollDuality(bonus, rollCtx.advantage, rollCtx.disadvantage);

                // Тифлинг «Бесстрашный»: подмена Страха на Надежду, если игрок согласился заранее.
                if (roll.FearSide && rollCtx.convertFearToHope)
                {
                    // Меняем местами кости, чтобы Hope > Fear (визуально) и не начисляем Надежду в пул.
                    roll = new RollOutcome { isDuality = true, hopeDie = roll.fearDie, fearDie = roll.hopeDie, total = roll.total };
                    rollCtx.grantHopeOnConversion = false;
                }
            }
            else
            {
                int d = UnityEngine.Random.Range(1, 21);
                if (rollCtx.advantage) d = Mathf.Max(d, UnityEngine.Random.Range(1, 21));
                if (rollCtx.disadvantage) d = Mathf.Min(d, UnityEngine.Random.Range(1, 21));
                roll = new RollOutcome { isDuality = false, total = d + attacker.enemyAttackBonus, hopeDie = 0, fearDie = 0 };
            }
            // Сбрасываем разовые баффы на следующий бросок.
            attacker.buffAttackBonusNextRoll = 0;

            rollCtx.outcome = roll;
            CombatTriggerBus.RaiseAfterRoll(rollCtx);

            // Запоминаем бросок на юните — для классовых фич вроде Победи-свой-страх.
            attacker.lastRollOutcome = rollCtx.outcome;
            roll = rollCtx.outcome;

            // Для игрока — уведомляем, что враг атакует его юнита (Тифлинг/Пугающий и т.п.).
            if (attacker.side == CombatSide.Enemy && target.side == CombatSide.Player)
                CombatTriggerBus.RaiseEnemyAttackAgainstUnit(new AttackContext { attacker = attacker, target = target });

            bool hit = roll.total >= target.stats.evasion;
            var result = new AttackResult
            {
                attacker = attacker,
                target = target,
                roll = roll,
                requiredEvasion = target.stats.evasion,
                hit = hit
            };

            // Начисление ресурсов игроку (Надежда/Страх) — только если это была Дуальность
            // и подписчики не запретили начисление.
            if (roll.isDuality && rollCtx.grantHopeOnConversion == false && rollCtx.convertFearToHope)
            {
                // Ничего не начисляем — Тифлинг «Бесстрашный».
            }
            else if (roll.isDuality)
            {
                ApplyDualityResourceGain(roll, attacker);
            }

            if (!hit)
            {
                result.message = $"{attacker.displayName} промахивается по {target.displayName} " +
                                 $"({FormatRoll(roll)} < Уклонение {target.stats.evasion}).";
                Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });
                CombatTriggerBus.RaiseAttackResolved(new AttackContext { attacker = attacker, target = target, result = result });

                // Провал броска игроком — Человек «Адаптивность», Священник «Вера в лучшее».
                if (attacker.side == CombatSide.Player)
                {
                    RaceFeatureBus.OfferHumanAdaptability(attacker);
                    ClassFeaturesBus.OfferClericFaith(rollCtx);
                }
                return result;
            }

            // --- Урон ---
            string effectiveDice = damageDiceOverride ?? weapon.damageDice;
            bool rerollLow = attacker.character != null && attacker.character.GetFlag(PassiveEffectsRegistry.FlagNoWay);
            int raw = damageOverride >= 0
                    ? damageOverride
                    : RollDamage(effectiveDice, rerollLow);

            // Оффхенд-бонус "Малый кинжал" — +2 если цель вплотную.
            if (attacker.side == CombatSide.Player && !isSpell && !inBeastForm)
            {
                var off = ItemDatabase.GetWeapon(attacker.character?.equippedOffhandId);
                if (off != null && off.itemId == "weapon_small_dagger_off" && distance <= 1)
                    raw += 2;
            }

            // ПЛУТ: Подлая атака (+N × d6) — считает, что есть преимущество (buff уже сброшен) ИЛИ союзник вплотную.
            if (attacker.side == CombatSide.Player && !isSpell && damageOverride < 0)
            {
                int sneakDice = ClassFeaturesBus.GetSneakAttackBonusDice(attacker, target);
                for (int i = 0; i < sneakDice; i++) raw += UnityEngine.Random.Range(1, 7);
            }

            // ВОИН-Берсерк: +заряды к урону.
            int berserker = ClassFeaturesBus.GetBerserkerDamageBonus(attacker);
            if (berserker > 0) raw += berserker;

            // МБИ: стойка (Устойчивая — доп. кость и отброс наименьшей).
            {
                int rawBefore = raw;
                ClassFeaturesBus.ApplyStanceModifiers(attacker, ref raw, effectiveDice);
                if (raw != rawBefore)
                    Debug.Log($"[Combat] Стойка модифицировала урон {rawBefore} → {raw}");
            }

            // Метамагия: удвоить одну кость урона — просто добавим ~половину сырого урона (грубый эквивалент).
            if (isSpell && attacker.character != null && attacker.character.GetFlag("metamagic_pending_double_die"))
            {
                raw += Mathf.Max(1, raw / 2);
                attacker.character.SetFlag("metamagic_pending_double_die", false);
            }

            // ПЛУТ-Отравитель: если есть жетон — можно применить эффект (авто, если жетоны есть, самый безопасный «плющ пиявки» +1d6).
            if (attacker.character?.subclassId == "rogue_poisoners_guild" && ClassFeaturesBus.GetPoisonTokens(attacker) > 0)
            {
                ClassFeaturesBus.SpendPoisonToken(attacker);
                raw += UnityEngine.Random.Range(1, 7);
            }

            // Уязвимость — цель получает +50% урона.
            if (target.HasEffectId("vulnerable_temp") || target.HasEffectId("vulnerable_next_attack"))
                raw = Mathf.RoundToInt(raw * 1.5f);

            // Броненосец (Друид): сопротивление физическому урону.
            if (target.character != null && target.character.GetFlag("armadillo_resist_physical")
                && weapon.damageKind == DamageKind.Physical)
                raw = Mathf.RoundToInt(raw * 0.5f);

            // Разовая Уязвимость — снимаем.
            target.RemoveEffectById("vulnerable_next_attack");

            // Событие «союзник получает урон» — для карты «Я твой щит».
            AllyDamageContext allyCtx = null;
            if (target.side == CombatSide.Player && attacker != null && attacker.side == CombatSide.Enemy)
            {
                allyCtx = new AllyDamageContext
                {
                    ally = target,
                    attacker = attacker,
                    rawDamage = raw
                };
                CombatTriggerBus.RaiseAllyIncomingDamage(allyCtx);
                // Если защитник взял урон на себя (Я твой щит) — оригинальная цель урон не получает.
                if (allyCtx.tookOverByPlayerUnit)
                {
                    result.rawDamage = raw;
                    result.message = $"{attacker.displayName} → {target.displayName}: удар перехвачен {allyCtx.tookOverBy.displayName}.";
                    Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });
                    CombatTriggerBus.RaiseAttackResolved(new AttackContext { attacker = attacker, target = target, result = result });
                    return result;
                }
            }

            // Триггер «входящий урон» — карты «Вернуться в строй», «Я твой щит» и т.д.
            var inc = new IncomingDamageContext
            {
                target = target,
                attacker = attacker,
                rawDamage = raw
            };
            CombatTriggerBus.RaiseIncomingDamage(inc);

            if (inc.cancelled)
            {
                result.rawDamage = 0;
                result.message = $"{attacker.displayName} → {target.displayName}: удар отражён/уклонён.";
                Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });
                CombatTriggerBus.RaiseAttackResolved(new AttackContext { attacker = attacker, target = target, result = result });
                return result;
            }

            var effectiveTarget = inc.absorbedByAlly && inc.absorbingAlly != null ? inc.absorbingAlly : target;
            // Спящий, получивший урон, просыпается.
            if (effectiveTarget.HasEffectId("asleep") && inc.rawDamage > 0)
                effectiveTarget.RemoveEffectById("asleep");
            var dmg = effectiveTarget.stats.TakeDamage(inc.rawDamage, inc.bypassArmor);
            result.rawDamage = inc.rawDamage;
            result.damage = dmg;

            string dmgText = dmg.blockedByThreshold
                ? $"броня удержала удар (сырой урон {inc.rawDamage} < DT {effectiveTarget.stats.damageThreshold})."
                : $"нанесено {dmg.hpDamageDealt} урона" +
                  (dmg.armorSlotBroken ? " (сломана ячейка брони)" : "") +
                  (dmg.healthSlotsBroken > 0 ? $", шкал здоровья сломано: {dmg.healthSlotsBroken}" : "");
            string absorbLabel = effectiveTarget == target ? "" : $" [перехватил {effectiveTarget.displayName}]";
            result.message = $"{attacker.displayName} попадает по {target.displayName}{absorbLabel}: {dmgText}";
            Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });

            // Триггер «нанесён урон»
            CombatTriggerBus.RaiseDamageDealt(new DamageDealtContext
            {
                attacker = attacker,
                target = effectiveTarget,
                result = dmg,
                wasSeriousDamage = dmg.healthSlotsBroken > 0
            });

            // Импульс: если враг попал по игроку — +1 Страх.
            if (attacker.side == CombatSide.Enemy && effectiveTarget.side == CombatSide.Player)
            {
                fearPool++;
                OnResourcesChanged?.Invoke();
            }

            if (!effectiveTarget.stats.IsAlive)
            {
                effectiveTarget.MarkDead();
                Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"{effectiveTarget.displayName} повержен." });
                CheckCombatEnd();
            }

            // МБИ: Быстрая стойка — предложить атаковать доп. цель за Выносливость.
            if (attacker.side == CombatSide.Player && result.hit
                && ClassFeaturesBus.GetStance(attacker) == "fast"
                && attacker.stats.currentStamina >= 1
                && !attacker.character.GetFlag("fast_stance_used_this_attack"))
            {
                // Ищем ближайшую другую цель в дистанции атаки.
                var extra = allUnits
                    .Where(x => x != target && x.side == CombatSide.Enemy && !x.IsDead
                             && ManhattanDistance(attacker.gridPosition, x.gridPosition) <= reach)
                    .OrderBy(x => ManhattanDistance(attacker.gridPosition, x.gridPosition))
                    .FirstOrDefault();
                if (extra != null)
                {
                    var atkRef = attacker;
                    var tgtRef = extra;
                    AbilityConfirmDialog.Show(
                        title: "МБИ: Быстрая стойка",
                        description: $"Атаковать доп. цель {tgtRef.displayName}?",
                        resources: $"Стоимость: 1 Выносливость (у вас {atkRef.stats.currentStamina}).",
                        yes: () =>
                        {
                            atkRef.stats.SpendStamina(1);
                            atkRef.character.SetFlag("fast_stance_used_this_attack", true);
                            PerformAttack(atkRef, tgtRef);
                            atkRef.character.SetFlag("fast_stance_used_this_attack", false);
                        });
                }
            }

            // МБИ: Захватная стойка — при успешной атаке предложить Обездвижить за Выносливость.
            if (attacker.side == CombatSide.Player && result.hit
                && ClassFeaturesBus.GetStance(attacker) == "grapple"
                && attacker.stats.currentStamina >= 1
                && !effectiveTarget.HasEffectId("immobilized_temp"))
            {
                var t = effectiveTarget;
                AbilityConfirmDialog.Show(
                    title: "МБИ: Захватная стойка",
                    description: $"Обездвижить {t.displayName}?",
                    resources: $"Стоимость: 1 Выносливость (у вас {attacker.stats.currentStamina}).",
                    yes: () =>
                    {
                        attacker.stats.SpendStamina(1);
                        t.ApplyEffect(new StatusEffect
                        {
                            effectId = "immobilized_temp",
                            displayName = "Обездвижен (захват)",
                            effectType = EffectType.Immobilized,
                            remainingDuration = 2
                        });
                    });
            }

            CombatTriggerBus.RaiseAttackResolved(new AttackContext { attacker = attacker, target = target, result = result });

            // Каратель: Месть — если ВРАГ вплотную попал по нашему юниту.
            if (attacker.side == CombatSide.Enemy && target.side == CombatSide.Player && result.hit
                && ManhattanDistance(attacker.gridPosition, target.gridPosition) <= 1)
            {
                ClassFeaturesBus.OfferPunisherVengeance(target, attacker, result);
            }

            return result;
        }

        /// <summary>Бросок Заклинания — короткий вариант без урона. Возвращает Дуальность и флаг успеха.</summary>
        public SpellCheckResult PerformSpellCheck(CombatUnit caster, int difficultyClass)
        {
            RollOutcome roll;
            if (caster.side == CombatSide.Player)
                roll = RollDuality(caster.stats.GetSkillBonus(SkillType.Magic));
            else
                roll = new RollOutcome { isDuality = false, total = UnityEngine.Random.Range(1, 21) + caster.enemyAttackBonus };
            if (roll.isDuality) ApplyDualityResourceGain(roll, caster);
            return new SpellCheckResult { roll = roll, success = roll.total >= difficultyClass, dc = difficultyClass };
        }

        /// <summary>Мистический туман 5×5 клеток с центром на кастере.</summary>
        public void SpawnMysticFog(Vector2Int center)
        {
            CombatZones.Add(new CombatZone
            {
                kind = ZoneKind.MysticFog,
                center = center,
                radius = 2,
                roundsLeft = 999
            });
            Log(new CombatLogEntry { kind = CombatLogKind.System,
                message = $"Мистический туман 5×5 клеток с центром в {center}. Атаки в/из области — с помехой." });
        }

        /// <summary>Короткая Передышка — восстанавливает Выносливость юнита.</summary>
        public ShortRestResult PerformShortRest(CombatUnit unit)
        {
            var roll = unit.side == CombatSide.Player
                ? RollDuality(0)
                : new RollOutcome { total = UnityEngine.Random.Range(1, 21), isDuality = false };

            unit.stats.RestoreStamina(1);
            var res = new ShortRestResult { unit = unit, roll = roll };

            if (roll.isDuality)
            {
                if (roll.HopeSide)
                {
                    // Не тратит ход, но и Надежду не даёт (см. ГДД).
                    res.consumesAction = false;
                    res.message = $"{unit.displayName} переводит дыхание — Выносливость +1, ход остаётся.";
                }
                else
                {
                    res.consumesAction = true;
                    fearPool++;
                    OnResourcesChanged?.Invoke();
                    res.message = $"{unit.displayName} тратит выносливость, но передаёт ход (враг получает +1 Страх).";
                }
            }
            else
            {
                res.consumesAction = true;
                res.message = $"{unit.displayName} переводит дыхание.";
            }

            Log(new CombatLogEntry { kind = CombatLogKind.System, message = res.message });
            return res;
        }

        /// <summary>Игрок отмечает завершение действий юнита.</summary>
        public void FinishUnitTurn(CombatUnit unit, bool successfulHopeRoll)
        {
            if (unit == null) return;
            activatedThisRound.Add(unit.unitId);
            unit.hasMovedThisTurn = false;
            unit.ClearOneShotBuffs();
            selectedUnit = null;
            currentUnitMoveBudget = 0;

            consecutiveActivationsThisPass++;

            // Игрок сохраняет ход, если успех с Надеждой; иначе передаёт.
            bool keepSide = activeSide == CombatSide.Player && successfulHopeRoll;
            if (!keepSide) PassTurnToOtherSide();
            else Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"{unit.displayName} — успех с Надеждой, ход продолжается." });
        }

        private void PassTurnToOtherSide()
        {
            int allowedActivations = consecutiveActivationsThisPass;
            var previousSide = activeSide;
            activeSide = activeSide == CombatSide.Player ? CombatSide.Enemy : CombatSide.Player;
            consecutiveActivationsThisPass = 0;

            // Проверяем, все ли на этой (уходящей) стороне уже активированы — если да, сбрасываем счётчик раунда для неё.
            var thisSideUnits = allUnits.Where(u => u.side == previousSide && !u.IsDead).ToList();
            if (thisSideUnits.All(u => activatedThisRound.Contains(u.unitId)))
                foreach (var u in thisSideUnits) activatedThisRound.Remove(u.unitId);

            OnTurnPassedToSide?.Invoke(activeSide);
            Log(new CombatLogEntry { kind = CombatLogKind.System,
                message = $"Ход переходит: {activeSide}. Разрешено активаций: {allowedActivations}." });

            if (activeSide == CombatSide.Enemy)
                RunAI(allowedActivations);
        }

        /// <summary>Простой AI: активирует до N врагов подряд, каждый идёт к ближайшей цели и атакует.</summary>
        private void RunAI(int activations)
        {
            for (int i = 0; i < activations; i++)
            {
                var u = PickAIActivation();
                if (u == null) break;
                OnUnitActivated?.Invoke(u);
                CombatTriggerBus.RaiseUnitActivated(u);

                var target = allUnits
                    .Where(x => x.side == CombatSide.Player && !x.IsDead)
                    .OrderBy(x => ManhattanDistance(x.gridPosition, u.gridPosition))
                    .FirstOrDefault();

                if (target != null && !EnemyAI.TryPerformSpecialAction(u, target, this))
                {
                    // AI пытается подойти к цели по BFS, потом атаковать.
                    MoveUnitTowards(u, target.gridPosition, baseMovementTiles);
                    PerformAttack(u, target);
                }

                activatedThisRound.Add(u.unitId);
                if (!isCombatActive) return;
            }

            // После действий AI ход возвращается игроку.
            var previousSide = activeSide;
            activeSide = CombatSide.Player;
            consecutiveActivationsThisPass = 0;

            var enemies = allUnits.Where(x => x.side == previousSide && !x.IsDead).ToList();
            if (enemies.All(x => activatedThisRound.Contains(x.unitId)))
                foreach (var x in enemies) activatedThisRound.Remove(x.unitId);

            OnTurnPassedToSide?.Invoke(activeSide);
        }

        // ============================================================
        //  Броски и ресурсы
        // ============================================================

        public RollOutcome RollDuality(int bonus, bool advantage = false, bool disadvantage = false)
        {
            int hope = UnityEngine.Random.Range(1, 13);
            int fear = UnityEngine.Random.Range(1, 13);
            int mod = 0;
            if (advantage && !disadvantage) mod += UnityEngine.Random.Range(1, 7);
            else if (disadvantage && !advantage) mod -= UnityEngine.Random.Range(1, 7);
            return new RollOutcome
            {
                isDuality = true,
                hopeDie = hope,
                fearDie = fear,
                total = hope + fear + bonus + mod
            };
        }

        /// <summary>После броска Дуальности — начисляем Надежду или Страх согласно ГДД.</summary>
        public void ApplyDualityResourceGain(RollOutcome roll, CombatUnit source)
        {
            if (!roll.isDuality) return;
            if (roll.hopeDie == roll.fearDie)
            {
                // Критический успех: +1 Надежды и +1 Выносливости источнику.
                hopePool++;
                source?.stats.RestoreStamina(1);
                Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"Крит! +1 Надежда, +1 Выносливость {source?.displayName}." });
            }
            else if (roll.hopeDie > roll.fearDie)
            {
                hopePool++;
            }
            else
            {
                fearPool++;
            }
            OnResourcesChanged?.Invoke();
        }

        public bool TrySpendHope(int amount = 1)
        {
            if (hopePool < amount) return false;
            hopePool -= amount;
            OnResourcesChanged?.Invoke();
            return true;
        }

        public bool TrySpendFear(int amount = 1)
        {
            if (fearPool < amount) return false;
            fearPool -= amount;
            OnResourcesChanged?.Invoke();
            return true;
        }

        public void RefundHope(int amount = 1)
        {
            hopePool += amount;
            OnResourcesChanged?.Invoke();
        }

        // ============================================================
        //  Утилиты
        // ============================================================

        public int RollDamage(string dice, bool rerollLowOnes = false)
        {
            // Формат: [N]d<sides>[+X].
            if (string.IsNullOrEmpty(dice)) return 0;
            dice = dice.ToLower().Replace(" ", "");
            int plus = 0;
            int plusIdx = dice.IndexOf('+');
            if (plusIdx > 0)
            {
                int.TryParse(dice.Substring(plusIdx + 1), out plus);
                dice = dice.Substring(0, plusIdx);
            }
            int dIdx = dice.IndexOf('d');
            if (dIdx < 0) { int.TryParse(dice, out var flat); return flat + plus; }
            int count = 1;
            if (dIdx > 0) int.TryParse(dice.Substring(0, dIdx), out count);
            int.TryParse(dice.Substring(dIdx + 1), out var sides);
            if (sides <= 0) return plus;
            int sum = 0;
            int n = Mathf.Max(1, count);
            for (int i = 0; i < n; i++)
            {
                int r = UnityEngine.Random.Range(1, sides + 1);
                // Карта «Так не пойдёт»: 1 и 2 не выпадают — перекатываем.
                if (rerollLowOnes)
                {
                    int safety = 5;
                    while ((r == 1 || r == 2) && safety-- > 0)
                        r = UnityEngine.Random.Range(1, sides + 1);
                }
                sum += r;
            }
            return sum + plus;
        }

        /// <summary>Клеточная дистанция ГДД — Chebyshev, диагональ = 1.</summary>
        public static int ManhattanDistance(Vector2Int a, Vector2Int b)
            => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>
        /// Перемещает юнита к указанной клетке в пределах бюджета движения.
        /// Использует BFS; если цель недостижима — идёт как можно ближе.
        /// Возвращает список пройденных клеток (для анимации).
        /// </summary>
        public List<Vector2Int> MoveUnitTowards(CombatUnit unit, Vector2Int desired, int budget)
        {
            var empty = new List<Vector2Int>();
            if (unit == null || currentMap == null) return empty;
            if (unit.HasEffectId("immobilized_temp"))
            {
                Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"{unit.displayName} обездвижен и не может двигаться." });
                return empty;
            }

            // Полный путь до цели.
            var full = Pathfinding.FindPath(currentMap, unit.gridPosition, desired, 999, allUnits, unit);
            if (full.Count == 0)
            {
                // Цель занята/недостижима — ищем ближайшую свободную соседнюю.
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
                                          new Vector2Int(1,1), new Vector2Int(1,-1),
                                          new Vector2Int(-1,1), new Vector2Int(-1,-1) })
                {
                    var alt = desired + d;
                    if (!currentMap.IsWalkable(alt)) continue;
                    if (allUnits.Exists(x => !x.IsDead && x != unit && x.gridPosition == alt)) continue;
                    full = Pathfinding.FindPath(currentMap, unit.gridPosition, alt, 999, allUnits, unit);
                    if (full.Count > 0) break;
                }
            }
            if (full.Count == 0) return empty;

            // Обрезаем путь по бюджету, учитывая difficult terrain.
            var walked = new List<Vector2Int>();
            int spent = 0;
            foreach (var step in full)
            {
                int cost = currentMap.IsDifficultTerrain(step) ? 2 : 1;
                if (spent + cost > budget) break;
                spent += cost;
                walked.Add(step);
            }
            if (walked.Count == 0) return empty;
            unit.gridPosition = walked[walked.Count - 1];
            return walked;
        }

        /// <summary>Проверка полу-укрытия: есть ли на прямой между from/to клетка HalfCover.</summary>
        private bool IsHalfCoverBetween(Vector2Int from, Vector2Int to)
        {
            if (currentMap == null) return false;
            int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                if (!(x0 == from.x && y0 == from.y) && !(x0 == to.x && y0 == to.y))
                {
                    if (currentMap.GetTile(new Vector2Int(x0, y0)) == TileKind.HalfCover)
                        return true;
                }
                if (x0 == x1 && y0 == y1) return false;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx)  { err += dx; y0 += sy; }
            }
        }

        private void CheckCombatEnd()
        {
            bool anyPlayer = allUnits.Any(u => u.side == CombatSide.Player && !u.IsDead);
            bool anyEnemy  = allUnits.Any(u => u.side == CombatSide.Enemy  && !u.IsDead);
            if (!anyEnemy) EndCombat(true);
            else if (!anyPlayer) EndCombat(false);
        }

        private string FormatRoll(RollOutcome r)
        {
            if (r.isDuality)
                return $"[H{r.hopeDie} F{r.fearDie}] Итог {r.total}";
            return $"d20 → {r.total}";
        }

        private void Log(CombatLogEntry entry)
        {
            OnCombatEvent?.Invoke(entry);
            Debug.Log($"[Combat/{entry.kind}] {entry.message}");
        }

        // ============================================================
        //  Создание юнитов
        // ============================================================

        private CombatUnit CreateUnitFromCharacter(PlayerCharacter c, CombatSide side)
        {
            return new CombatUnit
            {
                unitId = c.characterId ?? "player",
                displayName = c.displayName ?? c.playerName ?? "Игрок",
                side = side,
                character = c,
                stats = c.stats,
                fallbackWeapon = new WeaponDefinition
                {
                    itemId = "unarmed",
                    displayName = "Кулаки",
                    attackSkill = SkillType.BodyPower,
                    range = WeaponRange.Melee,
                    damageDice = "d4"
                }
            };
        }

        private CombatUnit CreateUnitFromCompanion(CompanionData c)
        {
            return new CombatUnit
            {
                unitId = c.companionId,
                displayName = c.displayName,
                side = CombatSide.Player,
                stats = c.stats ?? new CharacterStats { evasion = 10, maxHealthSlots = 6, hpPerSlot = 6, currentSlotHp = 6, maxStamina = 1, currentStamina = 1 },
                fallbackWeapon = new WeaponDefinition
                {
                    itemId = "companion_default",
                    displayName = "Оружие спутника",
                    attackSkill = SkillType.BodyPower,
                    range = WeaponRange.Melee,
                    damageDice = "d6+1"
                }
            };
        }

        private CombatUnit CreateUnitFromEnemy(EnemyDefinition e)
        {
            var stats = new CharacterStats
            {
                maxHealthSlots  = e.healthSlots,
                usedHealthSlots = 0,
                hpPerSlot       = e.hpPerSlot,
                currentSlotHp   = e.hpPerSlot,
                maxArmorSlots   = e.armorSlots,
                damageThreshold = e.damageThreshold,
                armorRating     = e.armorRating,
                evasion         = e.evasion,
                maxStamina      = e.stamina,
                currentStamina  = e.stamina,
                level = e.level
            };
            return new CombatUnit
            {
                unitId = $"{e.enemyId}_{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                displayName = e.displayName,
                side = CombatSide.Enemy,
                stats = stats,
                gridPosition = e.spawnPosition,
                enemyAttackBonus = e.attackBonus,
                enemyRole = e.role,
                enemyTags = new List<string>(e.tags ?? new List<string>()),
                fallbackWeapon = new WeaponDefinition
                {
                    itemId = $"{e.enemyId}_weapon",
                    displayName = string.IsNullOrEmpty(e.weaponName) ? "Оружие" : e.weaponName,
                    attackSkill = SkillType.BodyPower,
                    range = e.weaponRange,
                    damageDice = e.weaponDamage
                }
            };
        }

        // ============================================================
        //  Сохранение (упрощённо)
        // ============================================================

        public string OnSave() => "";
        public void OnLoad(string json) { }
    }

    // ================================================================
    //   Типы данных
    // ================================================================

    public enum CombatSide { Player, Enemy, Neutral }

    public enum EnemyRole
    {
        Minion,      // Приспешник
        Standard,    // Рядовой
        Skirmisher,  // Скрытный
        Bruiser,     // Громила
        Leader,      // Лидер
        Support,     // Поддержка
        Solo         // Одиночка
    }

    [Serializable]
    public class CombatUnit
    {
        public string unitId;
        public string displayName;
        public CombatSide side;

        // Позиция и общие флаги.
        public Vector2Int gridPosition;
        public bool hasMovedThisTurn;
        private bool dead;
        public bool IsDead => dead || (stats != null && !stats.IsAlive);
        public void MarkDead() => dead = true;

        // Ссылка на характеристики.
        [NonSerialized] public CharacterStats stats;

        // Для игрока — прямой доступ к персонажу (инвентарь, домены, карты).
        [NonSerialized] public PlayerCharacter character;

        // Оружие "по умолчанию" (для юнитов без equippedMainWeaponId).
        [NonSerialized] public WeaponDefinition fallbackWeapon;

        // Данные врага.
        public int enemyAttackBonus;
        public EnemyRole enemyRole;
        public List<string> enemyTags = new();

        // Разовые баффы/модификаторы боя.
        [NonSerialized] public int buffAttackBonusNextRoll;
        [NonSerialized] public int movementBudgetOverride;   // 0 = использовать base
        [NonSerialized] public bool ignoreHazardsThisTurn;
        [NonSerialized] public ElementalGuardianKind elementalGuardian;

        // Последний бросок (нужен классовым фичам вроде "Победи свой страх" в Mage-War).
        [NonSerialized] public RollOutcome lastRollOutcome;

        // Друид — характеристики звериной формы (перекрывают оружие).
        [NonSerialized] public int beastAttackBonus;
        [NonSerialized] public string beastAttackDice;
        [NonSerialized] public Items.WeaponRange beastAttackRange;

        // -------- Работа с эффектами --------
        [NonSerialized] private List<StatusEffect> effects;
        private List<StatusEffect> Effects => effects ??= new List<StatusEffect>();

        public void ApplyEffect(StatusEffect e)
        {
            Effects.Add(e);
        }
        public bool HasEffectId(string id) => Effects.Exists(e => e.effectId == id);
        public void RemoveEffectById(string id) => Effects.RemoveAll(e => e.effectId == id);

        public void SetElementalGuardian(ElementalGuardianKind kind)
        {
            elementalGuardian = kind;
        }

        public void ClearOneShotBuffs()
        {
            buffAttackBonusNextRoll = 0;
            movementBudgetOverride = 0;
            ignoreHazardsThisTurn = false;
        }
    }

    public struct SpellCheckResult
    {
        public RollOutcome roll;
        public bool success;
        public int dc;
    }

    [Serializable]
    public class CombatEncounter
    {
        public string encounterId;
        public string environment;
        /// <summary>id арены из StreamingAssets/Arenas. Если пусто — используется encounterId, потом дефолт.</summary>
        public string arenaId;
        public List<EnemyDefinition> enemies = new();
    }

    [Serializable]
    public class EnemyDefinition
    {
        public string enemyId;
        public string displayName;
        public EnemyRole role = EnemyRole.Standard;
        public int level = 1;

        // Защита.
        public int evasion = 10;         // «Сложность» по ГДД
        public int damageThreshold = 5;
        public int armorRating = 0;
        public int armorSlots = 0;

        // Здоровье.
        public int healthSlots = 4;
        public int hpPerSlot = 5;

        // Ресурсы.
        public int stamina = 1;

        // Атака.
        public int attackBonus = 0;
        public string weaponName = "Оружие";
        public WeaponRange weaponRange = WeaponRange.Melee;
        public string weaponDamage = "d6";

        // Позиция.
        public Vector2Int spawnPosition;

        // Теги для спец-логики (например "morale_lust", "guard").
        public List<string> tags = new();
    }

    public struct RollOutcome
    {
        public bool isDuality;
        public int hopeDie;
        public int fearDie;
        public int total;
        public bool HopeSide => isDuality && hopeDie > fearDie;
        public bool FearSide => isDuality && fearDie > hopeDie;
        public bool Crit     => isDuality && hopeDie == fearDie;
    }

    public struct AttackResult
    {
        public CombatUnit attacker;
        public CombatUnit target;
        public RollOutcome roll;
        public int requiredEvasion;
        public bool hit;
        public int rawDamage;
        public DamageResult damage;
        public string message;
    }

    public struct ShortRestResult
    {
        public CombatUnit unit;
        public RollOutcome roll;
        public bool consumesAction;
        public string message;
    }

    public enum CombatLogKind { System, Attack, Ability, Damage, Movement }

    public struct CombatLogEntry
    {
        public CombatLogKind kind;
        public string message;
    }
}
