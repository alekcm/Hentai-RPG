using System.Collections.Generic;
using UnityEngine;

namespace RPG.Combat
{
    /// <summary>
    /// Пошаговый BFS по клеткам поля боя.
    /// Учитывает:
    ///  — Стены (Wall / FullCover) — непроходимы.
    ///  — Другие юниты — блокируют клетку (в т.ч. союзники).
    ///  — Difficult terrain — двойная стоимость входа.
    ///  — Обездвижен (immobilized_temp) — не может двигаться.
    /// Возвращает словарь «клетка → минимальная стоимость».
    /// </summary>
    public static class Pathfinding
    {
        private static readonly Vector2Int[] neighbors4 = {
            new Vector2Int( 1,  0), new Vector2Int(-1, 0),
            new Vector2Int( 0,  1), new Vector2Int( 0, -1)
        };

        /// <summary>Все клетки, до которых можно дойти за <= budget очков.</summary>
        public static Dictionary<Vector2Int, int> Reachable(
            BattleMap map, Vector2Int start, int budget,
            IEnumerable<CombatUnit> allUnits, CombatUnit self)
        {
            var result = new Dictionary<Vector2Int, int>();
            if (map == null || budget <= 0) { result[start] = 0; return result; }

            var occupied = BuildOccupancy(allUnits, self);
            var frontier = new PriorityQueue();
            frontier.Push(start, 0);
            result[start] = 0;

            while (frontier.Count > 0)
            {
                var (cur, cost) = frontier.Pop();
                if (cost > budget) continue;

                foreach (var d in neighbors4)
                {
                    var nb = cur + d;
                    if (!map.InBounds(nb)) continue;
                    if (!map.IsWalkable(nb)) continue;
                    if (occupied.Contains(nb)) continue;

                    int step = map.IsDifficultTerrain(nb) ? 2 : 1;
                    int newCost = cost + step;
                    if (newCost > budget) continue;

                    if (!result.TryGetValue(nb, out int prev) || newCost < prev)
                    {
                        result[nb] = newCost;
                        frontier.Push(nb, newCost);
                    }
                }
            }
            return result;
        }

        /// <summary>Восстановить путь от start до target по клеткам, если возможно в бюджете.</summary>
        public static List<Vector2Int> FindPath(
            BattleMap map, Vector2Int start, Vector2Int target, int budget,
            IEnumerable<CombatUnit> allUnits, CombatUnit self)
        {
            var result = new List<Vector2Int>();
            if (map == null) return result;
            if (start == target) return result;

            var occupied = BuildOccupancy(allUnits, self);

            var came = new Dictionary<Vector2Int, Vector2Int>();
            var cost = new Dictionary<Vector2Int, int> { [start] = 0 };
            var frontier = new PriorityQueue();
            frontier.Push(start, 0);

            bool found = false;
            while (frontier.Count > 0)
            {
                var (cur, curCost) = frontier.Pop();
                if (cur == target) { found = true; break; }
                if (curCost > budget) continue;

                foreach (var d in neighbors4)
                {
                    var nb = cur + d;
                    if (!map.InBounds(nb)) continue;
                    if (!map.IsWalkable(nb)) continue;
                    // Разрешаем стоять на конечной клетке даже если она "занята", если это цель =? Нет, движение только на пустую.
                    if (occupied.Contains(nb)) continue;

                    int step = map.IsDifficultTerrain(nb) ? 2 : 1;
                    int newCost = curCost + step;
                    if (newCost > budget) continue;

                    if (!cost.TryGetValue(nb, out int prev) || newCost < prev)
                    {
                        cost[nb] = newCost;
                        came[nb] = cur;
                        frontier.Push(nb, newCost);
                    }
                }
            }

            if (!found) return result;

            // Восстанавливаем путь.
            var cur2 = target;
            while (cur2 != start)
            {
                result.Add(cur2);
                cur2 = came[cur2];
            }
            result.Reverse();
            return result;
        }

        /// <summary>Chebyshev distance — «клеточная» дистанция ГДД, соседи-диагонали = 1.
        /// Мы используем её для проверок дальности оружия/заклинаний,
        /// а BFS-стоимость движения — только по 4 соседям (чтобы диагональ не была халявной).</summary>
        public static int GridDistance(Vector2Int a, Vector2Int b)
            => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>Простой Bresenham для проверки линии видимости через тайлы.</summary>
        public static bool HasLineOfSight(BattleMap map, Vector2Int from, Vector2Int to)
        {
            if (map == null) return true;
            if (from == to) return true;
            int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                if (!(x0 == from.x && y0 == from.y) && !(x0 == to.x && y0 == to.y))
                {
                    if (map.BlocksLineOfSight(new Vector2Int(x0, y0))) return false;
                }
                if (x0 == x1 && y0 == y1) return true;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx)  { err += dx; y0 += sy; }
            }
        }

        private static HashSet<Vector2Int> BuildOccupancy(IEnumerable<CombatUnit> units, CombatUnit exclude)
        {
            var occ = new HashSet<Vector2Int>();
            foreach (var u in units)
            {
                if (u == null || u.IsDead || u == exclude) continue;
                occ.Add(u.gridPosition);
            }
            return occ;
        }

        // --------------------------------------------------------
        // Простенькая приоритетная очередь для BFS/Dijkstra
        // --------------------------------------------------------
        private class PriorityQueue
        {
            private readonly List<(Vector2Int p, int cost)> data = new();
            public int Count => data.Count;
            public void Push(Vector2Int p, int cost)
            {
                data.Add((p, cost));
                int i = data.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (data[parent].cost <= data[i].cost) break;
                    (data[parent], data[i]) = (data[i], data[parent]);
                    i = parent;
                }
            }
            public (Vector2Int, int) Pop()
            {
                var top = data[0];
                int last = data.Count - 1;
                data[0] = data[last];
                data.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = i * 2 + 2, s = i;
                    if (l < data.Count && data[l].cost < data[s].cost) s = l;
                    if (r < data.Count && data[r].cost < data[s].cost) s = r;
                    if (s == i) break;
                    (data[i], data[s]) = (data[s], data[i]);
                    i = s;
                }
                return top;
            }
        }
    }
}
