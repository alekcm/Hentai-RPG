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
        private static Dictionary<string, RawEnemy> library;

        private static void EnsureLibrary()
        {
            if (library != null) return;
            library = new Dictionary<string, RawEnemy>();
            string libPath = Path.Combine(Application.streamingAssetsPath, "EnemyLibrary", "enemy_library.json");
            if (!File.Exists(libPath))
            {
                Debug.LogWarning($"[EncounterLoader] Библиотека противников не найдена: {libPath}");
                return;
            }
            var libRaw = JsonUtility.FromJson<RawLibrary>(File.ReadAllText(libPath));
            if (libRaw?.enemies != null)
                foreach (var e in libRaw.enemies)
                    library[e.enemyId] = e;
            Debug.Log($"[EncounterLoader] Загружено статблоков: {library.Count}");
        }

        public static CombatEncounter Load(string encounterId)
        {
            if (string.IsNullOrEmpty(encounterId)) return null;
            if (cache.TryGetValue(encounterId, out var cached)) return cached;
            EnsureLibrary();

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
                arenaId = raw.arenaId,
                enemies = new List<EnemyDefinition>()
            };

            foreach (var re in raw.enemies)
            {
                // Если в JSON нет статов — берём из библиотеки.
                RawEnemy tpl = null;
                if (library != null && !string.IsNullOrEmpty(re.enemyId))
                    library.TryGetValue(re.enemyId, out tpl);

                var def = new EnemyDefinition
                {
                    enemyId = re.enemyId,
                    displayName = Pick(re.displayName, tpl?.displayName, re.enemyId),
                    role = ParseRole(Pick(re.role, tpl?.role, "Standard")),
                    level = re.level > 0 ? re.level : (tpl?.level ?? 1),
                    evasion = re.evasion != 0 ? re.evasion : (tpl?.evasion ?? 10),
                    damageThreshold = re.damageThreshold != 0 ? re.damageThreshold : (tpl?.damageThreshold ?? 0),
                    armorRating = re.armorRating != 0 ? re.armorRating : (tpl?.armorRating ?? 0),
                    armorSlots = re.armorSlots != 0 ? re.armorSlots : (tpl?.armorSlots ?? 0),
                    healthSlots = re.healthSlots != 0 ? re.healthSlots : (tpl?.healthSlots ?? 3),
                    hpPerSlot = re.hpPerSlot != 0 ? re.hpPerSlot : (tpl?.hpPerSlot ?? 4),
                    stamina = re.stamina != 0 ? re.stamina : (tpl?.stamina ?? 1),
                    attackBonus = re.attackBonus != 0 ? re.attackBonus : (tpl?.attackBonus ?? 0),
                    weaponName = Pick(re.weaponName, tpl?.weaponName, "Оружие"),
                    weaponRange = ParseRange(Pick(re.weaponRange, tpl?.weaponRange, "Melee")),
                    weaponDamage = Pick(re.weaponDamage, tpl?.weaponDamage, "d6"),
                    spawnPosition = new Vector2Int(re.spawnPosition?.x ?? 12, re.spawnPosition?.y ?? 6),
                    tags = re.tags != null && re.tags.Count > 0 ? re.tags : (tpl?.tags ?? new List<string>())
                };
                enc.enemies.Add(def);
            }

            cache[encounterId] = enc;
            return enc;
        }

        private static string Pick(params string[] vals)
        {
            foreach (var v in vals) if (!string.IsNullOrEmpty(v)) return v;
            return "";
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
        private class RawLibrary
        {
            public string description;
            public List<RawEnemy> enemies = new();
        }

        [Serializable]
        private class RawEncounter
        {
            public string encounterId;
            public string environment;
            public string arenaId;
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
