using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;
using RPG.Character;

namespace RPG.Combat
{
    /// <summary>
    /// Тактическая пошаговая боевая система в стиле Baldur's Gate / Divinity.
    /// Управляет ходами, действиями, AI врагов.
    /// </summary>
    public class CombatManager : MonoBehaviour, ISaveable
    {
        public static CombatManager Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private int gridWidth = 20;
        [SerializeField] private int gridHeight = 20;
        [SerializeField] private float tileSize = 1f;

        [Header("Combat Settings")]
        [SerializeField] private float actionPointBase = 4f;
        [SerializeField] private float movementCostPerTile = 1f;
        [SerializeField] private float attackActionCost = 2f;
        [SerializeField] private float spellActionCost = 2f;

        // Состояние боя
        private bool isCombatActive;
        private List<CombatUnit> allUnits = new();
        private List<CombatUnit> turnOrder = new();
        private int currentTurnIndex = -1;
        private CombatUnit currentUnit;
        private int roundNumber;

        public bool IsCombatActive => isCombatActive;
        public CombatUnit CurrentUnit => currentUnit;
        public int RoundNumber => roundNumber;

        public string SaveKey => "CombatManager";

        public event Action OnCombatStart;
        public event Action<bool> OnCombatEnd; // true = победа игрока
        public event Action<CombatUnit> OnTurnStart;
        public event Action<CombatUnit> OnTurnEnd;
        public event Action<CombatAction, CombatActionResult> OnActionPerformed;
        public event Action<CombatUnit> OnUnitDied;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            GameManager.Instance.SaveManager.RegisterSaveable(this);
        }

        #region Combat Lifecycle

        public void StartCombat(CombatEncounter encounter)
        {
            if (isCombatActive)
            {
                Debug.LogWarning("[Combat] Combat already active");
                return;
            }

            allUnits.Clear();
            turnOrder.Clear();
            roundNumber = 0;

            // Добавляем игрока и партию
            var playerChar = CharacterCreation.Instance?.Character;
            if (playerChar != null)
            {
                var playerUnit = CreateUnitFromCharacter(playerChar, true, false);
                allUnits.Add(playerUnit);
            }

            // Добавляем компаньонов
            var companions = Companion.CompanionManager.Instance.GetPartyMembers();
            foreach (var companion in companions)
            {
                var unit = CreateUnitFromCompanion(companion);
                allUnits.Add(unit);
            }

            // Добавляем врагов
            foreach (var enemy in encounter.enemies)
            {
                var unit = CreateUnitFromEnemy(enemy);
                allUnits.Add(unit);
            }

            // Определяем порядок ходов (инициатива)
            DetermineTurnOrder();

            isCombatActive = true;
            GameManager.Instance.SetGameState(GameState.Combat);
            GameManager.Instance.EventBus.RaiseCombatStarted();
            OnCombatStart?.Invoke();

            // Начинаем первый раунд
            StartNextRound();
        }

        public void EndCombat(bool playerVictory)
        {
            isCombatActive = false;

            if (playerVictory)
            {
                // Выдаём опыт
                GrantExperience();
                // Собираем лут
                CollectLoot();
            }

            // Очищаем боевое состояние
            foreach (var unit in allUnits)
            {
                if (unit.isPlayerControlled && unit.character != null)
                {
                    // Синхронизируем HP обратно
                    unit.character.stats.currentHP = unit.currentHP;
                    unit.character.stats.currentMP = unit.currentMP;
                }
            }

            allUnits.Clear();
            turnOrder.Clear();
            currentUnit = null;

            GameManager.Instance.SetGameState(GameState.Exploration);
            GameManager.Instance.EventBus.RaiseCombatEnded(playerVictory);
            OnCombatEnd?.Invoke(playerVictory);
        }

        #endregion

        #region Turn Management

        private void DetermineTurnOrder()
        {
            // Инициатива = d20 + DEX модификатор
            foreach (var unit in allUnits)
            {
                if (!unit.isDead)
                {
                    unit.initiative = UnityEngine.Random.Range(1, 21) + unit.dexterityMod;
                    turnOrder.Add(unit);
                }
            }

            turnOrder.Sort((a, b) => b.initiative.CompareTo(a.initiative));
        }

        private void StartNextRound()
        {
            roundNumber++;
            currentTurnIndex = -1;
            AdvanceTurn();
        }

        public void AdvanceTurn()
        {
            if (!isCombatActive) return;

            // Конец хода текущего
            if (currentUnit != null)
            {
                currentUnit.TickEffects();
                OnTurnEnd?.Invoke(currentUnit);
            }

            currentTurnIndex++;

            // Пропускаем мёртвых
            while (currentTurnIndex < turnOrder.Count &&
                   (turnOrder[currentTurnIndex].isDead || turnOrder[currentTurnIndex].isStunned))
            {
                currentTurnIndex++;
            }

            // Конец раунда
            if (currentTurnIndex >= turnOrder.Count)
            {
                // Проверяем условия победы/поражения
                if (CheckVictoryCondition())
                {
                    EndCombat(true);
                    return;
                }
                if (CheckDefeatCondition())
                {
                    EndCombat(false);
                    return;
                }

                StartNextRound();
                return;
            }

            currentUnit = turnOrder[currentTurnIndex];
            currentUnit.currentActionPoints = currentUnit.maxActionPoints;
            currentUnit.hasMoved = false;

            OnTurnStart?.Invoke(currentUnit);

            // Если ход AI
            if (!currentUnit.isPlayerControlled && !currentUnit.isDead)
            {
                ProcessAITurn(currentUnit);
            }
        }

        private bool CheckVictoryCondition()
        {
            return allUnits.FindAll(u => !u.isPlayerControlled).TrueForAll(u => u.isDead);
        }

        private bool CheckDefeatCondition()
        {
            return allUnits.FindAll(u => u.isPlayerControlled).TrueForAll(u => u.isDead);
        }

        #endregion

        #region Actions

        public CombatActionResult PerformAction(CombatAction action)
        {
            if (!isCombatActive || currentUnit == null)
                return new CombatActionResult { success = false, message = "Нет активного хода" };

            if (currentUnit != action.performer)
                return new CombatActionResult { success = false, message = "Сейчас не ваш ход" };

            if (currentUnit.currentActionPoints < action.actionPointCost)
                return new CombatActionResult { success = false, message = "Недостаточно очков действий" };

            var result = action.Execute();

            // Тратим AP
            currentUnit.currentActionPoints -= action.actionPointCost;

            OnActionPerformed?.Invoke(action, result);
            GameManager.Instance.EventBus.RaiseDamageDealt(
                action.target?.unitId ?? "", result.damageDealt);

            // Проверяем смерть
            if (action.target != null && action.target.isDead)
            {
                OnUnitDied?.Invoke(action.target);
                GameManager.Instance.EventBus.RaiseCharacterDied(action.target.unitId);
            }

            // Проверяем конец боя
            if (CheckVictoryCondition())
            {
                EndCombat(true);
            }
            else if (CheckDefeatCondition())
            {
                EndCombat(false);
            }

            return result;
        }

        /// <summary>
        /// Атака ближнего боя
        /// </summary>
        public CombatActionResult MeleeAttack(CombatUnit attacker, CombatUnit target)
        {
            var action = new CombatAction
            {
                performer = attacker,
                target = target,
                actionType = CombatActionType.MeleeAttack,
                actionPointCost = attackActionCost
            };
            action.Execute = () =>
            {
                int attackRoll = UnityEngine.Random.Range(1, 21);
                int attackBonus = attacker.strengthMod + attacker.proficiencyBonus;
                int totalAttack = attackRoll + attackBonus;

                if (attackRoll == 1) // Критический промах
                    return new CombatActionResult { success = false, message = "Критический промах!" };

                if (attackRoll == 20 || totalAttack >= target.armorClass) // Критический удар или попадание
                {
                    int damage = CalculateDamage(attacker, target, attackRoll == 20);
                    target.currentHP = Mathf.Max(0, target.currentHP - damage);

                    if (target.currentHP <= 0)
                        target.isDead = true;

                    return new CombatActionResult
                    {
                        success = true,
                        damageDealt = damage,
                        isCritical = attackRoll == 20,
                        message = attackRoll == 20 ?
                            $"Критический удар! {damage} урона!" :
                            $"Попадание! {damage} урона."
                    };
                }

                return new CombatActionResult { success = false, message = "Промах!" };
            };

            return PerformAction(action);
        }

        /// <summary>
        /// Магическая атака
        /// </summary>
        public CombatActionResult CastSpell(CombatUnit caster, CombatUnit target, SpellDefinition spell)
        {
            if (caster.currentMP < spell.mpCost)
                return new CombatActionResult { success = false, message = "Недостаточно маны" };

            var action = new CombatAction
            {
                performer = caster,
                target = target,
                actionType = CombatActionType.Spell,
                actionPointCost = spellActionCost
            };
            action.Execute = () =>
            {
                caster.currentMP -= spell.mpCost;

                // Проверка спасброска если нужно
                if (spell.requiresSave)
                {
                    int saveRoll = UnityEngine.Random.Range(1, 21) + target.GetSaveBonus(spell.saveType);
                    if (saveRoll >= spell.saveDC)
                    {
                        // Успешный спасбросок - половина урона
                        int halfDamage = spell.baseDamage / 2;
                        target.currentHP = Mathf.Max(0, target.currentHP - halfDamage);
                        if (target.currentHP <= 0) target.isDead = true;
                        return new CombatActionResult
                        {
                            success = true,
                            damageDealt = halfDamage,
                            message = $"Спасбросок успешен! {halfDamage} урона."
                        };
                    }
                }

                // Бросок атаки заклинанием
                int spellAttackRoll = UnityEngine.Random.Range(1, 21);
                int spellBonus = caster.GetIntelligenceMod() + caster.proficiencyBonus;

                if (spellAttackRoll == 20 || (spellAttackRoll + spellBonus) >= target.armorClass)
                {
                    int damage = spell.baseDamage +
                                 (spellAttackRoll == 20 ? spell.baseDamage : 0);
                    target.currentHP = Mathf.Max(0, target.currentHP - damage);
                    if (target.currentHP <= 0) target.isDead = true;

                    // Применяем эффект
                    if (spell.appliedEffect != null)
                        target.ApplyEffect(spell.appliedEffect);

                    return new CombatActionResult
                    {
                        success = true,
                        damageDealt = damage,
                        isCritical = spellAttackRoll == 20,
                        message = $"{spell.displayName}: {damage} урона!"
                    };
                }

                return new CombatActionResult { success = false, message = "Заклинание не попало!" };
            };

            return PerformAction(action);
        }

        /// <summary>
        /// Перемещение юнита
        /// </summary>
        public CombatActionResult MoveUnit(CombatUnit unit, Vector2Int targetPosition)
        {
            int distance = Math.Abs(targetPosition.x - unit.gridPosition.x) +
                          Math.Abs(targetPosition.y - unit.gridPosition.y);
            float moveCost = distance * movementCostPerTile;

            if (unit.currentActionPoints < moveCost)
                return new CombatActionResult { success = false, message = "Недостаточно AP" };

            if (unit.hasMoved && moveCost > 0)
                return new CombatActionResult { success = false, message = "Уже перемещались" };

            // Проверяем, свободна ли клетка
            if (IsTileOccupied(targetPosition))
                return new CombatActionResult { success = false, message = "Клетка занята" };

            unit.currentActionPoints -= moveCost;
            unit.gridPosition = targetPosition;
            unit.hasMoved = true;

            // Проверяем атаки возможности
            CheckOpportunityAttacks(unit);

            return new CombatActionResult { success = true, message = "Перемещение завершено" };
        }

        /// <summary>
        /// Конец хода
        /// </summary>
        public void EndTurn()
        {
            if (!isCombatActive) return;
            AdvanceTurn();
        }

        private int CalculateDamage(CombatUnit attacker, CombatUnit target, bool isCritical)
        {
            // Базовый урон от оружия + модификатор силы
            int baseDamage = attacker.weaponDamage + attacker.strengthMod;
            if (isCritical) baseDamage *= 2;

            // Снижение от брони (упрощённо)
            int effectiveDamage = Mathf.Max(1, baseDamage);
            return effectiveDamage;
        }

        private void CheckOpportunityAttacks(CombatUnit movingUnit)
        {
            // Упрощённо: враги рядом могут атаковать
            foreach (var unit in allUnits)
            {
                if (unit.isDead || unit.isPlayerControlled == movingUnit.isPlayerControlled)
                    continue;

                int distance = Math.Abs(unit.gridPosition.x - movingUnit.gridPosition.x) +
                              Math.Abs(unit.gridPosition.y - movingUnit.gridPosition.y);

                if (distance <= 1)
                {
                    // Атака возможности (с штрафом)
                    int attackRoll = UnityEngine.Random.Range(1, 21);
                    if (attackRoll + unit.strengthMod >= movingUnit.armorClass)
                    {
                        int damage = Mathf.Max(1, unit.weaponDamage + unit.strengthMod - 2);
                        movingUnit.currentHP -= damage;
                        if (movingUnit.currentHP <= 0) movingUnit.isDead = true;
                    }
                }
            }
        }

        private bool IsTileOccupied(Vector2Int position)
        {
            return allUnits.Exists(u => !u.isDead && u.gridPosition == position);
        }

        #endregion

        #region AI

        private void ProcessAITurn(CombatUnit aiUnit)
        {
            StartCoroutine(ProcessAITurnCoroutine(aiUnit));
        }

        private System.Collections.IEnumerator ProcessAITurnCoroutine(CombatUnit aiUnit)
        {
            yield return new WaitForSeconds(0.5f);

            // Простой AI: найти ближайшую цель, подойти, атаковать
            var targets = allUnits.FindAll(u => u.isPlayerControlled && !u.isDead);
            if (targets.Count == 0)
            {
                EndTurn();
                yield break;
            }

            // Находим ближайшую цель
            CombatUnit closestTarget = null;
            float closestDist = float.MaxValue;
            foreach (var target in targets)
            {
                float dist = Vector2.Distance(
                    (Vector2)aiUnit.gridPosition,
                    (Vector2)target.gridPosition);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestTarget = target;
                }
            }

            if (closestTarget == null)
            {
                EndTurn();
                yield break;
            }

            // Если достаточно близко - атакуем
            if (closestDist <= 1.5f)
            {
                MeleeAttack(aiUnit, closestTarget);
                yield return new WaitForSeconds(0.3f);
            }
            else
            {
                // Двигаемся к цели
                Vector2Int moveTarget = GetAdjacentTile(closestTarget.gridPosition);
                if (moveTarget != aiUnit.gridPosition)
                {
                    MoveUnit(aiUnit, moveTarget);
                    yield return new WaitForSeconds(0.3f);

                    // Пробуем атаковать если остались AP
                    if (aiUnit.currentActionPoints >= attackActionCost)
                    {
                        float newDist = Vector2.Distance(
                            (Vector2)aiUnit.gridPosition,
                            (Vector2)closestTarget.gridPosition);
                        if (newDist <= 1.5f)
                        {
                            MeleeAttack(aiUnit, closestTarget);
                            yield return new WaitForSeconds(0.3f);
                        }
                    }
                }
            }

            EndTurn();
        }

        private Vector2Int GetAdjacentTile(Vector2Int target)
        {
            Vector2Int[] offsets = {
                new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
            };

            foreach (var offset in offsets)
            {
                Vector2Int tile = target + offset;
                if (!IsTileOccupied(tile) && IsInBounds(tile))
                    return tile;
            }
            return target;
        }

        private bool IsInBounds(Vector2Int pos)
        {
            return pos.x >= 0 && pos.x < gridWidth && pos.y >= 0 && pos.y < gridHeight;
        }

        #endregion

        #region Unit Creation

        private CombatUnit CreateUnitFromCharacter(PlayerCharacter character, bool isPlayer, bool isCompanion)
        {
            return new CombatUnit
            {
                unitId = character.characterId,
                displayName = character.displayName,
                isPlayerControlled = true,
                character = character,
                currentHP = character.stats.currentHP,
                maxHP = character.stats.maxHP,
                currentMP = character.stats.currentMP,
                maxMP = character.stats.maxMP,
                armorClass = character.stats.armorClass,
                strengthMod = character.stats.GetStrengthMod,
                dexterityMod = character.stats.GetDexterityMod,
                constitutionMod = character.stats.GetConstitutionMod,
                proficiencyBonus = character.stats.GetProficiencyBonus(),
                weaponDamage = 6, // базовый d6
                maxActionPoints = actionPointBase,
                currentActionPoints = actionPointBase,
                gridPosition = Vector2Int.zero
            };
        }

        private CombatUnit CreateUnitFromCompanion(Companion.CompanionData companion)
        {
            return new CombatUnit
            {
                unitId = companion.companionId,
                displayName = companion.displayName,
                isPlayerControlled = true,
                currentHP = companion.stats?.currentHP ?? 20,
                maxHP = companion.stats?.maxHP ?? 20,
                currentMP = companion.stats?.currentMP ?? 10,
                maxMP = companion.stats?.maxMP ?? 10,
                armorClass = companion.stats?.armorClass ?? 12,
                strengthMod = companion.stats?.GetStrengthMod ?? 0,
                dexterityMod = companion.stats?.GetDexterityMod ?? 0,
                constitutionMod = companion.stats?.GetConstitutionMod ?? 0,
                proficiencyBonus = companion.stats?.GetProficiencyBonus() ?? 2,
                weaponDamage = 6,
                maxActionPoints = actionPointBase,
                currentActionPoints = actionPointBase,
                gridPosition = Vector2Int.zero
            };
        }

        private CombatUnit CreateUnitFromEnemy(EnemyDefinition enemy)
        {
            return new CombatUnit
            {
                unitId = enemy.enemyId + "_" + Guid.NewGuid().ToString().Substring(0, 4),
                displayName = enemy.displayName,
                isPlayerControlled = false,
                currentHP = enemy.maxHP,
                maxHP = enemy.maxHP,
                armorClass = enemy.armorClass,
                strengthMod = enemy.strengthMod,
                dexterityMod = enemy.dexterityMod,
                constitutionMod = enemy.constitutionMod,
                proficiencyBonus = enemy.proficiencyBonus,
                weaponDamage = enemy.weaponDamage,
                maxActionPoints = actionPointBase,
                currentActionPoints = actionPointBase,
                experienceValue = enemy.experienceValue,
                gridPosition = enemy.spawnPosition
            };
        }

        #endregion

        #region Post-Combat

        private void GrantExperience()
        {
            int totalExp = 0;
            foreach (var unit in allUnits)
            {
                if (!unit.isPlayerControlled)
                    totalExp += unit.experienceValue;
            }

            var player = CharacterCreation.Instance?.Character;
            if (player != null)
            {
                player.stats.experience += totalExp;
                GameManager.Instance.EventBus.RaiseExperienceGained("player", totalExp);

                if (player.stats.TryLevelUp())
                    GameManager.Instance.EventBus.RaiseLevelUp("player", player.stats.level);
            }
        }

        private void CollectLoot()
        {
            // TODO: система лута
        }

        #endregion

        #region Save/Load

        public string OnSave()
        {
            // Сохраняем только если бой активен
            if (!isCombatActive) return "{}";

            var data = new CombatSaveData
            {
                isActive = true,
                roundNumber = roundNumber,
                currentTurnIndex = currentTurnIndex,
                units = new()
            };

            foreach (var unit in allUnits)
            {
                data.units.Add(new CombatUnitSaveData
                {
                    unitId = unit.unitId,
                    currentHP = unit.currentHP,
                    currentMP = unit.currentMP,
                    gridPosition = unit.gridPosition,
                    isDead = unit.isDead,
                    initiative = unit.initiative
                });
            }

            return JsonUtility.ToJson(data);
        }

        public void OnLoad(string json)
        {
            var data = JsonUtility.FromJson<CombatSaveData>(json);
            if (data == null || !data.isActive) return;
            // Восстановление боя - TODO: полная реализация
        }

        #endregion
    }

    #region Data Types

    [Serializable]
    public class CombatUnit
    {
        public string unitId;
        public string displayName;
        public bool isPlayerControlled;
        public bool isDead;
        public bool isStunned;
        public bool hasMoved;

        // Ссылка на персонажа (для синхронизации)
        [NonSerialized] public PlayerCharacter character;

        // Статы
        public int currentHP;
        public int maxHP;
        public int currentMP;
        public int maxMP;
        public int armorClass;
        public int initiative;

        // Модификаторы
        public int strengthMod;
        public int dexterityMod;
        public int constitutionMod;
        public int proficiencyBonus;
        public int weaponDamage;

        // Очки действий
        public float maxActionPoints;
        public float currentActionPoints;

        // Позиция на сетке
        public Vector2Int gridPosition;

        // Опыт за убийство
        public int experienceValue;

        // Эффекты
        private List<StatusEffect> activeEffects = new();

        public int GetIntelligenceMod() => 0; // TODO

        public int GetSaveBonus(SaveType saveType)
        {
            return saveType switch
            {
                SaveType.Fortitude => constitutionMod + proficiencyBonus,
                SaveType.Reflex => dexterityMod + proficiencyBonus,
                SaveType.Will => proficiencyBonus,
                _ => 0
            };
        }

        public void TickEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                activeEffects[i].remainingDuration--;
                if (activeEffects[i].remainingDuration <= 0)
                    activeEffects.RemoveAt(i);
            }
        }

        public void ApplyEffect(StatusEffect effect)
        {
            activeEffects.Add(effect);
        }
    }

    [Serializable]
    public class CombatAction
    {
        public CombatUnit performer;
        public CombatUnit target;
        public CombatActionType actionType;
        public float actionPointCost;
        public Func<CombatActionResult> Execute;
    }

    public enum CombatActionType
    {
        MeleeAttack,
        RangedAttack,
        Spell,
        Item,
        Defend,
        Dash,
        Hide,
        UseAbility
    }

    [Serializable]
    public class CombatActionResult
    {
        public bool success;
        public int damageDealt;
        public int healingDone;
        public bool isCritical;
        public string message;
    }

    [Serializable]
    public class CombatEncounter
    {
        public string encounterId;
        public List<EnemyDefinition> enemies = new();
        public string environment;
        public bool isBossFight;
    }

    [Serializable]
    public class EnemyDefinition
    {
        public string enemyId;
        public string displayName;
        public int maxHP;
        public int armorClass;
        public int strengthMod;
        public int dexterityMod;
        public int constitutionMod;
        public int proficiencyBonus;
        public int weaponDamage;
        public int experienceValue;
        public Vector2Int spawnPosition;
        public string lootTableId;
    }

    [Serializable]
    public class SpellDefinition
    {
        public string spellId;
        public string displayName;
        public int mpCost;
        public int baseDamage;
        public bool requiresSave;
        public SaveType saveType;
        public int saveDC;
        public int range;
        public StatusEffect appliedEffect;
    }

    public enum SaveType
    {
        Fortitude,
        Reflex,
        Will
    }

    #endregion

    #region Save Data

    [Serializable]
    public class CombatSaveData
    {
        public bool isActive;
        public int roundNumber;
        public int currentTurnIndex;
        public List<CombatUnitSaveData> units = new();
    }

    [Serializable]
    public class CombatUnitSaveData
    {
        public string unitId;
        public int currentHP;
        public int currentMP;
        public Vector2Int gridPosition;
        public bool isDead;
        public int initiative;
    }

    #endregion
}
