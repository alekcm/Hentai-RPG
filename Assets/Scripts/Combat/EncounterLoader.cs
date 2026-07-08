using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RPG.Items;

namespace RPG.Combat
{
    /// <summary>
    /// Загружает описания боевых энкаунтеров из StreamingAssets/Encounters/&lt;id&gt;.json.
    /// Формат JSON см. prologue_r2_guards_and_lust.json.
    /// </summary>
    public static class EncounterLoader
    {
        private static readonly Dictionary<string, CombatEncounter> cache = new();

        public static CombatEncounter Load(string encounterId)
        {
            if (cache.TryGetValue(encounterId, out var cached)) return cached;

            string path = Path.Combine(Application.streamingAssetsPath, "Encounters", encounterId + ".json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[EncounterLoader] Файл не найден: {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            var raw = JsonUtility.FromJson<RawEncounter>(json);
            if (raw == null)
            {
                Debug.LogError($"[EncounterLoader] Не удалось распарсить {encounterId}");
                return null;
            }

            var enc = new CombatEncounter
            {
                encounterId = raw.encounterId,
                environment = raw.environment,
                enemies = new List<EnemyDefinition>()
            };

            foreach (var re in raw.enemies)
            {
                var def = new EnemyDefinition
                {
                    enemyId = re.enemyId,
                    displayName = re.displayName,
                    role = ParseRole(re.role),
                    level = re.level > 0 ? re.level : 1,
                    evasion = re.evasion,
                    damageThreshold = re.damageThreshold,
                    armorRating = re.armorRating,
                    armorSlots = re.armorSlots,
                    healthSlots = re.healthSlots,
                    hpPerSlot = re.hpPerSlot,
                    stamina = re.stamina,
                    attackBonus = re.attackBonus,
                    weaponName = re.weaponName,
                    weaponRange = ParseRange(re.weaponRange),
                    weaponDamage = re.weaponDamage,
                    spawnPosition = new Vector2Int(re.spawnPosition.x, re.spawnPosition.y),
                    tags = re.tags ?? new List<string>()
                };
                enc.enemies.Add(def);
            }

            cache[encounterId] = enc;
            return enc;
        }

        private static EnemyRole ParseRole(string s)
        {
            if (Enum.TryParse<EnemyRole>(s, true, out var r)) return r;
            return EnemyRole.Standard;
        }

        private static WeaponRange ParseRange(string s)
        {
            if (Enum.TryParse<WeaponRange>(s, true, out var r)) return r;
            return WeaponRange.Melee;
        }

        [Serializable]
        private class RawEncounter
        {
            public string encounterId;
            public string environment;
            public string description;
            public List<RawEnemy> enemies = new();
        }

        [Serializable]
        private class RawEnemy
        {
            public string enemyId;
            public string displayName;
            public string role;
            public int level;
            public int evasion;
            public int damageThreshold;
            public int armorRating;
            public int armorSlots;
            public int healthSlots;
            public int hpPerSlot;
            public int stamina;
            public int attackBonus;
            public string weaponName;
            public string weaponRange;
            public string weaponDamage;
            public IntVec2 spawnPosition;
            public List<string> tags;
        }

        [Serializable]
        private class IntVec2 { public int x; public int y; }
    }
}
