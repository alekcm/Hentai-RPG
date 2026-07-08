using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RPG.Combat
{
    /// <summary>
    /// Загружает описание карты боя из StreamingAssets/Arenas/&lt;id&gt;.json.
    /// Формат JSON — см. arena_r2_guardroom.json.
    /// Тайлы задаются либо строками (по одному символу на клетку), либо массивом чисел.
    /// Символы: '.' Floor, '#' Wall, 'O' FullCover, 'o' HalfCover, '~' Difficult, '!' Hazard.
    /// </summary>
    public static class BattleMapLoader
    {
        private static readonly Dictionary<string, BattleMap> cache = new();

        public static BattleMap Load(string arenaId)
        {
            if (string.IsNullOrEmpty(arenaId)) return null;
            if (cache.TryGetValue(arenaId, out var cached)) return cached;

            string path = Path.Combine(Application.streamingAssetsPath, "Arenas", arenaId + ".json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[BattleMapLoader] Файл арены не найден: {path}. Будет использована пустая карта по умолчанию.");
                return null;
            }

            var raw = JsonUtility.FromJson<RawArena>(File.ReadAllText(path));
            if (raw == null || raw.tiles == null || raw.tiles.Count == 0)
            {
                Debug.LogError($"[BattleMapLoader] Не удалось распарсить арену {arenaId}");
                return null;
            }

            var map = new BattleMap
            {
                mapId = raw.mapId ?? arenaId,
                displayName = raw.displayName ?? arenaId,
                width = raw.width > 0 ? raw.width : raw.tiles[0].Length,
                height = raw.height > 0 ? raw.height : raw.tiles.Count
            };
            map.tiles = new TileKind[map.width, map.height];

            // По ГДД строки в JSON — от верхней к нижней. y=0 в игре — нижняя строка.
            for (int rowIdx = 0; rowIdx < raw.tiles.Count && rowIdx < map.height; rowIdx++)
            {
                string row = raw.tiles[rowIdx];
                int y = map.height - 1 - rowIdx;
                for (int x = 0; x < row.Length && x < map.width; x++)
                    map.tiles[x, y] = CharToTile(row[x]);
            }

            if (raw.playerStarts != null)
                foreach (var p in raw.playerStarts) map.playerStarts.Add(new Vector2Int(p.x, p.y));
            if (raw.enemyStarts != null)
                foreach (var e in raw.enemyStarts)
                    map.enemyStarts[e.enemyId] = new Vector2Int(e.x, e.y);

            cache[arenaId] = map;
            Debug.Log($"[BattleMapLoader] Арена {arenaId} загружена: {map.width}x{map.height}, стартов игрока: {map.playerStarts.Count}, врагов: {map.enemyStarts.Count}.");
            return map;
        }

        /// <summary>Дефолтная пустая карта, если для энкаунтера не задана арена.</summary>
        public static BattleMap CreateDefault(int width = 16, int height = 12)
        {
            var map = new BattleMap
            {
                mapId = "default",
                displayName = "Пустая арена",
                width = width,
                height = height
            };
            map.tiles = new TileKind[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    map.tiles[x, y] = TileKind.Floor;
            map.playerStarts.Add(new Vector2Int(2, height / 2));
            return map;
        }

        private static TileKind CharToTile(char c) => c switch
        {
            '.' => TileKind.Floor,
            '#' => TileKind.Wall,
            'O' => TileKind.FullCover,
            'o' => TileKind.HalfCover,
            '~' => TileKind.Difficult,
            '!' => TileKind.Hazard,
            _   => TileKind.Floor
        };

        [Serializable]
        private class RawArena
        {
            public string mapId;
            public string displayName;
            public int width;
            public int height;
            public List<string> tiles = new();
            public List<Point> playerStarts = new();
            public List<NamedPoint> enemyStarts = new();
        }

        [Serializable] private class Point { public int x; public int y; }
        [Serializable] private class NamedPoint { public string enemyId; public int x; public int y; }
    }
}
