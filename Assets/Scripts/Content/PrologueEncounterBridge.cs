using System.Collections.Generic;
using UnityEngine;
using RPG.Dialogue;
using RPG.Combat;
using RPG.Core;

namespace RPG.Content
{
    /// <summary>
    /// Слушает переходы по узлам диалогов и запускает бой на ключевых узлах пролога.
    /// Нужен, чтобы не изменять существующий контент диалогов пролога (в них уже сложная разветвлённая логика).
    /// Когда игрок доходит до узла-триггера в первый раз — стартует соответствующий encounter из
    /// StreamingAssets/Encounters/&lt;id&gt;.json.
    /// </summary>
    public class PrologueEncounterBridge : MonoBehaviour
    {
        // nodeId → encounterId
        private static readonly Dictionary<string, string> NodeToEncounter = new()
        {
            { "r2_46_lust_manifest", "prologue_r2_guards_and_lust" },
            { "r2_13_fear_eruption", "prologue_r2_guards_and_fear" }
        };

        private readonly HashSet<string> triggeredThisSession = new();
        private bool hooked;

        private void OnEnable() => TryHook();
        private void Start()    => TryHook();

        private void TryHook()
        {
            if (hooked) return;
            if (DialogueManager.Instance == null) return;
            DialogueManager.Instance.OnNodePresented += OnNode;
            hooked = true;
        }

        private void OnDisable()
        {
            if (hooked && DialogueManager.Instance != null)
                DialogueManager.Instance.OnNodePresented -= OnNode;
            hooked = false;
        }

        private void OnNode(DialogueNode node)
        {
            if (node == null || CombatManager.Instance == null) return;
            if (!NodeToEncounter.TryGetValue(node.nodeId, out var encId)) return;
            // Триггерим только один раз на сессию — чтобы после победы, вернувшись в диалог, снова не запустилось.
            if (!triggeredThisSession.Add(node.nodeId)) return;

            Debug.Log($"[PrologueBridge] Node '{node.nodeId}' → запускаем бой '{encId}'.");
            CombatManager.Instance.StartCombat(encId);
        }
    }
}
