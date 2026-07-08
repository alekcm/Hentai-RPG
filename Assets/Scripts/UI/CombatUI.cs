using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RPG.Combat;
using RPG.Character;
using RPG.Items;
using RPG.Core;

namespace RPG.UI
{
    /// <summary>
    /// Минималистичный UI боя для теста правил ГДД (не тактическая сетка!).
    /// Показывает:
    ///  — Пулы Надежды и Страха.
    ///  — Портреты юнитов со шкалами здоровья/брони/выносливости и «Уклонением».
    ///  — Кнопки действий выбранного своего юнита: Атака (выбор цели), Короткая передышка, Завершить ход.
    ///  — Текстовый лог событий.
    /// Автоматически поднимается сам при OnCombatStart, скрывается при OnCombatEnd.
    /// </summary>
    public class CombatUI : MonoBehaviour
    {
        private static CombatUI _instance;
        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("CombatUI");
            _instance = go.AddComponent<CombatUI>();
            DontDestroyOnLoad(go);
        }

        private GameObject root;
        private Text resourcesText;
        private Text sideText;
        private Text logText;
        private Transform unitsRow;
        private Transform actionsRow;
        private List<GameObject> unitCards = new();

        private CombatUnit currentTargetPick;
        private bool targetPickMode;
        private System.Action<CombatUnit> onTargetPicked;
        private readonly Queue<string> logLines = new();

        private void Awake()
        {
            _instance = this;
            BuildUI();
            root.SetActive(false);

            var cm = CombatManager.Instance;
            if (cm != null) HookCombat(cm);
        }

        private void Start()
        {
            var cm = CombatManager.Instance;
            if (cm != null) HookCombat(cm);
        }

        private bool hooked;
        private void HookCombat(CombatManager cm)
        {
            if (hooked) return;
            hooked = true;
            cm.OnCombatStart += HandleStart;
            cm.OnCombatEnd += HandleEnd;
            cm.OnResourcesChanged += RefreshResources;
            cm.OnUnitActivated += _ => RefreshUnits();
            cm.OnTurnPassedToSide += side => { sideText.text = side == CombatSide.Player ? "Ход: игрок" : "Ход: враг"; RefreshUnits(); };
            cm.OnCombatEvent += entry =>
            {
                logLines.Enqueue(entry.message);
                while (logLines.Count > 10) logLines.Dequeue();
                logText.text = string.Join("\n", logLines);
            };
        }

        private void HandleStart()
        {
            root.SetActive(true);
            RefreshResources();
            RefreshUnits();
        }

        private void HandleEnd(bool _)
        {
            root.SetActive(false);
        }

        // ---------- Построение UI ----------

        private void BuildUI()
        {
            var canvasGO = new GameObject("CombatUICanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            root = canvasGO;

            // Верхняя панель ресурсов.
            var top = MakePanel(canvasGO.transform, "Top", new Vector2(0, 40), new Vector2(1200, 60));
            top.anchoredPosition = new Vector2(0, -30 + (Screen.height / 2f) - 30);
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.anchoredPosition = new Vector2(0, -10);
            top.sizeDelta = new Vector2(1000, 60);

            resourcesText = MakeText(top, "Res", Vector2.zero, 20, FontStyle.Bold);
            resourcesText.rectTransform.sizeDelta = new Vector2(500, 50);
            resourcesText.rectTransform.anchoredPosition = new Vector2(-200, 0);

            sideText = MakeText(top, "Side", Vector2.zero, 20, FontStyle.Italic);
            sideText.rectTransform.sizeDelta = new Vector2(300, 50);
            sideText.rectTransform.anchoredPosition = new Vector2(250, 0);

            // Ряд карточек юнитов.
            var unitsPanel = MakePanel(canvasGO.transform, "Units", Vector2.zero, new Vector2(1200, 200));
            unitsPanel.anchorMin = unitsPanel.anchorMax = new Vector2(0.5f, 1f);
            unitsPanel.pivot = new Vector2(0.5f, 1f);
            unitsPanel.anchoredPosition = new Vector2(0, -80);
            unitsPanel.sizeDelta = new Vector2(1200, 200);
            var hg = unitsPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(20, 20, 20, 20);
            hg.spacing = 12;
            hg.childAlignment = TextAnchor.MiddleCenter;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;
            unitsRow = unitsPanel;

            // Ряд действий.
            var actionsPanel = MakePanel(canvasGO.transform, "Actions", Vector2.zero, new Vector2(900, 80));
            actionsPanel.anchorMin = actionsPanel.anchorMax = new Vector2(0.5f, 0f);
            actionsPanel.pivot = new Vector2(0.5f, 0f);
            actionsPanel.anchoredPosition = new Vector2(0, 20);
            actionsPanel.sizeDelta = new Vector2(900, 80);
            var hg2 = actionsPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            hg2.padding = new RectOffset(10, 10, 10, 10);
            hg2.spacing = 10;
            hg2.childAlignment = TextAnchor.MiddleCenter;
            hg2.childForceExpandWidth = false; hg2.childForceExpandHeight = false;
            actionsRow = actionsPanel;

            MakeActionButton(actionsRow, "Атаковать", () => BeginTargetPick());
            MakeActionButton(actionsRow, "Короткая передышка", DoShortRest);
            MakeActionButton(actionsRow, "Пропустить ход", () => EndTurn(false));

            // Лог.
            var logPanel = MakePanel(canvasGO.transform, "Log", Vector2.zero, new Vector2(400, 280));
            logPanel.anchorMin = logPanel.anchorMax = new Vector2(1f, 0.5f);
            logPanel.pivot = new Vector2(1f, 0.5f);
            logPanel.anchoredPosition = new Vector2(-10, 0);
            logPanel.sizeDelta = new Vector2(400, 320);
            logText = MakeText(logPanel, "LogText", Vector2.zero, 13, FontStyle.Normal);
            logText.rectTransform.sizeDelta = new Vector2(380, 300);
            logText.alignment = TextAnchor.UpperLeft;
            logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static RectTransform MakePanel(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.12f, 0.85f);
            return rt;
        }

        private static Text MakeText(RectTransform parent, string name, Vector2 pos, int size, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(200, 30);
            t.rectTransform.anchoredPosition = pos;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private static Button MakeActionButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 44);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.40f, 1f);
            var btn = go.AddComponent<Button>();
            var t = MakeText(rt, "Lbl", Vector2.zero, 14, FontStyle.Bold);
            t.text = label;
            t.rectTransform.sizeDelta = rt.sizeDelta;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        // ---------- Отрисовка ----------

        private void RefreshResources()
        {
            var cm = CombatManager.Instance;
            if (cm == null) { resourcesText.text = ""; return; }
            resourcesText.text = $"Надежда: {cm.HopePool}    Страх: {cm.FearPool}";
        }

        private void RefreshUnits()
        {
            foreach (var c in unitCards) Destroy(c);
            unitCards.Clear();

            var cm = CombatManager.Instance;
            if (cm == null) return;

            foreach (var u in cm.AllUnits)
            {
                var card = new GameObject($"Unit_{u.unitId}");
                card.transform.SetParent(unitsRow, false);
                var rt = card.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(150, 160);
                var img = card.AddComponent<Image>();
                img.color = u.side == CombatSide.Player
                    ? new Color(0.15f, 0.32f, 0.20f, 1f)
                    : new Color(0.35f, 0.15f, 0.15f, 1f);
                if (u == cm.SelectedUnit)
                    img.color += new Color(0.15f, 0.15f, 0.0f, 0f);

                var name = MakeText(rt, "Name", new Vector2(0, 65), 14, FontStyle.Bold);
                name.text = u.displayName + (u.IsDead ? " ✝" : "");
                name.rectTransform.sizeDelta = new Vector2(140, 24);

                var sb = new StringBuilder();
                sb.AppendLine($"HP: {u.stats.maxHealthSlots - u.stats.usedHealthSlots}/{u.stats.maxHealthSlots}");
                sb.AppendLine($"  ({u.stats.currentSlotHp}/{u.stats.hpPerSlot} HP в шкале)");
                if (u.stats.maxArmorSlots > 0)
                    sb.AppendLine($"Броня: {u.stats.maxArmorSlots - u.stats.usedArmorSlots}/{u.stats.maxArmorSlots} (DT {u.stats.damageThreshold})");
                else
                    sb.AppendLine("Броня: —");
                sb.AppendLine($"Уклонение: {u.stats.evasion}");
                sb.AppendLine($"Выносл.: {u.stats.currentStamina}/{u.stats.maxStamina}");
                var body = MakeText(rt, "Body", new Vector2(0, -5), 11, FontStyle.Normal);
                body.text = sb.ToString();
                body.alignment = TextAnchor.UpperCenter;
                body.rectTransform.sizeDelta = new Vector2(140, 90);

                var btn = card.AddComponent<Button>();
                var unitRef = u;
                btn.onClick.AddListener(() => OnUnitCardClick(unitRef));

                unitCards.Add(card);
            }
        }

        // ---------- Действия ----------

        private void OnUnitCardClick(CombatUnit u)
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return;

            if (targetPickMode)
            {
                if (u.side == CombatSide.Enemy && !u.IsDead)
                {
                    targetPickMode = false;
                    var cb = onTargetPicked;
                    onTargetPicked = null;
                    cb?.Invoke(u);
                }
                return;
            }

            // Обычный клик — активировать своего юнита.
            if (u.side == CombatSide.Player && cm.ActiveSide == CombatSide.Player)
            {
                cm.ActivatePlayerUnit(u.unitId);
                RefreshUnits();
            }
        }

        private void BeginTargetPick()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            targetPickMode = true;
            onTargetPicked = target =>
            {
                var res = cm.PerformAttack(cm.SelectedUnit, target);
                bool successHope = res.hit && res.roll.HopeSide;
                RefreshUnits();
                cm.FinishUnitTurn(cm.SelectedUnit, successHope);
                RefreshUnits();
            };
        }

        private void DoShortRest()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;

            AbilityConfirmDialog.Show(
                title: "Короткая передышка",
                description: "Бросок Дуальности. Если Надежда > Страха — Выносливость +1 и ход остаётся. Иначе — Выносливость +1, ход переходит врагу.",
                resources: $"Ресурсы: Надежда {cm.HopePool}, Страх {cm.FearPool}",
                yes: () =>
                {
                    var r = cm.PerformShortRest(cm.SelectedUnit);
                    RefreshUnits();
                    if (r.consumesAction)
                        cm.FinishUnitTurn(cm.SelectedUnit, false);
                    RefreshUnits();
                });
        }

        private void EndTurn(bool hopeSuccess)
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            cm.FinishUnitTurn(cm.SelectedUnit, hopeSuccess);
            RefreshUnits();
        }
    }
}
