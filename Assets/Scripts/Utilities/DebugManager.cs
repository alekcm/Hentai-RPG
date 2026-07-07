using UnityEngine;
using System;
using RPG.Core;
using RPG.Character;

namespace RPG.Utilities
{
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

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            // Быстрые клавиши для отладки
            if (kb.f1Key.wasPressedThisFrame)
            {
                SaveManager.Instance?.Save(0);
                Debug.Log("[Debug] Quick save");
            }

            if (kb.f2Key.wasPressedThisFrame)
            {
                SaveManager.Instance?.Load(0);
                Debug.Log("[Debug] Quick load");
            }

            if (kb.f3Key.wasPressedThisFrame)
            {
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.AddGold(1000);
                    Debug.Log("[Debug] Added 1000 gold");
                }
            }

            if (kb.f4Key.wasPressedThisFrame)
            {
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.stats.experience += 500;
                    if (player.stats.TryLevelUp())
                        Debug.Log($"[Debug] Level up! Now level {player.stats.level}");
                }
            }

            if (kb.f5Key.wasPressedThisFrame)
            {
                var player = CharacterCreation.Instance?.Character;
                if (player != null)
                {
                    player.stats.currentHP = player.stats.maxHP;
                    player.stats.currentMP = player.stats.maxMP;
                    Debug.Log("[Debug] Full restore");
                }
            }

            if (kb.f9Key.wasPressedThisFrame)
            {
                showDebugGUI = !showDebugGUI;
            }

            if (kb.f10Key.wasPressedThisFrame)
            {
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
}
