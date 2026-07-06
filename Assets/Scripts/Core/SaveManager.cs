using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace RPG.Core
{
    /// <summary>
    /// Менеджер сохранений. Сериализует/десериализует все данные игры.
    /// Поддерживает несколько слотов сохранения.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        [SerializeField] private int maxSaveSlots = 10;
        [SerializeField] private string saveFileName = "save_";

        private string SavePath => Path.Combine(Application.persistentDataPath, "Saves");

        // Список всех систем, которые участвуют в сохранении
        private List<ISaveable> saveables = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (!Directory.Exists(SavePath))
                Directory.CreateDirectory(SavePath);
        }

        public void RegisterSaveable(ISaveable saveable)
        {
            if (!saveables.Contains(saveable))
                saveables.Add(saveable);
        }

        public void UnregisterSaveable(ISaveable saveable)
        {
            saveables.Remove(saveable);
        }

        public void Save(int slot = 0)
        {
            if (slot < 0 || slot >= maxSaveSlots)
            {
                Debug.LogError($"[SaveManager] Invalid slot: {slot}");
                return;
            }

            SaveFile saveFile = new SaveFile
            {
                version = Application.version,
                saveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                playTimeSeconds = Time.time,
                slotIndex = slot
            };

            // Собираем данные от всех систем
            foreach (var saveable in saveables)
            {
                try
                {
                    string key = saveable.SaveKey;
                    string data = saveable.OnSave();
                    saveFile.data[key] = data;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] Error saving {saveable.SaveKey}: {e.Message}");
                }
            }

            string json = JsonUtility.ToJson(saveFile, true);
            string filePath = GetSaveFilePath(slot);
            File.WriteAllText(filePath, json);

            Debug.Log($"[SaveManager] Game saved to slot {slot}");
        }

        public bool Load(int slot = 0)
        {
            string filePath = GetSaveFilePath(slot);
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[SaveManager] No save file at slot {slot}");
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                SaveFile saveFile = JsonUtility.FromJson<SaveFile>(json);

                foreach (var saveable in saveables)
                {
                    try
                    {
                        string key = saveable.SaveKey;
                        if (saveFile.data.TryGetValue(key, out string data))
                        {
                            saveable.OnLoad(data);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[SaveManager] Error loading {saveable.SaveKey}: {e.Message}");
                    }
                }

                Debug.Log($"[SaveManager] Game loaded from slot {slot}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to load save: {e.Message}");
                return false;
            }
        }

        public bool HasSave(int slot)
        {
            return File.Exists(GetSaveFilePath(slot));
        }

        public void DeleteSave(int slot)
        {
            string filePath = GetSaveFilePath(slot);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        public SaveFileInfo GetSaveInfo(int slot)
        {
            string filePath = GetSaveFilePath(slot);
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = File.ReadAllText(filePath);
                SaveFile saveFile = JsonUtility.FromJson<SaveFile>(json);
                return new SaveFileInfo
                {
                    slot = slot,
                    saveDate = saveFile.saveDate,
                    playTimeSeconds = saveFile.playTimeSeconds,
                    version = saveFile.version
                };
            }
            catch
            {
                return null;
            }
        }

        private string GetSaveFilePath(int slot)
        {
            return Path.Combine(SavePath, $"{saveFileName}{slot}.json");
        }

        public List<SaveFileInfo> GetAllSaveInfos()
        {
            List<SaveFileInfo> infos = new();
            for (int i = 0; i < maxSaveSlots; i++)
            {
                var info = GetSaveInfo(i);
                if (info != null)
                    infos.Add(info);
            }
            return infos;
        }
    }

    /// <summary>
    /// Интерфейс для всех систем, которые участвуют в сохранении
    /// </summary>
    public interface ISaveable
    {
        string SaveKey { get; }
        string OnSave();
        void OnLoad(string data);
    }

    [Serializable]
    public class SaveFile
    {
        public string version;
        public string saveDate;
        public float playTimeSeconds;
        public int slotIndex;
        public SerializableDictionary data = new();
    }

    /// <summary>
    /// JsonUtility не умеет Dictionary, поэтому используем сериализуемый wrapper
    /// </summary>
    [Serializable]
    public class SerializableDictionary : Dictionary<string, string>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<string> keys = new();
        [SerializeField] private List<string> values = new();

        public void OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();
            foreach (var kvp in this)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            for (int i = 0; i < keys.Count && i < values.Count; i++)
            {
                this[keys[i]] = values[i];
            }
        }
    }

    [Serializable]
    public class SaveFileInfo
    {
        public int slot;
        public string saveDate;
        public float playTimeSeconds;
        public string version;
    }
}
