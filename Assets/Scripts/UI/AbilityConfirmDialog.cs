using System;
using UnityEngine;
using UnityEngine.UI;
using RPG.Combat;

namespace RPG.UI
{
    /// <summary>
    /// Модальное окно подтверждения способности, которая тратит Надежду/Выносливость
    /// или срабатывает по триггеру. Автоматически ставит бой "на паузу" — CombatManager
    /// не делает ничего, пока игрок не примет решение.
    ///
    /// Использование:
    ///   AbilityConfirmDialog.Show(
    ///       title: "Использовать «Я твой щит»?",
    ///       description: "Союзник получает урон. Потратить 1 Выносливость?",
    ///       yes: () => ApplyShield(),
    ///       no:  () => { /* пропустить */ });
    ///
    /// Не требует Canvas в сцене — создаёт свой на лету (в overlay-режиме).
    /// </summary>
    public class AbilityConfirmDialog : MonoBehaviour
    {
        private static AbilityConfirmDialog _instance;
        private static GameObject _canvasRoot;

        private Text titleText;
        private Text descText;
        private Text resourcesText;
        private Button yesButton;
        private Button noButton;

        private Action onYes;
        private Action onNo;

        /// <summary>Показывает модалку. Обязательно указывать yes; no можно оставить null.</summary>
        public static void Show(string title, string description, string resources, Action yes, Action no = null)
        {
            EnsureInstance();
            _instance.Configure(title, description, resources, yes, no);
        }

        public static bool IsShown => _instance != null && _instance.gameObject.activeSelf;

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            _canvasRoot = new GameObject("AbilityConfirmCanvas");
            DontDestroyOnLoad(_canvasRoot);
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            _canvasRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Фон-затемнение (блокирует клики).
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgRT = bg.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.55f);

            // Панель.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pRT = panel.AddComponent<RectTransform>();
            pRT.sizeDelta = new Vector2(560, 260);
            pRT.anchorMin = pRT.anchorMax = new Vector2(0.5f, 0.5f);
            pRT.anchoredPosition = Vector2.zero;
            var pImg = panel.AddComponent<Image>();
            pImg.color = new Color(0.10f, 0.10f, 0.14f, 0.98f);

            _instance = panel.AddComponent<AbilityConfirmDialog>();

            // Заголовок.
            _instance.titleText = MakeText(panel.transform, "Title", new Vector2(0, 90), 22, FontStyle.Bold);
            _instance.descText  = MakeText(panel.transform, "Desc",  new Vector2(0, 20), 16, FontStyle.Normal);
            _instance.descText.rectTransform.sizeDelta = new Vector2(520, 120);
            _instance.descText.alignment = TextAnchor.UpperCenter;
            _instance.resourcesText = MakeText(panel.transform, "Res",   new Vector2(0, -50), 14, FontStyle.Italic);

            _instance.yesButton = MakeButton(panel.transform, "Yes", "Использовать", new Vector2(-100, -95));
            _instance.noButton  = MakeButton(panel.transform, "No",  "Отмена",       new Vector2( 100, -95));

            _instance.yesButton.onClick.AddListener(() => _instance.Close(true));
            _instance.noButton.onClick.AddListener(()  => _instance.Close(false));

            _instance.gameObject.SetActive(false);
        }

        private static Text MakeText(Transform parent, string name, Vector2 pos, int size, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(520, 40);
            t.rectTransform.anchoredPosition = pos;
            return t;
        }

        private static Button MakeButton(Transform parent, string name, string label, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 44);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.25f, 0.34f, 1f);
            var btn = go.AddComponent<Button>();
            var t = MakeText(go.transform, "Label", Vector2.zero, 16, FontStyle.Bold);
            t.text = label;
            t.rectTransform.sizeDelta = rt.sizeDelta;
            return btn;
        }

        private void Configure(string title, string desc, string resources, Action yes, Action no)
        {
            titleText.text = title ?? "";
            descText.text = desc ?? "";
            resourcesText.text = resources ?? "";
            onYes = yes;
            onNo = no;
            gameObject.SetActive(true);
            // Ставим время на паузу — модалка блокирующая.
            Time.timeScale = 0f;
        }

        private void Close(bool yes)
        {
            Time.timeScale = 1f;
            gameObject.SetActive(false);
            try { if (yes) onYes?.Invoke(); else onNo?.Invoke(); }
            finally { onYes = null; onNo = null; }
        }
    }
}
