using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.Core;
using RPG.Character;
using RPG.Domains;
using RPG.Items;
using RPG.Companion;

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

            // --- Игрок ---
            var player = CharacterCreation.Instance?.Character;
            if (player != null)
            {
                var u = CreateUnitFromCharacter(player, CombatSide.Player);
                u.gridPosition = new Vector2Int(2, gridHeight / 2);
                allUnits.Add(u);
            }

            // --- Компаньоны (пока не участвуют в r2, но система готова) ---
            var companions = CompanionManager.Instance?.GetPartyMembers();
            if (companions != null)
            {
                int i = 0;
                foreach (var c in companions)
                {
                    var u = CreateUnitFromCompanion(c);
                    u.gridPosition = new Vector2Int(1, gridHeight / 2 + (i - 1));
                    allUnits.Add(u);
                    i++;
                }
            }

            // --- Враги ---
            foreach (var e in encounter.enemies)
            {
                var u = CreateUnitFromEnemy(e);
                allUnits.Add(u);
            }

            // Стартовые ресурсы каждого юнита.
            foreach (var u in allUnits)
            {
                u.stats.ResetArmorAtCombatStart();
                u.stats.ResetStaminaAtCombatStart();
            }

            isCombatActive = true;
            GameManager.Instance.SetGameState(GameState.Combat);
            GameManager.Instance.EventBus.RaiseCombatStarted();
            OnCombatStart?.Invoke();
            Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"Начался бой: {encounter.encounterId}." });
            OnTurnPassedToSide?.Invoke(activeSide);
        }

        public void EndCombat(bool playerWon)
        {
            if (!isCombatActive) return;
            isCombatActive = false;
            GameManager.Instance.EventBus.RaiseCombatEnded(playerWon);
            OnCombatEnd?.Invoke(playerWon);
            GameManager.Instance.SetGameState(GameState.Dialogue);
            Log(new CombatLogEntry { kind = CombatLogKind.System, message = playerWon ? "Победа." : "Поражение." });
        }

        // ============================================================
        //  Активация юнитов
        // ============================================================

        /// <summary>Игрок вручную активирует своего юнита (клик по портрету).</summary>
        public bool ActivatePlayerUnit(string unitId)
        {
            if (!isCombatActive || activeSide != CombatSide.Player) return false;
            var u = allUnits.Find(x => x.unitId == unitId && x.side == CombatSide.Player && !x.IsDead);
            if (u == null) return false;
            if (activatedThisRound.Contains(u.unitId)) return false;
            selectedUnit = u;
            OnUnitActivated?.Invoke(u);
            return true;
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
        public AttackResult PerformAttack(CombatUnit attacker, CombatUnit target)
        {
            if (attacker == null || target == null || target.IsDead)
                return new AttackResult { message = "Некорректная цель." };

            var weapon = ItemDatabase.GetWeapon(attacker.character?.equippedMainWeaponId)
                         ?? attacker.fallbackWeapon;
            if (weapon == null)
                return new AttackResult { message = $"У {attacker.displayName} нет оружия." };

            int distance = ManhattanDistance(attacker.gridPosition, target.gridPosition);
            int reach = RangeInfo.Tiles(weapon.range);
            if (distance > reach)
                return new AttackResult { message = $"Цель слишком далеко ({distance} > {reach})." };

            // --- Бросок попадания ---
            RollOutcome roll;
            if (attacker.side == CombatSide.Player)
            {
                roll = RollDuality(attacker.stats.GetSkillBonus(weapon.attackSkill) + weapon.attackRollBonus);
            }
            else
            {
                // Враги — d20.
                int d = UnityEngine.Random.Range(1, 21);
                roll = new RollOutcome
                {
                    isDuality = false,
                    total = d + attacker.enemyAttackBonus,
                    hopeDie = 0, fearDie = 0
                };
            }

            bool hit = roll.total >= target.stats.evasion;
            var result = new AttackResult
            {
                attacker = attacker,
                target = target,
                roll = roll,
                requiredEvasion = target.stats.evasion,
                hit = hit
            };

            if (roll.isDuality)
                ApplyDualityResourceGain(roll, attacker);

            if (!hit)
            {
                result.message = $"{attacker.displayName} промахивается по {target.displayName} " +
                                 $"({FormatRoll(roll)} < Уклонение {target.stats.evasion}).";
                Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });
                return result;
            }

            // --- Урон ---
            int raw = RollDamage(weapon.damageDice);
            // Оффхенд-бонус "Малый кинжал" — +2 если цель вплотную.
            if (attacker.side == CombatSide.Player)
            {
                var off = ItemDatabase.GetWeapon(attacker.character?.equippedOffhandId);
                if (off != null && off.itemId == "weapon_small_dagger_off" && distance <= 1)
                    raw += 2;
            }
            var dmg = target.stats.TakeDamage(raw);
            result.rawDamage = raw;
            result.damage = dmg;

            string dmgText = dmg.blockedByThreshold
                ? $"броня удержала удар (сырой урон {raw} < DT {target.stats.damageThreshold})."
                : $"нанесено {dmg.hpDamageDealt} урона" +
                  (dmg.armorSlotBroken ? " (сломана ячейка брони)" : "") +
                  (dmg.healthSlotsBroken > 0 ? $", шкал здоровья сломано: {dmg.healthSlotsBroken}" : "");
            result.message = $"{attacker.displayName} попадает по {target.displayName}: {dmgText}";
            Log(new CombatLogEntry { kind = CombatLogKind.Attack, message = result.message });

            // Импульс: если враг попал по игроку — +1 Страх.
            if (attacker.side == CombatSide.Enemy && target.side == CombatSide.Player)
            {
                fearPool++;
                OnResourcesChanged?.Invoke();
            }

            if (!target.stats.IsAlive)
            {
                target.MarkDead();
                Log(new CombatLogEntry { kind = CombatLogKind.System, message = $"{target.displayName} повержен." });
                CheckCombatEnd();
            }

            return result;
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
            selectedUnit = null;

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

                var target = allUnits
                    .Where(x => x.side == CombatSide.Player && !x.IsDead)
                    .OrderBy(x => ManhattanDistance(x.gridPosition, u.gridPosition))
                    .FirstOrDefault();

                if (target != null)
                {
                    // Двинемся к цели в пределах baseMovementTiles.
                    var toTarget = target.gridPosition - u.gridPosition;
                    int stepX = Mathf.Clamp(toTarget.x, -baseMovementTiles, baseMovementTiles);
                    int stepY = Mathf.Clamp(toTarget.y, -baseMovementTiles, baseMovementTiles);
                    int budget = baseMovementTiles;
                    int dx = Mathf.Abs(stepX), dy = Mathf.Abs(stepY);
                    if (dx + dy > budget)
                    {
                        // Урезаем шаг, чтобы уложиться в бюджет.
                        float k = (float)budget / (dx + dy);
                        stepX = Mathf.RoundToInt(stepX * k);
                        stepY = Mathf.RoundToInt(stepY * k);
                    }
                    u.gridPosition += new Vector2Int(stepX, stepY);

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

        public int RollDamage(string dice)
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
            for (int i = 0; i < Mathf.Max(1, count); i++) sum += UnityEngine.Random.Range(1, sides + 1);
            return sum + plus;
        }

        public static int ManhattanDistance(Vector2Int a, Vector2Int b)
            => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

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
    }

    [Serializable]
    public class CombatEncounter
    {
        public string encounterId;
        public string environment;
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
