using System.Collections.Generic;
using UnityEngine;

namespace RPG.Combat
{
    /// <summary>
    /// Зоны на поле боя — мистический туман, огненные лужи, области Морали и т.п.
    /// Стационарные, до конца боя (или иного условия).
    /// </summary>
    public enum ZoneKind
    {
        MysticFog,      // блокирует видимость: атаки в/из зоны с помехой
        Fire,           // проходящий через — получает урон
        Vines,          // движение стоит дороже
        MoraleAura      // особые эффекты Морали
    }

    public class CombatZone
    {
        public ZoneKind kind;
        public Vector2Int center;
        public int radius;   // "5x5" = radius 2 (манхэттен)
        public int roundsLeft;   // 999 = "до конца боя"

        public bool Contains(Vector2Int p) => CombatManager.ManhattanDistance(center, p) <= radius;
    }

    public static class CombatZones
    {
        private static readonly List<CombatZone> zones = new();

        public static IReadOnlyList<CombatZone> All => zones;

        public static void Clear() => zones.Clear();

        public static void Add(CombatZone zone)
        {
            zones.Add(zone);
            Debug.Log($"[Zones] Добавлена зона {zone.kind} в {zone.center} радиусом {zone.radius}.");
        }

        /// <summary>Есть ли между двумя точками зона типа fog, влияющая на LOS.</summary>
        public static bool AttackObscuredByFog(Vector2Int from, Vector2Int to)
        {
            foreach (var z in zones)
            {
                if (z.kind != ZoneKind.MysticFog) continue;
                // Достаточно, чтобы одна из точек была в тумане — атака идёт с помехой.
                if (z.Contains(from) || z.Contains(to)) return true;
            }
            return false;
        }

        public static int FireDamageOnEnter(Vector2Int p)
        {
            int total = 0;
            foreach (var z in zones)
                if (z.kind == ZoneKind.Fire && z.Contains(p))
                    total += UnityEngine.Random.Range(1, 7); // 1d6
            return total;
        }
    }
}
