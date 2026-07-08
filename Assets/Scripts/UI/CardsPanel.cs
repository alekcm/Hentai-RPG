using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RPG.Combat;
using RPG.Domains;

namespace RPG.UI
{
    /// <summary>
    /// Модальная панель «Карты юнита». Показывает известные карты выбранного игрового юнита,
    /// подсвечивает доступные (по ресурсам) и вызывает исполнителя карт с корректным CardContext.
    /// Для карт, требующих цель (target), после нажатия «Использовать» переходит в режим выбора цели через CombatUI.
    /// Для карт с sub-choice (Книга Авы / Хранитель стихий / Вдохновляющие слова / Исцеляющее касание)
    /// сначала показывается меню под-выбора.
    /// </summary>
    public class CardsPanel : MonoBehaviour
    {
        private static CardsPanel _instance;
        private static GameObject _canvasRoot;

        private GameObject panel;
        private Transform listContent;

        public static void Show(CombatUnit unit, Action<CardResult> onDone)
        {
            EnsureInstance();
            _instance.Populate(unit, onDone);
        }

        public static bool IsShown => _instance != null && _instance.panel != null && _instance.panel.activeSelf;

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            _canvasRoot = new GameObject("CardsPanelCanvas");
            DontDestroyOnLoad(_canvasRoot);
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            _canvasRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            _instance = _canvasRoot.AddComponent<CardsPanel>();

            // Затемнение фона
            var bg = MakePanel(_canvasRoot.transform, "Backdrop", Vector2.zero, new Vector2(2000, 2000),
                               new Color(0, 0, 0, 0.55f));
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;

            var panelRT = MakePanel(_canvasRoot.transform, "CardsPanel", Vector2.zero, new Vector2(720, 480),
                                    new Color(0.10f, 0.10f, 0.14f, 0.98f));
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            _instance.panel = panelRT.gameObject;

            var title = MakeText(panelRT, "Title", new Vector2(0, 215), 22, FontStyle.Bold);
            title.text = "Мои карты";
            title.rectTransform.sizeDelta = new Vector2(700, 30);

            // Скролл-контейнер
            var scrollGO = new GameObject("Scroll");
            scrollGO.transform.SetParent(panelRT, false);
            var srt = scrollGO.AddComponent<RectTransform>();
            srt.sizeDelta = new Vector2(700, 380);
            srt.anchoredPosition = new Vector2(0, -20);
            var scroll = scrollGO.AddComponent<ScrollRect>();
            var sImg = scrollGO.AddComponent<Image>();
            sImg.color = new Color(0.06f, 0.06f, 0.08f, 0.6f);
            scroll.horizontal = false; scroll.vertical = true;

            var content = new GameObject("Content");
            content.transform.SetParent(scrollGO.transform, false);
            var cRT = content.AddComponent<RectTransform>();
            cRT.sizeDelta = new Vector2(680, 0);
            cRT.pivot = new Vector2(0.5f, 1f);
            cRT.anchorMin = new Vector2(0f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.anchoredPosition = Vector2.zero;
            var vg = content.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(8, 8, 8, 8);
            vg.spacing = 6;
            vg.childAlignment = TextAnchor.UpperCenter;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            var fit = content.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cRT;
            scroll.viewport = srt;
            _instance.listContent = content.transform;

            // Кнопка «Закрыть»
            var closeBtn = MakeButton(panelRT, "Close", "Закрыть", new Vector2(280, -220));
            closeBtn.onClick.AddListener(() => _instance.Close(null));

            _instance.panel.SetActive(false);
        }

        private Action<CardResult> onDone;
        private CombatUnit currentUnit;

        private void Populate(CombatUnit unit, Action<CardResult> onDoneCb)
        {
            currentUnit = unit;
            onDone = onDoneCb;
            panel.SetActive(true);
            Time.timeScale = 0f;

            // Очистка списка
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);

            if (unit?.character?.knownDomainCards == null || unit.character.knownDomainCards.Count == 0)
            {
                var empty = MakeText((RectTransform)listContent, "Empty", Vector2.zero, 14, FontStyle.Italic);
                empty.text = "Карты не выбраны при создании персонажа.";
                return;
            }

            foreach (var cardId in unit.character.knownDomainCards)
            {
                var card = DomainDatabase.GetCard(cardId);
                if (card == null) continue;
                BuildCardRow(card, unit);
            }
        }

        private void BuildCardRow(DomainCard card, CombatUnit unit)
        {
            var row = new GameObject($"Card_{card.cardId}");
            row.transform.SetParent(listContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(680, 96);
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.18f, 0.22f, 1f);

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 96; le.preferredHeight = 96;

            string reason;
            bool canUse = DomainCardExecutor.CanUse(unit, card.cardId, out reason);

            var title = MakeText((RectTransform)row.transform, "T", new Vector2(-100, 32), 15, FontStyle.Bold);
            title.text = $"[{DomainNames.GetRussianName(card.domain)}] {card.displayName}";
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(520, 22);
            title.rectTransform.anchoredPosition = new Vector2(20, 32);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            title.rectTransform.pivot = new Vector2(0f, 0.5f);

            var desc = MakeText((RectTransform)row.transform, "D", Vector2.zero, 11, FontStyle.Normal);
            desc.text = card.description;
            desc.alignment = TextAnchor.UpperLeft;
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.rectTransform.sizeDelta = new Vector2(500, 60);
            desc.rectTransform.anchoredPosition = new Vector2(20, -14);
            desc.rectTransform.anchorMin = desc.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            desc.rectTransform.pivot = new Vector2(0f, 0.5f);

            var cost = new List<string>();
            if (card.cost.hope > 0) cost.Add($"Надежда −{card.cost.hope}");
            if (card.cost.stamina > 0) cost.Add($"Выносливость −{card.cost.stamina}");
            if (card.usesCharges) cost.Add("тратит заряд");
            string costText = cost.Count > 0 ? string.Join(", ", cost) : "без стоимости";

            var status = MakeText((RectTransform)row.transform, "S", Vector2.zero, 11, FontStyle.Italic);
            status.text = costText + (canUse ? "" : $"  ({reason})");
            status.color = canUse ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.85f, 0.55f, 0.55f);
            status.alignment = TextAnchor.MiddleRight;
            status.rectTransform.sizeDelta = new Vector2(280, 22);
            status.rectTransform.anchoredPosition = new Vector2(-140, 32);
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            status.rectTransform.pivot = new Vector2(1f, 0.5f);

            var useBtn = MakeButton((RectTransform)row.transform, "Use", "Использовать", new Vector2(-90, -20));
            useBtn.transform.SetParent(row.transform, false);
            var brt = (RectTransform)useBtn.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-10, -22);
            brt.sizeDelta = new Vector2(140, 32);

            useBtn.interactable = canUse;
            useBtn.onClick.AddListener(() =>
            {
                BeginActivation(card, unit);
            });
        }

        // ---------------- Активация карты ----------------

        private void BeginActivation(DomainCard card, CombatUnit unit)
        {
            // Определяем, нужен ли sub-choice или цель.
            string[] subChoices = GetSubChoicesFor(card.cardId);
            if (subChoices != null && subChoices.Length > 0)
            {
                ShowSubChoiceMenu(card, unit, subChoices);
                return;
            }
            ContinueActivation(card, unit, null);
        }

        private void ContinueActivation(DomainCard card, CombatUnit unit, string subChoice)
        {
            bool needsTarget = NeedsTarget(card.cardId, subChoice);
            if (!needsTarget)
            {
                ExecuteWithContext(card, unit, new CardContext { subChoice = subChoice });
                return;
            }

            // Закрываем панель — CombatUI переведём в режим выбора цели.
            Close(null); // не финализируем onDone, просто прячем панель

            CombatUIBridge.RequestTargetPick(
                targetFilter: t => IsValidTargetFor(card.cardId, subChoice, unit, t),
                onPicked: (target) =>
                {
                    ExecuteWithContext(card, unit, new CardContext { subChoice = subChoice, primaryTarget = target });
                },
                onCancel: () => onDone?.Invoke(CardResult.Info("Отмена.")));
        }

        private void ExecuteWithContext(DomainCard card, CombatUnit unit, CardContext ctx)
        {
            AbilityConfirmDialog.Show(
                title: $"Активировать «{card.displayName}»?",
                description: card.description,
                resources: BuildCostString(card, unit),
                yes: () =>
                {
                    var result = DomainCardExecutor.Execute(unit, card.cardId, ctx);
                    Close(result);
                },
                no: () =>
                {
                    // Игрок передумал — вернуть панель, чтобы можно было выбрать другую карту.
                    Populate(unit, onDone);
                });
        }

        private static string BuildCostString(DomainCard card, CombatUnit unit)
        {
            var parts = new List<string>();
            if (card.cost.hope > 0) parts.Add($"−{card.cost.hope} Надежды (у нас {CombatManager.Instance.HopePool})");
            if (card.cost.stamina > 0) parts.Add($"−{card.cost.stamina} Выносливости (у {unit.displayName}: {unit.stats.currentStamina})");
            if (card.usesCharges) parts.Add("тратит заряд карты");
            return parts.Count == 0 ? "без стоимости ресурсов" : string.Join(", ", parts);
        }

        private void ShowSubChoiceMenu(DomainCard card, CombatUnit unit, string[] subChoices)
        {
            // Простой вертикальный список кнопок вместо стандартного AbilityConfirmDialog.
            // Очищаем listContent и рисуем выбор вариантов.
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);

            var title = MakeText((RectTransform)listContent, "T", Vector2.zero, 15, FontStyle.Bold);
            title.text = $"{card.displayName} — выберите эффект:";

            foreach (var choice in subChoices)
            {
                var localChoice = choice;
                var btn = MakeButton((RectTransform)listContent, choice, GetSubChoiceLabel(card.cardId, choice), Vector2.zero);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40;
                btn.onClick.AddListener(() => ContinueActivation(card, unit, localChoice));
            }

            var back = MakeButton((RectTransform)listContent, "Back", "← Назад", Vector2.zero);
            var backLe = back.gameObject.AddComponent<LayoutElement>();
            backLe.preferredHeight = 32;
            back.onClick.AddListener(() => Populate(unit, onDone));
        }

        private void Close(CardResult result)
        {
            Time.timeScale = 1f;
            panel.SetActive(false);
            if (result != null) onDone?.Invoke(result);
        }

        // ---------------- Метаданные карт ----------------

        private static string[] GetSubChoicesFor(string cardId) => cardId switch
        {
            "magic_1_book_of_ava" => new[] { "push", "ice_armor", "frostbite" },
            "magic_1_book_of_taifar" => new[] { "sleep", "wildfire", "mystic_fog" },
            "charm_1_inspiring_words" => new[] { "heal", "stamina", "hope" },
            "light_1_healing_touch" => new[] { "one_slot_stamina", "two_slots" },
            "nature_1_elemental_guardian" => new[] { "fire", "earth", "water", "air" },
            _ => null
        };

        private static string GetSubChoiceLabel(string cardId, string choice)
        {
            return (cardId, choice) switch
            {
                ("magic_1_book_of_ava",     "push")       => "Силовой толчок (цель вплотную, d10+2 маг.)",
                ("magic_1_book_of_ava",     "ice_armor")  => "Доспехи льда (+1 к Показателю Брони цели, −1 Надежда)",
                ("magic_1_book_of_ava",     "frostbite")  => "Обморожение (12 кл., d6, Обездвижен)",
                ("magic_1_book_of_taifar",  "sleep")      => "Дрёма (2 кл., цель Засыпает)",
                ("magic_1_book_of_taifar",  "wildfire")   => "Дикое пламя (до 3 врагов вплотную, 2d6 маг.)",
                ("magic_1_book_of_taifar",  "mystic_fog") => "Мистический туман (СЛ 13)",
                ("charm_1_inspiring_words", "heal")       => "Восстановить цели 1 шкалу здоровья",
                ("charm_1_inspiring_words", "stamina")    => "Восстановить цели Выносливость",
                ("charm_1_inspiring_words", "hope")       => "+1 Надежда",
                ("light_1_healing_touch",   "one_slot_stamina") => "1 шкала здоровья + Выносливость",
                ("light_1_healing_touch",   "two_slots")  => "2 шкалы здоровья",
                ("nature_1_elemental_guardian", "fire")   => "Огонь: атакующий вплотную получает 1d10 маг.",
                ("nature_1_elemental_guardian", "earth")  => "Земля: +Порог Урона на уровень",
                ("nature_1_elemental_guardian", "water")  => "Вода: при след. ударе враги теряют Выносливость",
                ("nature_1_elemental_guardian", "air")    => "Воздух: удвоить движение, игнор поверхностей",
                _ => choice
            };
        }

        private static bool NeedsTarget(string cardId, string subChoice) => cardId switch
        {
            "weapon_1_whirlwind" => true,
            "magic_1_book_of_ava" => true,
            "magic_1_book_of_taifar" => subChoice == "sleep",
            "magic_1_chaos_release" => true,
            "charm_1_provoke" => true,
            "charm_1_trick" => true,
            "charm_1_inspiring_words" => subChoice != "hope",
            "terror_1_voice_of_dread" => true,
            "terror_1_withering_strike" => true,
            "nature_1_entangling_vines" => true,
            "nature_1_regeneration" => true,
            "light_1_arrow_of_light" => true,
            "light_1_healing_touch" => true,
            "defense_1_throwback" => true,
            _ => false
        };

        private static bool IsValidTargetFor(string cardId, string subChoice, CombatUnit caster, CombatUnit t)
        {
            if (t == null || t.IsDead) return false;
            // Целительные / бафф-карты — на союзников.
            bool onAlly = cardId switch
            {
                "charm_1_inspiring_words" => true,
                "light_1_healing_touch" => true,
                "nature_1_regeneration" => true,
                "magic_1_book_of_ava" when subChoice == "ice_armor" => true,
                _ => false
            };
            return onAlly ? t.side == caster.side : t.side != caster.side;
        }

        // ---------------- Утилиты сборки UI ----------------

        private static RectTransform MakePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = color;
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
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(280, 24);
            rt.anchoredPosition = pos;
            return t;
        }

        private static Button MakeButton(RectTransform parent, string name, string label, Vector2 pos)
        {
            var go = new GameObject("Btn_" + name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 36);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.40f, 1f);
            var btn = go.AddComponent<Button>();
            var t = MakeText(rt, "L", Vector2.zero, 13, FontStyle.Bold);
            t.text = label;
            t.rectTransform.sizeDelta = rt.sizeDelta;
            return btn;
        }
    }
}
