using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RPG.Combat;
using RPG.Items;

namespace RPG.UI
{
    /// <summary>
    /// HUD боя поверх GridCombatView.
    /// Показывает:
    ///  — Верхняя панель: пулы Надежды/Страха, чей ход.
    ///  — Левая нижняя карточка активного юнита (HP-шкалы, броня, DT, Уклонение, Выносливость, движение).
    ///  — Правая нижняя панель действий: Двигаться / Атаковать / Карты / Класс / Короткая передышка / Пропустить ход.
    ///  — Правый лог событий.
    /// Юнитов игрок выбирает кликом по спрайту НА СЕТКЕ (см. GridCombatView).
    /// Цели атаки — также на сетке.
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
        private Text selectedInfoText;
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
            cm.OnUnitActivated += _ => RefreshSelected();
            cm.OnTurnPassedToSide += side =>
            {
                sideText.text = side == CombatSide.Player ? "Ход: игрок" : "Ход: враг";
                RefreshSelected();
            };
            cm.OnCombatEvent += entry =>
            {
                logLines.Enqueue(entry.message);
                while (logLines.Count > 14) logLines.Dequeue();
                logText.text = string.Join("\n", logLines);
            };
        }

        private void HandleStart()
        {
            root.SetActive(true);
            RefreshResources();
            RefreshSelected();
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
            var top = MakePanel(canvasGO.transform, "Top", new Vector2(0, -10), new Vector2(1000, 60));
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 1f);
            top.pivot = new Vector2(0.5f, 1f);

            resourcesText = MakeText(top, "Res", Vector2.zero, 20, FontStyle.Bold);
            resourcesText.rectTransform.sizeDelta = new Vector2(500, 50);
            resourcesText.rectTransform.anchoredPosition = new Vector2(-200, 0);

            sideText = MakeText(top, "Side", Vector2.zero, 20, FontStyle.Italic);
            sideText.rectTransform.sizeDelta = new Vector2(300, 50);
            sideText.rectTransform.anchoredPosition = new Vector2(250, 0);

            // Карточка активного юнита (слева-внизу).
            var infoPanel = MakePanel(canvasGO.transform, "SelectedInfo", Vector2.zero, new Vector2(280, 180));
            infoPanel.anchorMin = infoPanel.anchorMax = new Vector2(0f, 0f);
            infoPanel.pivot = new Vector2(0f, 0f);
            infoPanel.anchoredPosition = new Vector2(10, 10);
            selectedInfoText = MakeText(infoPanel, "InfoText", Vector2.zero, 13, FontStyle.Normal);
            selectedInfoText.rectTransform.sizeDelta = new Vector2(260, 160);
            selectedInfoText.alignment = TextAnchor.UpperLeft;
            selectedInfoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            selectedInfoText.verticalOverflow = VerticalWrapMode.Overflow;

            // Панель действий (по центру-внизу).
            var actionsPanel = MakePanel(canvasGO.transform, "Actions", Vector2.zero, new Vector2(1000, 80));
            actionsPanel.anchorMin = actionsPanel.anchorMax = new Vector2(0.5f, 0f);
            actionsPanel.pivot = new Vector2(0.5f, 0f);
            actionsPanel.anchoredPosition = new Vector2(0, 10);
            var hg = actionsPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(10, 10, 10, 10);
            hg.spacing = 8;
            hg.childAlignment = TextAnchor.MiddleCenter;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;

            MakeActionButton(actionsPanel, "Двигаться",         DoMove);
            MakeActionButton(actionsPanel, "Атаковать",         DoAttack);
            MakeActionButton(actionsPanel, "Карты…",            ShowCardsPanel);
            MakeActionButton(actionsPanel, "Класс…",            ShowClassPanel);
            MakeActionButton(actionsPanel, "Кор. передышка",    DoShortRest);
            MakeActionButton(actionsPanel, "Пропустить ход",    () => EndTurn(false));

            // Лог (справа).
            var logPanel = MakePanel(canvasGO.transform, "Log", Vector2.zero, new Vector2(360, 340));
            logPanel.anchorMin = logPanel.anchorMax = new Vector2(1f, 0.5f);
            logPanel.pivot = new Vector2(1f, 0.5f);
            logPanel.anchoredPosition = new Vector2(-10, 0);
            logText = MakeText(logPanel, "LogText", Vector2.zero, 12, FontStyle.Normal);
            logText.rectTransform.sizeDelta = new Vector2(340, 320);
            logText.alignment = TextAnchor.UpperLeft;
            logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            logText.verticalOverflow = VerticalWrapMode.Overflow;
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
            rt.sizeDelta = new Vector2(150, 44);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.40f, 1f);
            var btn = go.AddComponent<Button>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 150; le.preferredHeight = 44;
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

        private void RefreshSelected()
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;
            var u = cm.SelectedUnit;
            if (u == null)
            {
                selectedInfoText.text = "Кликните по своему юниту на карте, чтобы активировать его.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"<b>{u.displayName}</b>");
            if (u.character != null)
                sb.AppendLine($"<i>{u.character.race} · {u.character.characterClass} · {u.character.subclassId}</i>");
            sb.AppendLine($"HP-шкал: {u.stats.maxHealthSlots - u.stats.usedHealthSlots}/{u.stats.maxHealthSlots}  " +
                          $"({u.stats.currentSlotHp}/{u.stats.hpPerSlot} в шкале)");
            if (u.stats.maxArmorSlots > 0)
                sb.AppendLine($"Броня: {u.stats.maxArmorSlots - u.stats.usedArmorSlots}/{u.stats.maxArmorSlots}  " +
                              $"(DT {u.stats.damageThreshold})");
            else
                sb.AppendLine("Броня: —");
            sb.AppendLine($"Уклонение: {u.stats.evasion}  ·  Выносл.: {u.stats.currentStamina}/{u.stats.maxStamina}");
            sb.AppendLine($"Движение: {cm.CurrentUnitMoveBudget} клеток");

            var weapon = ItemDatabase.GetWeapon(u.character?.equippedMainWeaponId) ?? u.fallbackWeapon;
            if (weapon != null)
                sb.AppendLine($"Оружие: {weapon.displayName} — {weapon.damageDice} ({weapon.range})");

            selectedInfoText.text = sb.ToString();
        }

        // ---------- Действия ----------

        private void DoMove()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            var grid = GridCombatView.Instance;
            if (grid == null) return;
            if (cm.CurrentUnitMoveBudget <= 0)
            {
                logLines.Enqueue("Движение исчерпано.");
                return;
            }
            grid.BeginMovePick(cm.SelectedUnit, cm.CurrentUnitMoveBudget);
        }

        private void DoAttack()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            var grid = GridCombatView.Instance;
            if (grid == null) return;

            var attacker = cm.SelectedUnit;
            var weapon = ItemDatabase.GetWeapon(attacker.character?.equippedMainWeaponId) ?? attacker.fallbackWeapon;
            int reach = weapon != null ? RangeInfo.Tiles(weapon.range) : 1;

            grid.BeginTargetPick(attacker, reach,
                filter: t => t != null && !t.IsDead && t.side == CombatSide.Enemy,
                onPicked: target =>
                {
                    var res = cm.PerformAttack(attacker, target);
                    bool successHope = res.hit && res.roll.HopeSide;
                    grid.RefreshAll();
                    cm.FinishUnitTurn(attacker, successHope);
                    RefreshSelected();
                    grid.RefreshAll();
                });
        }

        // ---------- Public bridge для CardsPanel ----------
        public static void RequestTargetPickPublic(System.Predicate<CombatUnit> filter,
                                                    System.Action<CombatUnit> onPicked,
                                                    System.Action onCancel)
        {
            EnsureExists();
            var grid = GridCombatView.Instance;
            var cm = CombatManager.Instance;
            if (grid != null && cm?.SelectedUnit != null)
            {
                grid.BeginTargetPick(cm.SelectedUnit, 24, filter, onPicked, onCancel);
                return;
            }
            // fallback: если сетки нет — просто передадим первую подходящую цель.
            foreach (var u in cm?.AllUnits ?? new List<CombatUnit>())
                if (filter == null || filter(u)) { onPicked?.Invoke(u); return; }
            onCancel?.Invoke();
        }

        private void ShowCardsPanel()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            CardsPanel.Show(cm.SelectedUnit, result =>
            {
                if (result == null) return;
                RefreshResources();
                RefreshSelected();
                GridCombatView.Instance?.RefreshAll();
                if (result.consumeAction)
                {
                    cm.FinishUnitTurn(cm.SelectedUnit, result.hopeSuccess);
                    RefreshSelected();
                }
            });
        }

        private void ShowClassPanel()
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            ClassActionsPanel.Show(cm.SelectedUnit, () =>
            {
                RefreshResources();
                RefreshSelected();
                GridCombatView.Instance?.RefreshAll();
            });
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
                    RefreshSelected();
                    GridCombatView.Instance?.RefreshAll();
                    if (r.consumesAction)
                        cm.FinishUnitTurn(cm.SelectedUnit, false);
                    RefreshSelected();
                });
        }

        private void EndTurn(bool hopeSuccess)
        {
            var cm = CombatManager.Instance;
            if (cm == null || cm.SelectedUnit == null) return;
            cm.FinishUnitTurn(cm.SelectedUnit, hopeSuccess);
            RefreshSelected();
            GridCombatView.Instance?.RefreshAll();
        }
    }
}
