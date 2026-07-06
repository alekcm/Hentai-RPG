using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using RPG.Core;
using RPG.Companion;
using RPG.LLM;

namespace RPG.UI
{
    /// <summary>
    /// UI для взаимодействия с компаньонами, включая LLM-диалоги.
    /// Используется в лагере и при исследовании.
    /// </summary>
    public class CompanionUI : MonoBehaviour
    {
        [Header("Main Panel")]
        [SerializeField] private GameObject companionPanel;
        [SerializeField] private Text companionNameText;
        [SerializeField] private Image companionPortrait;
        [SerializeField] private Text relationshipText;
        [SerializeField] private Slider affinitySlider;

        [Header("Chat Panel")]
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private Transform chatContentParent;
        [SerializeField] private GameObject chatMessagePrefab;
        [SerializeField] private GameObject playerMessagePrefab;
        [SerializeField] private ScrollRect chatScrollRect;

        [Header("Input")]
        [SerializeField] private InputField playerInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button endConversationButton;

        [Header("Quick Phrases")]
        [SerializeField] private Transform quickPhrasesParent;
        [SerializeField] private GameObject quickPhrasePrefab;

        [Header("Status")]
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject loadingIndicator;

        [Header("Settings")]
        [SerializeField] private float scrollDelay = 0.1f;
        [SerializeField] private int maxVisibleMessages = 50;

        private string currentCompanionId;
        private LLMDialogueProcessor llmProcessor;
        private List<GameObject> chatMessages = new();

        private void Start()
        {
            llmProcessor = LLMDialogueProcessor.Instance;
            if (llmProcessor == null)
            {
                Debug.LogError("[CompanionUI] LLMDialogueProcessor not found!");
                return;
            }

            llmProcessor.OnCompanionResponse += OnCompanionResponse;
            llmProcessor.OnPlayerMessageSent += OnPlayerMessageSent;
            llmProcessor.OnConversationEnded += OnConversationEnded;
            llmProcessor.OnError += OnLLMError;

            sendButton.onClick.AddListener(OnSendClicked);
            endConversationButton.onClick.AddListener(OnEndConversationClicked);
            playerInput.onEndEdit.AddListener(OnInputSubmit);

            companionPanel.SetActive(false);
            chatPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (llmProcessor != null)
            {
                llmProcessor.OnCompanionResponse -= OnCompanionResponse;
                llmProcessor.OnPlayerMessageSent -= OnPlayerMessageSent;
                llmProcessor.OnConversationEnded -= OnConversationEnded;
                llmProcessor.OnError -= OnLLMError;
            }
        }

        #region Panel Management

        public void ShowCompanionInfo(string companionId)
        {
            currentCompanionId = companionId;
            var companion = CompanionManager.Instance.GetCompanion(companionId);
            if (companion == null) return;

            companionPanel.SetActive(true);
            companionNameText.text = companion.displayName;

            var relationship = CompanionManager.Instance.GetRelationshipLevel(companionId);
            relationshipText.text = GetRelationshipDisplayText(relationship, companion.affinity);

            affinitySlider.value = (companion.affinity + 100) / 200f; // -100..100 -> 0..1

            // Обновляем быстрые фразы
            UpdateQuickPhrases(companion);
        }

        public void StartLLMConversation(string companionId)
        {
            if (!llmProcessor.CanTalkToCompanion(companionId))
            {
                ShowStatus("Сейчас нельзя поговорить");
                return;
            }

            currentCompanionId = companionId;
            chatPanel.SetActive(true);
            ClearChat();

            llmProcessor.StartConversation(companionId);

            var companion = CompanionManager.Instance.GetCompanion(companionId);
            ShowStatus($"Разговор с {companion.displayName}...");

            // Приветственное сообщение
            AddSystemMessage($"Вы начинаете разговор с {companion.displayName}.");
        }

        public void HidePanel()
        {
            if (llmProcessor.IsConversationActive)
            {
                llmProcessor.EndConversation();
            }

            companionPanel.SetActive(false);
            chatPanel.SetActive(false);
        }

        #endregion

        #region Chat

        private void OnSendClicked()
        {
            SendMessage();
        }

        private void OnInputSubmit(string text)
        {
            if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))
            {
                SendMessage();
            }
        }

        private void SendMessage()
        {
            string message = playerInput.text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            if (!llmProcessor.IsConversationActive)
                return;

            playerInput.text = "";
            llmProcessor.SendPlayerMessage(message);
        }

        private void OnPlayerMessageSent(string message)
        {
            AddChatMessage(message, true);
            ShowLoading(true);
        }

        private void OnCompanionResponse(string response)
        {
            ShowLoading(false);
            AddChatMessage(response, false);
        }

        private void OnLLMError(string error)
        {
            ShowLoading(false);
            AddSystemMessage(error);
        }

        private void OnConversationEnded(LLMConversationResult result)
        {
            ShowLoading(false);

            if (result.HasGameplayImpact)
            {
                string impactText = "Разговор завершён.\n";
                if (result.affinityChange != 0)
                    impactText += $"Отношение: {(result.affinityChange > 0 ? "+" : "")}{result.affinityChange}\n";
                if (result.setFlags.Count > 0)
                    impactText += "Этот разговор повлияет на будущие диалоги.\n";
                if (result.hadNSFWContent)
                    impactText += "🔥 Интимная сцена записана.\n";

                AddSystemMessage(impactText);
            }
            else
            {
                AddSystemMessage("Разговор завершён.");
            }

            // Обновляем информацию о компаньоне
            if (!string.IsNullOrEmpty(currentCompanionId))
                ShowCompanionInfo(currentCompanionId);
        }

        private void OnEndConversationClicked()
        {
            if (llmProcessor.IsConversationActive)
            {
                llmProcessor.EndConversation();
            }
            chatPanel.SetActive(false);
        }

        #endregion

        #region Chat UI Helpers

        private void AddChatMessage(string text, bool isPlayer)
        {
            var prefab = isPlayer ? playerMessagePrefab : chatMessagePrefab;
            var msgObj = Instantiate(prefab, chatContentParent);
            var msgText = msgObj.GetComponentInChildren<Text>();
            msgText.text = text;

            // Добавляем имя
            if (!isPlayer)
            {
                var companion = CompanionManager.Instance.GetCompanion(currentCompanionId);
                if (companion != null)
                {
                    var nameObj = msgObj.transform.Find("SenderName");
                    if (nameObj != null)
                        nameObj.GetComponent<Text>().text = companion.displayName;
                }
            }

            chatMessages.Add(msgObj);

            // Ограничиваем количество
            while (chatMessages.Count > maxVisibleMessages)
            {
                Destroy(chatMessages[0]);
                chatMessages.RemoveAt(0);
            }

            StartCoroutine(ScrollToBottom());
        }

        private void AddSystemMessage(string text)
        {
            var msgObj = Instantiate(chatMessagePrefab, chatContentParent);
            var msgText = msgObj.GetComponentInChildren<Text>();
            msgText.text = $"<i><color=#888888>{text}</color></i>";
            chatMessages.Add(msgObj);
            StartCoroutine(ScrollToBottom());
        }

        private void ClearChat()
        {
            foreach (var msg in chatMessages)
            {
                if (msg != null)
                    Destroy(msg);
            }
            chatMessages.Clear();
        }

        private IEnumerator ScrollToBottom()
        {
            yield return new WaitForSeconds(scrollDelay);
            chatScrollRect.verticalNormalizedPosition = 0f;
        }

        private void ShowLoading(bool show)
        {
            loadingIndicator.SetActive(show);
            sendButton.interactable = !show;
            statusText.text = show ? "Печатает..." : "";
        }

        private void ShowStatus(string text)
        {
            statusText.text = text;
        }

        #endregion

        #region Quick Phrases

        private void UpdateQuickPhrases(CompanionData companion)
        {
            // Очищаем
            foreach (Transform child in quickPhrasesParent)
                Destroy(child.gameObject);

            // Генерируем контекстные фразы
            var phrases = GetContextualPhrases(companion);
            foreach (var phrase in phrases)
            {
                var btnObj = Instantiate(quickPhrasePrefab, quickPhrasesParent);
                var btnText = btnObj.GetComponentInChildren<Text>();
                btnText.text = phrase;
                var btn = btnObj.GetComponent<Button>();

                var capturedPhrase = phrase;
                btn.onClick.AddListener(() =>
                {
                    playerInput.text = capturedPhrase;
                    SendMessage();
                });
            }
        }

        private List<string> GetContextualPhrases(CompanionData companion)
        {
            var phrases = new List<string>();

            var relationship = CompanionManager.Instance.GetRelationshipLevel(companion.companionId);

            phrases.Add("Как ты себя чувствуешь?");
            phrases.Add("Расскажи о себе.");

            switch (relationship)
            {
                case CompanionRelationship.Neutral:
                    phrases.Add("Что ты думаешь о нашем путешествии?");
                    phrases.Add("Доверяешь ли ты мне?");
                    break;
                case CompanionRelationship.Warm:
                case CompanionRelationship.Friendly:
                    phrases.Add("Ты мне нравишься.");
                    phrases.Add("Почему ты присоединилась к нам?");
                    break;
                case CompanionRelationship.Devoted:
                    phrases.Add("Ты особенная для меня.");
                    phrases.Add("Хочешь побыть наедине?");
                    break;
                case CompanionRelationship.Cold:
                case CompanionRelationship.Hostile:
                    phrases.Add("Что я могу сделать, чтобы ты мне доверяла?");
                    phrases.Add("Прости, если я тебя обидел.");
                    break;
            }

            if (companion.isRomanced)
            {
                phrases.Add("Иди сюда...");
                phrases.Add("Я скучал по тебе.");
            }

            return phrases;
        }

        #endregion

        #region Helpers

        private string GetRelationshipDisplayText(CompanionRelationship relationship, int affinity)
        {
            string relText = relationship switch
            {
                CompanionRelationship.Hostile => "Враждебна",
                CompanionRelationship.Cold => "Холодна",
                CompanionRelationship.Neutral => "Нейтральна",
                CompanionRelationship.Warm => "Тепло относится",
                CompanionRelationship.Friendly => "Дружелюбна",
                CompanionRelationship.Devoted => "Предана",
                _ => "Неизвестно"
            };
            return $"{relText} ({affinity:+0;-0})";
        }

        #endregion
    }
}
