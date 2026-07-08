using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.Combat
{
    /// <summary>
    /// Модель поля боя: сетка клеток, стены, укрытия, стартовые позиции.
    /// Грузится из JSON per-encounter через <see cref="BattleMapLoader"/>.
    /// </summary>
    public class BattleMap
    {
        public string mapId;
        public string displayName;
        public int width;
        public int height;

        /// <summary>Тайлы карты. Индексация [x, y].</summary>
        public TileKind[,] tiles;

        /// <summary>Стартовые позиции игрока (в порядке юнитов).</summary>
        public List<Vector2Int> playerStarts = new();

        /// <summary>Стартовые позиции врагов по enemyId.</summary>
        public Dictionary<string, Vector2Int> enemyStarts = new();

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;

        public TileKind GetTile(Vector2Int p) => InBounds(p) ? tiles[p.x, p.y] : TileKind.Wall;

        /// <summary>Проходима ли клетка для стандартного передвижения (не блокирована стеной/пропастью).</summary>
        public bool IsWalkable(Vector2Int p)
        {
            var t = GetTile(p);
            return t == TileKind.Floor || t == TileKind.Difficult || t == TileKind.Hazard;
        }

        /// <summary>Блокирует ли клетка линию видимости (полное укрытие / стена).</summary>
        public bool BlocksLineOfSight(Vector2Int p)
        {
            var t = GetTile(p);
            return t == TileKind.Wall || t == TileKind.FullCover;
        }

        /// <summary>Даёт ли клетка укрытие (частичное = +помеха на дальнюю атаку по цели за ней).</summary>
        public bool GivesCover(Vector2Int p)
        {
            var t = GetTile(p);
            return t == TileKind.HalfCover || t == TileKind.FullCover;
        }

        /// <summary>Двойная стоимость движения (мебель / болото / лозы).</summary>
        public bool IsDifficultTerrain(Vector2Int p) => GetTile(p) == TileKind.Difficult;
    }

    /// <summary>
    /// Тип клетки на поле боя.
    /// Wall / FullCover — совсем не пройти.
    /// Floor — обычная.
    /// Difficult — стоит 2 очка движения (мебель, вода, лозы).
    /// Hazard — обычная для движения, но наносит урон при входе (реализуется через CombatZones).
    /// HalfCover — по ней можно ходить, но даёт частичное укрытие цели за ней.
    /// </summary>
    public enum TileKind
    {
        Floor = 0,
        Wall = 1,
        FullCover = 2,
        HalfCover = 3,
        Difficult = 4,
        Hazard = 5
    }
}
