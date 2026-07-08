using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RPG.Character;

namespace RPG.UI
{
    /// <summary>
    /// Подбирает спрайт для юнита боя.
    /// Файлы лежат в Assets/Art/Combat:
    ///   player/&lt;race&gt;_&lt;gender&gt;_&lt;class&gt;.png
    ///   enemies/&lt;enemyId&gt;.png
    /// Если конкретной комбинации нет — подставляется ближайшая по классу/полу/расе.
    /// В рантайме читается через Application.dataPath (для редактора и билда без AssetBundle).
    /// В билде можно перевести на Resources.Load — я оставил обе ветки.
    /// </summary>
    public static class PortraitResolver
    {
        private static readonly Dictionary<string, Sprite> cache = new();

        public static Sprite GetPlayerSprite(RaceType race, Gender gender, ClassType cls)
        {
            var candidates = new List<string>();

            string raceKey = race switch
            {
                RaceType.Human => "human",
                RaceType.Elf => "elf",
                RaceType.Tiefling => "tiefling",
                RaceType.Orc => "orc",
                _ => "human"
            };
            string genderKey = gender == Gender.Female ? "female" : "male";
            string classKey = cls switch
            {
                ClassType.Warrior => "warrior",
                ClassType.Rogue => "rogue",
                ClassType.Mage => "mage",
                ClassType.Cleric => "cleric",
                ClassType.Druid => "druid",
                _ => "warrior"
            };

            // Идеальное совпадение.
            candidates.Add($"player/{raceKey}_{genderKey}_{classKey}");
            // Тот же класс+пол, но другая раса (human — универсальный).
            candidates.Add($"player/human_{genderKey}_{classKey}");
            // Тот же класс, любой пол.
            candidates.Add($"player/human_male_{classKey}");
            candidates.Add($"player/human_female_{classKey}");
            // Хоть что-то по классу.
            candidates.Add($"player/tiefling_female_{classKey}");
            candidates.Add($"player/elf_male_{classKey}");
            // В самом крайнем случае — воин-человек.
            candidates.Add("player/human_male_warrior");

            foreach (var c in candidates)
            {
                var s = LoadSprite(c);
                if (s != null) return s;
            }
            return null;
        }

        public static Sprite GetEnemySprite(string enemyId)
        {
            var s = LoadSprite($"enemies/{enemyId}");
            if (s != null) return s;
            // Фоллбеки по семейству.
            if (enemyId.Contains("morale_lust")) return LoadSprite("enemies/morale_lust");
            if (enemyId.Contains("morale_fear")) return LoadSprite("enemies/morale_fear");
            if (enemyId.Contains("guard") || enemyId.Contains("gort") || enemyId.Contains("bruno"))
                return LoadSprite("enemies/brother_gort");
            return null;
        }

        // ------------- Загрузка с диска (плюс кэш) -------------

        private static Sprite LoadSprite(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            if (cache.TryGetValue(relativePath, out var s)) return s;

            // 1) В редакторе/сборке ищем в Assets/Art/Combat.
            string full = Path.Combine(Application.dataPath, "Art", "Combat", relativePath + ".png");
            if (File.Exists(full))
            {
                var bytes = File.ReadAllBytes(full);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.filterMode = FilterMode.Point; // пиксель-арт
                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);
                cache[relativePath] = sprite;
                return sprite;
            }

            // 2) Пробуем Resources (если художник кинет в Assets/Resources/Combat/...).
            var res = Resources.Load<Sprite>("Combat/" + relativePath);
            if (res != null)
            {
                cache[relativePath] = res;
                return res;
            }

            cache[relativePath] = null;
            return null;
        }
    }
}
