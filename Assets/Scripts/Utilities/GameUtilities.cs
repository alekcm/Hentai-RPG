using UnityEngine;
using System;
using System.Collections.Generic;
using RPG.Core;
using RPG.Character;

namespace RPG.Utilities
{
    /// <summary>
    /// Утилиты для работы с данными и отладки
    /// </summary>
    public static class GameUtilities
    {
        /// <summary>
        /// Генерирует уникальный ID
        /// </summary>
        public static string GenerateId(string prefix = "")
        {
            return $"{prefix}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        /// <summary>
        /// Форматирует время в читаемый вид
        /// </summary>
        public static string FormatGameTime(float hours, int day)
        {
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60);
            return $"День {day}, {h:D2}:{m:D2}";
        }

        /// <summary>
        /// Форматирует секунды в часы:минуты:секунды
        /// </summary>
        public static string FormatPlayTime(float seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        /// <summary>
        /// Проверяет вероятность (0-1)
        /// </summary>
        public static bool RollChance(float chance)
        {
            return UnityEngine.Random.value <= chance;
        }

        /// <summary>
        /// Бросок d20
        /// </summary>
        public static int RollD20()
        {
            return UnityEngine.Random.Range(1, 21);
        }

        /// <summary>
        /// Бросок кубика с модификатором
        /// </summary>
        public static int RollDice(int sides, int count = 1, int modifier = 0)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
                total += UnityEngine.Random.Range(1, sides + 1);
            return total + modifier;
        }
    }
}
