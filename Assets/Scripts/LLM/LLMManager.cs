using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using RPG.Core;

namespace RPG.LLM
{
    /// <summary>
    /// Менеджер LLM-интеграции. Отправляет запросы к API (OpenAI, локальные модели, etc.)
    /// и обрабатывает ответы. Поддерживает стриминг ответов.
    /// </summary>
    public class LLMManager : MonoBehaviour
    {
        public static LLMManager Instance { get; private set; }

        [Header("API Settings")]
        [SerializeField] private LLMProvider provider = LLMProvider.Local_Ollama;
        [SerializeField] private string apiUrl = "http://localhost:11434/api/chat";
        [SerializeField] private string apiKey = ""; // устанавливается в runtime
        [SerializeField] private string modelName = "hf.co/QuantFactory/Qwen2.5-7B-Instruct-Uncensored-GGUF:Q4_K_M";

        [Header("NSFW Model (separate for explicit content)")]
        [SerializeField] private string nsfwModelName = "hf.co/QuantFactory/Qwen2.5-7B-Instruct-Uncensored-GGUF:Q4_K_M";
        [SerializeField] private string nsfwApiUrl = "http://localhost:11434/api/chat";
        private bool useNsfwModel;

        [Header("Generation Settings")]
        [SerializeField] private float temperature = 0.8f;
        [SerializeField] private int maxTokens = 1000;
        [SerializeField] private float topP = 0.9f;
        [SerializeField] private float frequencyPenalty = 0.3f;
        [SerializeField] private float presencePenalty = 0.2f;

        [Header("Limits")]
        [SerializeField] private int maxConversationHistory = 20;
        [SerializeField] private float requestTimeout = 30f;
        [SerializeField] private int maxRetries = 2;

        private Queue<LLMRequest> requestQueue = new();
        private bool isProcessing;

        public bool IsProcessing => isProcessing;
        public event Action<string> OnResponseReceived;
        public event Action<string> OnStreamChunk;
        public event Action<string> OnError;

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

        public void SetApiKey(string key)
        {
            apiKey = key;
        }

        public void SetProvider(LLMProvider newProvider, string url = null, string model = null)
        {
            provider = newProvider;
            if (!string.IsNullOrEmpty(url)) apiUrl = url;
            if (!string.IsNullOrEmpty(model)) modelName = model;

            // Автоматически настраиваем URL для известных провайдеров
            switch (provider)
            {
                case LLMProvider.OpenAI:
                    if (string.IsNullOrEmpty(url)) apiUrl = "https://api.openai.com/v1/chat/completions";
                    break;
                case LLMProvider.Local_Ollama:
                    if (string.IsNullOrEmpty(url)) apiUrl = "http://localhost:11434/api/chat";
                    break;
                case LLMProvider.Local_LMStudio:
                    if (string.IsNullOrEmpty(url)) apiUrl = "http://localhost:1234/v1/chat/completions";
                    break;
                case LLMProvider.Anthropic:
                    if (string.IsNullOrEmpty(url)) apiUrl = "https://api.anthropic.com/v1/messages";
                    break;
            }
        }

        /// <summary>
        /// Включить/выключить NSFW модель для следующего запроса.
        /// Вызывать перед SendRequest если нужен NSFW контент.
        /// </summary>
        public void UseNsfwModelForNextRequest(bool use = true)
        {
            useNsfwModel = use;
        }

        /// <summary>
        /// Настроить NSFW модель
        /// </summary>
        public void SetNsfwModel(string model, string url = null)
        {
            nsfwModelName = model;
            if (!string.IsNullOrEmpty(url)) nsfwApiUrl = url;
        }

        #region Request Sending

        /// <summary>
        /// Отправить запрос к LLM и получить ответ через callback
        /// </summary>
        public void SendRequest(LLMRequest request, Action<string> onSuccess, Action<string> onError = null)
        {
            request.onSuccess = onSuccess;
            request.onError = onError ?? (e => Debug.LogError($"[LLM] Error: {e}"));
            requestQueue.Enqueue(request);

            if (!isProcessing)
                StartCoroutine(ProcessQueue());
        }

        /// <summary>
        /// Быстрый запрос: system prompt + user message
        /// </summary>
        public void QuickRequest(string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError = null)
        {
            var request = new LLMRequest
            {
                messages = new List<LLMMessage>
                {
                    new LLMMessage { role = "system", content = systemPrompt },
                    new LLMMessage { role = "user", content = userMessage }
                }
            };
            SendRequest(request, onSuccess, onError);
        }

        private IEnumerator ProcessQueue()
        {
            isProcessing = true;

            while (requestQueue.Count > 0)
            {
                var request = requestQueue.Dequeue();
                yield return StartCoroutine(ExecuteRequest(request));
            }

            isProcessing = false;
        }

        private IEnumerator ExecuteRequest(LLMRequest request)
        {
            int retries = 0;
            bool success = false;

            // Определяем какую модель и URL использовать
            string activeModel = useNsfwModel ? nsfwModelName : modelName;
            string activeUrl = useNsfwModel ? nsfwApiUrl : apiUrl;

            // Сбрасываем флаг после использования
            bool wasNsfw = useNsfwModel;
            useNsfwModel = false;

            while (retries <= maxRetries && !success)
            {
                string requestBody = BuildRequestBody(request, activeModel);

                using (var webRequest = new UnityWebRequest(activeUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.timeout = (int)requestTimeout;

                    // Headers
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    SetAuthHeaders(webRequest);

                    var operation = webRequest.SendWebRequest();
                    yield return operation;

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string response = ParseResponse(webRequest.downloadHandler.text);
                        success = true;

                        request.onSuccess?.Invoke(response);
                        OnResponseReceived?.Invoke(response);
                    }
                    else
                    {
                        string error = webRequest.error;
                        retries++;

                        if (retries > maxRetries)
                        {
                            request.onError?.Invoke(error);
                            OnError?.Invoke(error);
                            Debug.LogError($"[LLM] Request failed after {maxRetries} retries: {error}");
                        }
                        else
                        {
                            Debug.LogWarning($"[LLM] Request failed, retrying ({retries}/{maxRetries}): {error}");
                            yield return new WaitForSeconds(1f * retries);
                        }
                    }
                }
            }
        }

        #endregion

        #region Request/Response Building

        private string BuildRequestBody(LLMRequest request, string activeModel)
        {
            switch (provider)
            {
                case LLMProvider.OpenAI:
                case LLMProvider.Local_LMStudio:
                case LLMProvider.Local_Ollama:
                    return BuildOpenAIRequestBody(request, activeModel);
                case LLMProvider.Anthropic:
                    return BuildAnthropicRequestBody(request, activeModel);
                default:
                    return BuildOpenAIRequestBody(request, activeModel);
            }
        }

        private string BuildOpenAIRequestBody(LLMRequest request, string activeModel)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{activeModel}\",");
            sb.Append("\"messages\":[");

            for (int i = 0; i < request.messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var msg = request.messages[i];
                sb.Append("{");
                sb.Append($"\"role\":\"{EscapeJson(msg.role)}\",");
                sb.Append($"\"content\":\"{EscapeJson(msg.content)}\"");
                sb.Append("}");
            }

            sb.Append("],");
            sb.Append($"\"temperature\":{temperature},");
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append($"\"top_p\":{topP},");
            sb.Append($"\"frequency_penalty\":{frequencyPenalty},");
            sb.Append($"\"presence_penalty\":{presencePenalty}");

            // Response format для структурированных ответов
            if (request.useJsonFormat)
            {
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private string BuildAnthropicRequestBody(LLMRequest request, string activeModel)
        {
            // Anthropic использует немного другой формат
            var systemMsg = request.messages.Find(m => m.role == "system");
            var otherMsgs = request.messages.FindAll(m => m.role != "system");

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{activeModel}\",");
            if (systemMsg != null)
                sb.Append($"\"system\":\"{EscapeJson(systemMsg.content)}\",");
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append("\"messages\":[");

            for (int i = 0; i < otherMsgs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var msg = otherMsgs[i];
                sb.Append("{");
                sb.Append($"\"role\":\"{EscapeJson(msg.role)}\",");
                sb.Append($"\"content\":\"{EscapeJson(msg.content)}\"");
                sb.Append("}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private string ParseResponse(string jsonResponse)
        {
            try
            {
                // Простой парсинг JSON ответа
                // В production лучше использовать Newtonsoft.Json
                var response = JsonUtility.FromJson<OpenAIResponse>(jsonResponse);
                if (response?.choices != null && response.choices.Length > 0)
                {
                    return response.choices[0].message.content;
                }

                // Anthropic формат
                var anthropicResponse = JsonUtility.FromJson<AnthropicResponse>(jsonResponse);
                if (anthropicResponse?.content != null && anthropicResponse.content.Length > 0)
                {
                    return anthropicResponse.content[0].text;
                }

                return jsonResponse; // вернуть как есть если не удалось распарсить
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM] Failed to parse response: {e.Message}");
                return jsonResponse;
            }
        }

        private void SetAuthHeaders(UnityWebRequest request)
        {
            switch (provider)
            {
                case LLMProvider.OpenAI:
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    break;
                case LLMProvider.Anthropic:
                    request.SetRequestHeader("x-api-key", apiKey);
                    request.SetRequestHeader("anthropic-version", "2023-06-01");
                    break;
                case LLMProvider.Local_Ollama:
                case LLMProvider.Local_LMStudio:
                    // Локальные модели обычно не требуют авторизации
                    break;
            }
        }

        private string EscapeJson(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        #endregion
    }

    #region Data Types

    public enum LLMProvider
    {
        OpenAI,
        Anthropic,
        Local_Ollama,
        Local_LMStudio,
        Custom
    }

    [Serializable]
    public class LLMRequest
    {
        public List<LLMMessage> messages = new();
        public bool useJsonFormat;
        public Action<string> onSuccess;
        public Action<string> onError;
    }

    [Serializable]
    public class LLMMessage
    {
        public string role; // "system", "user", "assistant"
        public string content;
    }

    // OpenAI Response format
    [Serializable]
    public class OpenAIResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    public class Choice
    {
        public Message message;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    // Anthropic Response format
    [Serializable]
    public class AnthropicResponse
    {
        public ContentBlock[] content;
    }

    [Serializable]
    public class ContentBlock
    {
        public string type;
        public string text;
    }

    #endregion
}
