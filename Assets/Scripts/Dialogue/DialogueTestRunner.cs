using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using System.Collections;
using RPG.Core;
using RPG.Character;
using RPG.UI;

namespace RPG.Dialogue
{
    /// <summary>
    /// Автоматический тестовый модуль для запуска Сцены 1: "Камера Очищения".
    /// Автоматически создаёт EventSystem, DialogueUI и тестового персонажа, если их нет в сцене.
    /// </summary>
    public class DialogueTestRunner : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private string dialogueToTest = "prologue_room1_purification_chamber";
        [SerializeField] private float delayBeforeStart = 0.5f;
        [SerializeField] private bool autoCreateTestCharacter = true;

        [Header("Test Character Profile")]
        [SerializeField] private string testName = "Рейна";
        [SerializeField] private RaceType testRace = RaceType.Tiefling;
        [SerializeField] private ClassType testClass = ClassType.Rogue;
        [SerializeField] private Gender testGender = Gender.Female;

        private void Start()
        {
            StartCoroutine(RunTestRoutine());
        }

        private IEnumerator RunTestRoutine()
        {
            yield return new WaitForSeconds(delayBeforeStart);

            Debug.Log("[DialogueTestRunner] Initializing automated dialogue test environment...");

            // 1. Убеждаемся, что EventSystem существует для обработки кликов мышью по кнопкам
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
                DontDestroyOnLoad(esGo);
                Debug.Log("[DialogueTestRunner] Created EventSystem with InputSystemUIInputModule.");
            }

            // 2. Убеждаемся, что DialogueUI существует в сцене
            var dialogueUI = FindObjectOfType<DialogueUI>();
            if (dialogueUI == null)
            {
                var uiGo = new GameObject("DialogueUI");
                dialogueUI = uiGo.AddComponent<DialogueUI>();
                DontDestroyOnLoad(uiGo);
                Debug.Log("[DialogueTestRunner] Created DialogueUI.");
            }

            // 3. Создаём и настраиваем тестового персонажа с хорошими статами для проверок
            if (autoCreateTestCharacter && CharacterCreation.Instance != null)
            {
                CharacterCreation.Instance.ResetCreation();
                CharacterCreation.Instance.SetName(testName);
                CharacterCreation.Instance.SelectRace(testRace);
                CharacterCreation.Instance.SelectClass(testClass);
                CharacterCreation.Instance.SetGender(testGender);

                var character = CharacterCreation.Instance.Character;
                if (character != null)
                {
                    character.stats.sleightOfHand = 2;      // Ловкость рук (взлом)
                    character.stats.attentiveness = 2;      // Внимательность / Проницательность (наркотик Елей)
                    character.stats.magic = 1;              // Магия / Аркана (машина)
                    character.stats.trickery = 2;           // Плутовство / Соблазнение (NSFW)
                    character.stats.bodyPower = 1;          // Мощность тела (вырыв кандалов)
                    character.stats.bodyKnowledge = 1;      // Знания тела
                    character.stats.RecalculateDerivedStats();
                }
                Debug.Log($"[DialogueTestRunner] Configured Player: {testName} ({testRace} {testClass}, {testGender}) | HP: {character?.stats.currentHP}");
            }

            // Ждем один кадр, чтобы методы Start() у всех созданных UI компонентов гарантированно выполнились
            yield return null;

            // 4. Запускаем тестовый диалог
            if (DialogueManager.Instance != null)
            {
                if (!DialogueManager.Instance.HasDialogue(dialogueToTest))
                {
                    Debug.Log("[DialogueTestRunner] Dialogue not found yet, explicitly invoking LoadAllDialogues()...");
                    DialogueManager.Instance.LoadAllDialogues();
                }

                Debug.Log($"[DialogueTestRunner] Launching dialogue: {dialogueToTest}");
                DialogueManager.Instance.StartDialogue(dialogueToTest);
            }
            else
            {
                Debug.LogError("[DialogueTestRunner] DialogueManager.Instance is NULL! Ensure GameBootstrap is initialized.");
            }
        }
    }
}
