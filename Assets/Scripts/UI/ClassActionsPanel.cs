using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RPG.Character;
using RPG.Combat;
using RPG.Domains;

namespace RPG.UI
{
    /// <summary>
    /// Модальная панель классовых/подклассовых действий активного юнита:
    ///  ВОИН-МБИ: смена стойки (6 вариантов).
    ///  ПЛУТ-Отравитель: приготовить смеси.
    ///  СВЯЩЕННИК-Жрец: 3 молебна.
    ///  СВЯЩЕННИК-Серафим: включить/выключить форму крылатого стража.
    ///  МАГ-Метамагия: заблокировать одну свою карту → выбрать эффект усиления следующего заклинания.
    ///  ДРУИД: сменить звериную форму.
    /// </summary>
    public class ClassActionsPanel : MonoBehaviour
    {
        private static ClassActionsPanel _instance;
        private static GameObject _canvasRoot;

        private GameObject panel;
        private Transform listContent;
        private Action onClose;
        private CombatUnit currentUnit;

        public static void Show(CombatUnit unit, Action onClose)
        {
            EnsureInstance();
            _instance.currentUnit = unit;
            _instance.onClose = onClose;
            _instance.Populate();
            _instance.panel.SetActive(true);
            Time.timeScale = 0f;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            _canvasRoot = new GameObject("ClassActionsCanvas");
            DontDestroyOnLoad(_canvasRoot);
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950;
            _canvasRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            _instance = _canvasRoot.AddComponent<ClassActionsPanel>();

            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgRT = bg.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(_canvasRoot.transform, false);
            var prt = panelGO.AddComponent<RectTransform>();
            prt.sizeDelta = new Vector2(600, 440);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            panelGO.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f, 0.98f);
            _instance.panel = panelGO;

            var title = MakeText(prt, "Title", new Vector2(0, 195), 20, FontStyle.Bold);
            title.text = "Классовые действия";

            var content = new GameObject("Content");
            content.transform.SetParent(prt, false);
            var crt = content.AddComponent<RectTransform>();
            crt.sizeDelta = new Vector2(560, 340);
            crt.anchoredPosition = new Vector2(0, -10);
            var vg = content.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(10, 10, 10, 10);
            vg.spacing = 6;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            _instance.listContent = content.transform;

            var close = MakeButton(prt, "Close", "Закрыть", new Vector2(230, -200));
            close.onClick.AddListener(() => _instance.Close());

            _instance.panel.SetActive(false);
        }

        private void Populate()
        {
            for (int i = listContent.childCount - 1; i >= 0; i--) Destroy(listContent.GetChild(i).gameObject);

            var c = currentUnit?.character;
            if (c == null) { AddLabel("Юнит не выбран."); return; }

            AddLabel($"{c.displayName} — {c.characterClass}/{c.subclassId ?? "—"}");

            // --- Мастер БИ: стойки ---
            if (c.subclassId == "warrior_martial_artist")
            {
                AddLabel($"Активная стойка: {ClassFeaturesBus.GetStance(currentUnit) ?? "—"}. Смена = 1 Выносливость.");
                foreach (var (id, label) in new (string, string)[]
                {
                    ("brutal","Брутальная"),("defensive","Защитная"),("grapple","Захватная"),
                    ("sturdy","Устойчивая"),("precise","Точная"),("fast","Быстрая")
                })
                {
                    var lid = id;
                    AddButton($"Сменить на: {label}", () =>
                    {
                        if (currentUnit.stats.currentStamina < 1) return;
                        AbilityConfirmDialog.Show("Смена стойки", $"Перейти в стойку «{label}»?",
                            $"Стоимость: 1 Выносливость (у вас {currentUnit.stats.currentStamina}).",
                            yes: () => { ClassFeaturesBus.ChangeStance(currentUnit, lid); Populate(); });
                    });
                }
            }

            // --- Плут-Отравитель ---
            if (c.subclassId == "rogue_poisoners_guild")
            {
                AddLabel($"Жетоны смесей: {ClassFeaturesBus.GetPoisonTokens(currentUnit)}. При атаке — 1 жетон = +1d6 урона.");
                AddButton("Приготовить смеси (1 Вын → 1d4+1 жетонов)", () =>
                {
                    if (currentUnit.stats.currentStamina < 1) return;
                    AbilityConfirmDialog.Show("Токсичные смеси", "Приготовить 1d4+1 жетонов смесей?",
                        $"Стоимость: 1 Выносливость (у вас {currentUnit.stats.currentStamina}).",
                        yes: () => { ClassFeaturesBus.PoisonerBrew(currentUnit); Populate(); });
                });
            }

            // --- Жрец: молебны ---
            if (c.subclassId == "cleric_priest" && ClassFeaturesBus.IsPrayerAvailable(currentUnit))
            {
                AddLabel("Молебен доступен (раз в бой, свободное действие).");
                AddButton("Целебный: +1 шкала здоровья союзникам в 4 кл.", () =>
                    AbilityConfirmDialog.Show("Целебный молебен", "Произнести?", "Свободное действие, раз в бой.",
                        yes: () => { ClassFeaturesBus.CastPrayer(currentUnit, "healing"); Populate(); }));
                AddButton("Боевой: ближайший враг в 4 кл. → Уязвим", () =>
                    AbilityConfirmDialog.Show("Боевой молебен", "Произнести?", "Свободное действие, раз в бой.",
                        yes: () => { ClassFeaturesBus.CastPrayer(currentUnit, "battle"); Populate(); }));
                AddButton("Воодушевляющий: +1 Надежда за каждого спутника в 4 кл.", () =>
                    AbilityConfirmDialog.Show("Воодушевляющий молебен", "Произнести?", "Свободное действие, раз в бой.",
                        yes: () => { ClassFeaturesBus.CastPrayer(currentUnit, "inspiring"); Populate(); }));
            }

            // --- Серафим ---
            if (c.subclassId == "cleric_seraph")
            {
                bool active = ClassFeaturesBus.IsSeraphActive(currentUnit);
                AddLabel(active ? "Форма крылатого стража АКТИВНА." : "Форма крылатого стража неактивна.");
                AddButton(active ? "Выйти из формы" : "Войти в форму (1 Вын)", () =>
                {
                    if (!active && currentUnit.stats.currentStamina < 1) return;
                    AbilityConfirmDialog.Show("Форма крылатого стража",
                        active ? "Выйти из формы?" : "Войти в форму?",
                        active ? "Без стоимости." : "Стоимость: 1 Выносливость.",
                        yes: () => { ClassFeaturesBus.ToggleSeraphForm(currentUnit); Populate(); });
                });
            }

            // --- Мaг-Метамагия ---
            if (c.subclassId == "mage_school_of_metamagic")
            {
                AddLabel("Заблокировать одну свою карту до конца боя, чтобы усилить следующее заклинание:");
                foreach (var cardId in c.knownDomainCards)
                {
                    if (ClassFeaturesBus.IsCardMetamagicLocked(currentUnit, cardId)) continue;
                    var card = DomainDatabase.GetCard(cardId);
                    if (card == null) continue;
                    var lcid = cardId;
                    AddButton($"Блок «{card.displayName}» → выбор эффекта", () => ShowMetamagicSubMenu(lcid));
                }
            }

            // --- Друид: звериные формы ---
            if (c.characterClass == ClassType.Druid)
            {
                var cur = ClassFeaturesBus.GetDruidForm(currentUnit);
                AddLabel($"Текущая форма: {cur ?? "человек"}. Смена = 1 Выносливость.");
                foreach (var (id, label) in new (string, string)[]
                {
                    ("human","Человек"),
                    ("giant_arachnid","Огромный арахнид (+2 укл., d6+1)"),
                    ("silkworm","Шелкопряд (нить паутины — Обездвижить, d4)"),
                    ("wolf","Волк (+1 укл., d8+2)"),
                    ("dog","Собака (+2 укл., d6, Хрупкость)"),
                    ("gazelle","Газель (+3 укл., d6, Хрупкость)"),
                    ("armadillo","Броненосец (сопр. физ., d6)"),
                })
                {
                    var lid = id;
                    AddButton($"Форма: {label}", () =>
                    {
                        if (currentUnit.stats.currentStamina < 1 && lid != "human") return;
                        AbilityConfirmDialog.Show("Смена звериной формы", $"Принять форму «{label}»?",
                            lid == "human" ? "Без стоимости." : $"Стоимость: 1 Выносливость.",
                            yes: () => { ClassFeaturesBus.ChangeDruidForm(currentUnit, lid); Populate(); });
                    });
                }
            }

            // --- Отдых (вне боя) — тоже показываем ---
            if (!CombatManager.Instance.IsCombatActive)
            {
                AddButton("Отдых (монетка Страха)", () =>
                {
                    var r = RestAction.Perform(c);
                    Debug.Log($"[Rest] {r.message}");
                    Populate();
                });
            }
        }

        private void ShowMetamagicSubMenu(string cardId)
        {
            for (int i = listContent.childCount - 1; i >= 0; i--) Destroy(listContent.GetChild(i).gameObject);
            AddLabel($"Выберите эффект для карты «{DomainDatabase.GetCard(cardId).displayName}»:");
            foreach (var (eff, label) in new (string, string)[]
            {
                ("double_range","Удвоить дальность следующего заклинания"),
                ("plus2","+2 к результату броска попадания / проверки"),
                ("double_die","Удвоить одну кость урона"),
                ("extra_target","Поразить дополнительную цель в дистанции")
            })
            {
                var leff = eff;
                AddButton(label, () =>
                {
                    ClassFeaturesBus.LockCardForMetamagic(currentUnit, cardId, leff);
                    Populate();
                });
            }
            AddButton("← Назад", () => Populate());
        }

        private void Close()
        {
            Time.timeScale = 1f;
            panel.SetActive(false);
            onClose?.Invoke();
        }

        // ---- UI helpers ----

        private void AddLabel(string text)
        {
            var go = new GameObject("Lbl");
            go.transform.SetParent(listContent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.color = new Color(0.9f, 0.9f, 0.75f);
            t.fontSize = 13; t.fontStyle = FontStyle.Italic;
            t.alignment = TextAnchor.MiddleLeft;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 24; le.minHeight = 20;
        }

        private void AddButton(string text, Action onClick)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(540, 34);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.40f);
            var btn = go.AddComponent<Button>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 34;

            var t = new GameObject("L");
            t.transform.SetParent(go.transform, false);
            var trt = t.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tx = t.AddComponent<Text>();
            tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tx.color = Color.white; tx.fontSize = 13; tx.alignment = TextAnchor.MiddleCenter;
            tx.text = text;
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private static Text MakeText(Transform parent, string name, Vector2 pos, int size, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size; t.fontStyle = style; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(400, 28);
            rt.anchoredPosition = pos;
            return t;
        }

        private static Button MakeButton(Transform parent, string name, string label, Vector2 pos)
        {
            var go = new GameObject("Btn_" + name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140, 36);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.30f, 0.40f);
            var btn = go.AddComponent<Button>();
            var t = MakeText(rt, "L", Vector2.zero, 13, FontStyle.Bold);
            t.text = label;
            t.rectTransform.sizeDelta = rt.sizeDelta;
            return btn;
        }
    }
}
