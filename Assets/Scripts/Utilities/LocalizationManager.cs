using UnityEngine;
using System.Collections.Generic;

namespace RPG.Utilities
{
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
}
