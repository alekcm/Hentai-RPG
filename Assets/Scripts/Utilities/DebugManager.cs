using UnityEngine;
using RPG.Core;
using RPG.Character;
using RPG.Camp;

namespace RPG.Utilities
{
    /// <summary>Debug менеджер для тестирования (адаптирован под ГДД-модель).</summary>
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

            if (kb.f1Key.wasPressedThisFrame) { SaveManager.Instance?.Save(0); Debug.Log("[Debug] Quick save"); }
            if (kb.f2Key.wasPressedThisFrame) { SaveManager.Instance?.Load(0); Debug.Log("[Debug] Quick load"); }
            if (kb.f3Key.wasPressedThisFrame)
            {
                var p = CharacterCreation.Instance?.Character;
                if (p != null) { p.AddGold(1000); Debug.Log("[Debug] +1000 gold"); }
            }
            if (kb.f5Key.wasPressedThisFrame) FullRestore();
            if (kb.f9Key.wasPressedThisFrame) showDebugGUI = !showDebugGUI;
            if (kb.f10Key.wasPressedThisFrame && GameManager.Instance.CurrentState == GameState.Exploration)
            {
                CampManager.Instance?.EnterCamp();
                Debug.Log("[Debug] Teleported to camp");
            }
        }

        private static void FullRestore()
        {
            var p = CharacterCreation.Instance?.Character;
            if (p == null) return;
            p.stats.usedHealthSlots = 0;
            p.stats.currentSlotHp = p.stats.hpPerSlot;
            p.stats.ResetArmorAtCombatStart();
            p.stats.ResetStaminaAtCombatStart();
            Debug.Log("[Debug] Full restore");
        }

        private void OnGUI()
        {
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 500));
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Debug Info</b>");

            var p = CharacterCreation.Instance?.Character;
            if (p != null)
            {
                GUILayout.Label($"Name: {p.playerName}");
                GUILayout.Label($"Race/Class: {p.race}/{p.characterClass}/{p.subclassId}");
                GUILayout.Label($"Level: {p.stats.level}");
                GUILayout.Label($"Health slots: {p.stats.maxHealthSlots - p.stats.usedHealthSlots}/{p.stats.maxHealthSlots} (curr slot HP {p.stats.currentSlotHp}/{p.stats.hpPerSlot})");
                GUILayout.Label($"Armor slots: {p.stats.maxArmorSlots - p.stats.usedArmorSlots}/{p.stats.maxArmorSlots} (DT {p.stats.damageThreshold})");
                GUILayout.Label($"Evasion: {p.stats.evasion}");
                GUILayout.Label($"Stamina: {p.stats.currentStamina}/{p.stats.maxStamina}");
                GUILayout.Label($"Gold: {p.gold}");
            }

            GUILayout.Space(10);
            GUILayout.Label($"State: {GameManager.Instance?.CurrentState}");
            GUILayout.Label($"Day: {GameManager.Instance?.CurrentDay}   Time: {GameManager.Instance?.GameTimeHours:F1}h");
            GUILayout.Space(10);

            if (GUILayout.Button("Save (F1)")) SaveManager.Instance?.Save(0);
            if (GUILayout.Button("Load (F2)")) SaveManager.Instance?.Load(0);
            if (GUILayout.Button("+1000 Gold (F3)")) p?.AddGold(1000);
            if (GUILayout.Button("Full Restore (F5)")) FullRestore();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
