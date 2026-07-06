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

    /// <summary>
    /// Debug менеджер для тестирования
    /// </summary>
    public class DebugManager : MonoBehaviour
    {
        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugKeys = true;
        [SerializeField] private bool showDebugGUI = false;

        private void Update()
        {
            if (!enableDebugKeys) return;

            // Быстрые клавиши для отладки
            if (Input.GetKeyDown(KeyCode.F1))
            {
                // Быстрое сохранение
                SaveManager.Instance?.Save(0);
                Debug.Log("[Debug] Quick save");
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                // Быстрая загрузка
                SaveManager.Instance?.Load(0);
                Debug.Log("[Debug] Quick load");
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                // Дать золото
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.AddGold(1000);
                    Debug.Log("[Debug] Added 1000 gold");
                }
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                // Дать опыт
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.stats.experience += 500;
                    if (player.stats.TryLevelUp())
                        Debug.Log($"[Debug] Level up! Now level {player.stats.level}");
                }
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                // Полное восстановление
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.stats.currentHP = player.stats.maxHP;
                    player.stats.currentMP = player.stats.maxMP;
                    Debug.Log("[Debug] Full restore");
                }
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                showDebugGUI = !showDebugGUI;
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                // Телепорт в лагерь
                if (GameManager.Instance.CurrentState == GameState.Exploration)
                {
                    Camp.CampManager.Instance?.EnterCamp();
                    Debug.Log("[Debug] Teleported to camp");
                }
            }
        }

        private void OnGUI()
        {
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 500));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>Debug Info</b>");

            var player = CharacterCreation.Instance?.Character;
            if (player != null)
            {
                GUILayout.Label($"Name: {player.playerName}");
                GUILayout.Label($"Level: {player.stats.level}");
                GUILayout.Label($"HP: {player.stats.currentHP}/{player.stats.maxHP}");
                GUILayout.Label($"MP: {player.stats.currentMP}/{player.stats.maxMP}");
                GUILayout.Label($"Gold: {player.gold}");
                GUILayout.Label($"EXP: {player.stats.experience}/{player.stats.experienceToNextLevel}");
            }

            GUILayout.Space(10);
            GUILayout.Label($"State: {GameManager.Instance?.CurrentState}");
            GUILayout.Label($"Day: {GameManager.Instance?.CurrentDay}");
            GUILayout.Label($"Time: {GameManager.Instance?.GameTimeHours:F1}h");

            GUILayout.Space(10);

            if (GUILayout.Button("Save (F1)"))
                SaveManager.Instance?.Save(0);

            if (GUILayout.Button("Load (F2)"))
                SaveManager.Instance?.Load(0);

            if (GUILayout.Button("+1000 Gold (F3)"))
            {
                player?.AddGold(1000);
            }

            if (GUILayout.Button("+500 EXP (F4)"))
            {
                if (player != null)
                {
                    player.stats.experience += 500;
                    player.stats.TryLevelUp();
                }
            }

            if (GUILayout.Button("Full Restore (F5)"))
            {
                if (player != null)
                {
                    player.stats.currentHP = player.stats.maxHP;
                    player.stats.currentMP = player.stats.maxMP;
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Менеджер локализации (упрощённый)
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        public static LocalizationManager Instance { get; private set; }

        [SerializeField] private string currentLanguage = "ru";
        private Dictionary<string, Dictionary<string, string>> localizedTexts = new();

        public string CurrentLanguage => currentLanguage;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetLanguage(string language)
        {
            currentLanguage = language;
        }

        public string GetText(string key)
        {
            if (localizedTexts.TryGetValue(currentLanguage, out var langDict))
            {
                if (langDict.TryGetValue(key, out string text))
                    return text;
            }
            return key; // Возвращаем ключ если не найдено
        }

        public void LoadLocalizationData(string language, Dictionary<string, string> texts)
        {
            localizedTexts[language] = texts;
        }
    }

    /// <summary>
    /// Аудио менеджер
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource voiceSource;

        [Header("Settings")]
        [SerializeField] private float musicVolume = 0.5f;
        [SerializeField] private float sfxVolume = 0.8f;
        [SerializeField] private float voiceVolume = 1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (musicSource == null || clip == null) return;
            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        public void StopMusic()
        {
            if (musicSource != null)
                musicSource.Stop();
        }

        public void PlaySFX(AudioClip clip)
        {
            if (sfxSource == null || clip == null) return;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        public void PlayVoice(AudioClip clip)
        {
            if (voiceSource == null || clip == null) return;
            voiceSource.clip = clip;
            voiceSource.volume = voiceVolume;
            voiceSource.Play();
        }

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            if (musicSource != null)
                musicSource.volume = musicVolume;
        }

        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
        }

        public void SetVoiceVolume(float volume)
        {
            voiceVolume = Mathf.Clamp01(volume);
        }
    }
}
