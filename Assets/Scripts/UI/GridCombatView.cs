using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RPG.Combat;
using RPG.Character;
using RPG.Items;

namespace RPG.UI
{
    /// <summary>
    /// Тактическая сетка боя.
    /// Отдельный Canvas (нижний слой относительно HUD и модалок).
    /// Отображает:
    ///  — Тайлы (пол/стены/укрытия/лозы) как цветные квадраты.
    ///  — Юниты как спрайты (через PortraitResolver) + HP-бар + Уклонение.
    ///  — Подсветку клеток при выборе юнита: движение (синим), атака (красным), заклинание (лиловым).
    ///  — Анимацию шагов при передвижении.
    ///  — Область активных зон (мистический туман, огонь).
    ///  — Кнопки движения/атаки — через существующие CombatUI кнопки, но клики по сетке заменяют портретные списки.
    /// </summary>
    public class GridCombatView : MonoBehaviour
    {
        // --- Синглтон, чтоб CombatManager/CombatUI могли отправлять ему команды ---
        public static GridCombatView Instance { get; private set; }

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("GridCombatView");
            Instance = go.AddComponent<GridCombatView>();
            DontDestroyOnLoad(go);
        }

        // --- Настройки ---
        private const int TileSize = 48;   // пиксели на клетку
        private const int UnitZoom = 44;   // размер картинки юнита

        // --- Внутреннее ---
        private GameObject rootCanvas;
        private RectTransform gridRoot;
        private RectTransform tilesLayer;
        private RectTransform highlightsLayer;
        private RectTransform zonesLayer;
        private RectTransform unitsLayer;

        private BattleMap map;
        private readonly Dictionary<Vector2Int, GameObject> tileGOs = new();
        private readonly Dictionary<string, UnitVisual> unitVisuals = new();
        private readonly List<GameObject> highlightGOs = new();
        private readonly List<GameObject> zoneGOs = new();

        // --- Режимы взаимодействия ---
        public enum Mode { Idle, PickMove, PickTarget }
        private Mode mode = Mode.Idle;
        private Dictionary<Vector2Int, int> reachable;   // клетка → стоимость (для режима движения)
        private System.Predicate<CombatUnit> targetFilter;
        private System.Action<CombatUnit> onTargetPicked;
        private System.Action onCancel;
        private int reachRange;                          // для режима PickTarget — макс. дальность (клетки)
        private CombatUnit rangeOrigin;
        private bool needsLos;

        private bool eventsBound;

        // ================================================================
        //  Инициализация
        // ================================================================

        private void Awake()
        {
            Instance = this;
            BuildCanvas();
            HookCombatEvents();
            rootCanvas.SetActive(false);
        }

        private void Start()
        {
            HookCombatEvents();
        }

        private void HookCombatEvents()
        {
            if (eventsBound) return;
            var cm = CombatManager.Instance;
            if (cm == null) return;
            cm.OnCombatStart += HandleCombatStart;
            cm.OnCombatEnd += HandleCombatEnd;
            cm.OnUnitActivated += _ => RefreshHighlights();
            cm.OnTurnPassedToSide += _ => { CancelSelection(); RefreshAll(); };
            eventsBound = true;
        }

        private void BuildCanvas()
        {
            rootCanvas = new GameObject("GridCombatCanvas");
            rootCanvas.transform.SetParent(transform, false);
            var canvas = rootCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // ниже HUD (500) и модалок
            rootCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            rootCanvas.AddComponent<GraphicRaycaster>();

            // Фон.
            var bg = new GameObject("Bg");
            bg.transform.SetParent(rootCanvas.transform, false);
            var bgRT = bg.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);

            // Контейнер сетки — центрируется.
            var gridGO = new GameObject("Grid");
            gridGO.transform.SetParent(rootCanvas.transform, false);
            gridRoot = gridGO.AddComponent<RectTransform>();
            gridRoot.anchorMin = gridRoot.anchorMax = new Vector2(0.5f, 0.5f);
            gridRoot.pivot = new Vector2(0.5f, 0.5f);
            gridRoot.anchoredPosition = new Vector2(0, 30); // чуть выше центра, чтобы не залезать под кнопки

            tilesLayer      = MakeLayer("Tiles");
            highlightsLayer = MakeLayer("Highlights");
            zonesLayer      = MakeLayer("Zones");
            unitsLayer      = MakeLayer("Units");
        }

        private RectTransform MakeLayer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(gridRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        // ================================================================
        //  Жизненный цикл боя
        // ================================================================

        private void HandleCombatStart()
        {
            map = CombatManager.Instance.CurrentMap ?? BattleMapLoader.CreateDefault();
            SizeGridRoot();
            BuildTiles();
            BuildUnits();
            rootCanvas.SetActive(true);
        }

        private void HandleCombatEnd(bool _)
        {
            rootCanvas.SetActive(false);
            ClearAll();
        }

        private void ClearAll()
        {
            foreach (var kv in tileGOs) if (kv.Value) Destroy(kv.Value);
            tileGOs.Clear();
            foreach (var kv in unitVisuals) if (kv.Value.root) Destroy(kv.Value.root);
            unitVisuals.Clear();
            foreach (var h in highlightGOs) if (h) Destroy(h);
            highlightGOs.Clear();
            foreach (var z in zoneGOs) if (z) Destroy(z);
            zoneGOs.Clear();
        }

        private void SizeGridRoot()
        {
            gridRoot.sizeDelta = new Vector2(map.width * TileSize, map.height * TileSize);
        }

        // ================================================================
        //  Тайлы
        // ================================================================

        private void BuildTiles()
        {
            foreach (var kv in tileGOs) if (kv.Value) Destroy(kv.Value);
            tileGOs.Clear();

            for (int x = 0; x < map.width; x++)
            for (int y = 0; y < map.height; y++)
            {
                var pos = new Vector2Int(x, y);
                var tile = map.GetTile(pos);
                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(tilesLayer, false);
                var rt = go.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(TileSize - 1, TileSize - 1);
                rt.anchoredPosition = CellToLocal(pos);
                var img = go.AddComponent<Image>();
                img.color = TileColor(tile);
                img.raycastTarget = true;
                // Клик по клетке.
                var btn = go.AddComponent<Button>();
                var capturedPos = pos;
                btn.onClick.AddListener(() => OnTileClicked(capturedPos));
                // Отключим анимацию перехода — фон и так меняется подсветкой.
                var colors = btn.colors; colors.pressedColor = img.color; btn.colors = colors;
                tileGOs[pos] = go;
            }
        }

        private static Color TileColor(TileKind t) => t switch
        {
            TileKind.Floor      => new Color(0.20f, 0.20f, 0.24f),
            TileKind.Wall       => new Color(0.10f, 0.08f, 0.08f),
            TileKind.FullCover  => new Color(0.35f, 0.28f, 0.20f),
            TileKind.HalfCover  => new Color(0.30f, 0.26f, 0.20f),
            TileKind.Difficult  => new Color(0.16f, 0.28f, 0.18f),
            TileKind.Hazard     => new Color(0.38f, 0.10f, 0.10f),
            _                   => new Color(0.20f, 0.20f, 0.24f)
        };

        private Vector2 CellToLocal(Vector2Int p)
        {
            // Клетка (0,0) в левом-нижнем углу.
            float x = (p.x - map.width  / 2f + 0.5f) * TileSize;
            float y = (p.y - map.height / 2f + 0.5f) * TileSize;
            return new Vector2(x, y);
        }

        // ================================================================
        //  Юниты
        // ================================================================

        private class UnitVisual
        {
            public CombatUnit unit;
            public GameObject root;
            public Image portrait;
            public Image hpBar;
            public Image staminaBar;
            public Text nameLabel;
            public Image selectionRing;
        }

        private void BuildUnits()
        {
            foreach (var kv in unitVisuals) if (kv.Value.root) Destroy(kv.Value.root);
            unitVisuals.Clear();
            foreach (var u in CombatManager.Instance.AllUnits)
                AddUnitVisual(u);
        }

        private void AddUnitVisual(CombatUnit u)
        {
            var go = new GameObject($"Unit_{u.unitId}");
            go.transform.SetParent(unitsLayer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(UnitZoom, UnitZoom);
            rt.anchoredPosition = CellToLocal(u.gridPosition);

            // Кольцо выделения (под спрайтом).
            var ring = new GameObject("Ring");
            ring.transform.SetParent(go.transform, false);
            var rrt = ring.AddComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(UnitZoom + 6, UnitZoom + 6);
            var ringImg = ring.AddComponent<Image>();
            ringImg.color = u.side == CombatSide.Player
                ? new Color(0.35f, 0.85f, 0.35f, 0.55f)
                : new Color(0.85f, 0.30f, 0.30f, 0.55f);
            ringImg.raycastTarget = false;

            // Спрайт юнита.
            var portraitGO = new GameObject("Portrait");
            portraitGO.transform.SetParent(go.transform, false);
            var prt = portraitGO.AddComponent<RectTransform>();
            prt.sizeDelta = new Vector2(UnitZoom, UnitZoom);
            var portrait = portraitGO.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = true;
            portrait.sprite = ResolveSprite(u);
            if (portrait.sprite == null)
            {
                // Плейсхолдер — цветной круг с буквой.
                portrait.color = u.side == CombatSide.Player
                    ? new Color(0.4f, 0.7f, 0.4f) : new Color(0.7f, 0.35f, 0.35f);
            }

            // Клики по юниту.
            var btn = portraitGO.AddComponent<Button>();
            var localU = u;
            btn.onClick.AddListener(() => OnUnitClicked(localU));

            // HP-бар (сверху).
            var hpBg = new GameObject("HpBg");
            hpBg.transform.SetParent(go.transform, false);
            var hprt = hpBg.AddComponent<RectTransform>();
            hprt.sizeDelta = new Vector2(UnitZoom, 5);
            hprt.anchoredPosition = new Vector2(0, UnitZoom * 0.55f);
            hpBg.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var hpFill = new GameObject("HpFill");
            hpFill.transform.SetParent(hpBg.transform, false);
            var hpfr = hpFill.AddComponent<RectTransform>();
            hpfr.anchorMin = new Vector2(0, 0); hpfr.anchorMax = new Vector2(1, 1);
            hpfr.offsetMin = Vector2.zero; hpfr.offsetMax = Vector2.zero;
            hpfr.pivot = new Vector2(0, 0.5f);
            var hpImg = hpFill.AddComponent<Image>();
            hpImg.color = new Color(0.85f, 0.20f, 0.20f);

            // Выносливость (тонкая полоска ниже HP).
            var stBg = new GameObject("StBg");
            stBg.transform.SetParent(go.transform, false);
            var strt = stBg.AddComponent<RectTransform>();
            strt.sizeDelta = new Vector2(UnitZoom, 3);
            strt.anchoredPosition = new Vector2(0, UnitZoom * 0.55f - 6);
            stBg.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var stFill = new GameObject("StFill");
            stFill.transform.SetParent(stBg.transform, false);
            var stfr = stFill.AddComponent<RectTransform>();
            stfr.anchorMin = Vector2.zero; stfr.anchorMax = Vector2.one;
            stfr.offsetMin = Vector2.zero; stfr.offsetMax = Vector2.zero;
            stfr.pivot = new Vector2(0, 0.5f);
            var stImg = stFill.AddComponent<Image>();
            stImg.color = new Color(0.95f, 0.75f, 0.20f);

            // Подпись имени под юнитом.
            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(go.transform, false);
            var nrt = nameGO.AddComponent<RectTransform>();
            nrt.sizeDelta = new Vector2(120, 14);
            nrt.anchoredPosition = new Vector2(0, -UnitZoom * 0.55f);
            var txt = nameGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 11; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = u.displayName;

            var vis = new UnitVisual
            {
                unit = u, root = go, portrait = portrait,
                hpBar = hpImg, staminaBar = stImg,
                nameLabel = txt, selectionRing = ringImg
            };
            unitVisuals[u.unitId] = vis;
            RefreshUnitVisual(vis);
        }

        private Sprite ResolveSprite(CombatUnit u)
        {
            if (u.side == CombatSide.Player && u.character != null)
                return PortraitResolver.GetPlayerSprite(u.character.race, u.character.gender, u.character.characterClass);
            // Для врагов пробуем распарсить unitId (до первого '_' + подчёркивания).
            string idBase = u.unitId;
            int underscore = idBase.LastIndexOf('_');
            if (underscore > 0 && underscore < idBase.Length - 2 && idBase.Length - underscore <= 8)
                idBase = idBase.Substring(0, underscore);
            return PortraitResolver.GetEnemySprite(idBase);
        }

        private void RefreshUnitVisual(UnitVisual v)
        {
            if (v == null || v.unit == null) return;
            var u = v.unit;
            v.root.transform.SetAsLastSibling();
            v.root.SetActive(!u.IsDead);
            v.selectionRing.enabled = (u == CombatManager.Instance?.SelectedUnit);
            // HP fill
            float hpRatio = u.stats.maxHealthSlots > 0
                ? (float)(u.stats.maxHealthSlots - u.stats.usedHealthSlots) / u.stats.maxHealthSlots
                : 0f;
            var hpRT = (RectTransform)v.hpBar.transform;
            hpRT.anchorMax = new Vector2(Mathf.Clamp01(hpRatio), 1);
            v.hpBar.color = hpRatio > 0.5f ? new Color(0.30f, 0.85f, 0.30f)
                          : hpRatio > 0.25f ? new Color(0.90f, 0.80f, 0.20f)
                          :                    new Color(0.90f, 0.20f, 0.20f);
            // Stamina fill
            float stRatio = u.stats.maxStamina > 0
                ? (float)u.stats.currentStamina / u.stats.maxStamina
                : 0f;
            var stRT = (RectTransform)v.staminaBar.transform;
            stRT.anchorMax = new Vector2(Mathf.Clamp01(stRatio), 1);
        }

        // ================================================================
        //  Публичный API (вызывается CombatUI и CombatManager)
        // ================================================================

        public void RefreshAll()
        {
            if (map == null) return;
            foreach (var kv in unitVisuals)
            {
                if (kv.Value?.root == null) continue;
                kv.Value.root.GetComponent<RectTransform>().anchoredPosition = CellToLocal(kv.Value.unit.gridPosition);
                RefreshUnitVisual(kv.Value);
            }
            RefreshZones();
            RefreshHighlights();
        }

        /// <summary>Обновляет отображение активного юнита (кольцо выделения).</summary>
        public void RefreshHighlights()
        {
            ClearHighlights();
            var cm = CombatManager.Instance;
            if (cm == null || map == null) return;

            var sel = cm.SelectedUnit;
            foreach (var kv in unitVisuals)
                kv.Value.selectionRing.enabled = (kv.Value.unit == sel);

            switch (mode)
            {
                case Mode.PickMove:
                    if (reachable != null)
                        foreach (var kv in reachable)
                            if (kv.Key != sel.gridPosition)
                                DrawHighlight(kv.Key, new Color(0.30f, 0.60f, 1.00f, 0.35f));
                    break;
                case Mode.PickTarget:
                    foreach (var u in cm.AllUnits)
                    {
                        if (u.IsDead || u == sel) continue;
                        if (targetFilter != null && !targetFilter(u)) continue;
                        int dist = Pathfinding.GridDistance(rangeOrigin.gridPosition, u.gridPosition);
                        if (dist > reachRange) continue;
                        if (needsLos && !Pathfinding.HasLineOfSight(map, rangeOrigin.gridPosition, u.gridPosition)) continue;
                        DrawHighlight(u.gridPosition, new Color(1.00f, 0.30f, 0.30f, 0.45f));
                    }
                    break;
            }
        }

        public void RefreshZones()
        {
            foreach (var z in zoneGOs) if (z) Destroy(z);
            zoneGOs.Clear();
            foreach (var zone in CombatZones.All)
            {
                Color c = zone.kind switch
                {
                    ZoneKind.MysticFog => new Color(0.7f, 0.7f, 0.85f, 0.35f),
                    ZoneKind.Fire      => new Color(1.0f, 0.4f, 0.1f, 0.40f),
                    ZoneKind.Vines     => new Color(0.3f, 0.7f, 0.3f, 0.30f),
                    _                  => new Color(0.5f, 0.5f, 0.5f, 0.30f)
                };
                for (int dx = -zone.radius; dx <= zone.radius; dx++)
                for (int dy = -zone.radius; dy <= zone.radius; dy++)
                {
                    var p = new Vector2Int(zone.center.x + dx, zone.center.y + dy);
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) > zone.radius) continue;
                    if (!map.InBounds(p)) continue;
                    var go = new GameObject("Zone");
                    go.transform.SetParent(zonesLayer, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(TileSize - 2, TileSize - 2);
                    rt.anchoredPosition = CellToLocal(p);
                    var img = go.AddComponent<Image>();
                    img.color = c; img.raycastTarget = false;
                    zoneGOs.Add(go);
                }
            }
        }

        private void ClearHighlights()
        {
            foreach (var h in highlightGOs) if (h) Destroy(h);
            highlightGOs.Clear();
        }

        private void DrawHighlight(Vector2Int p, Color c)
        {
            var go = new GameObject("H");
            go.transform.SetParent(highlightsLayer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(TileSize - 3, TileSize - 3);
            rt.anchoredPosition = CellToLocal(p);
            var img = go.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            highlightGOs.Add(go);
        }

        // ---------- Режим движения ----------

        /// <summary>UI просит показать доступные для движения клетки.</summary>
        public bool BeginMovePick(CombatUnit unit, int budget)
        {
            if (unit == null || unit.HasEffectId("immobilized_temp")) return false;
            mode = Mode.PickMove;
            reachable = Pathfinding.Reachable(map, unit.gridPosition, budget, CombatManager.Instance.AllUnits, unit);
            RefreshHighlights();
            return true;
        }

        /// <summary>UI просит показать возможные цели.</summary>
        public bool BeginTargetPick(CombatUnit source, int rangeTiles, System.Predicate<CombatUnit> filter,
                                     System.Action<CombatUnit> onPicked, System.Action onCancel = null,
                                     bool requiresLos = true)
        {
            if (source == null) return false;
            mode = Mode.PickTarget;
            rangeOrigin = source;
            reachRange = rangeTiles;
            targetFilter = filter ?? (u => true);
            onTargetPicked = onPicked;
            this.onCancel = onCancel;
            needsLos = requiresLos;
            RefreshHighlights();
            return true;
        }

        public void CancelSelection()
        {
            mode = Mode.Idle;
            reachable = null;
            targetFilter = null;
            onTargetPicked = null;
            var oc = onCancel; onCancel = null;
            oc?.Invoke();
            RefreshHighlights();
        }

        // ---------- Обработчики кликов ----------

        private void OnTileClicked(Vector2Int p)
        {
            if (mode == Mode.PickMove)
            {
                var cm = CombatManager.Instance;
                var u = cm?.SelectedUnit;
                if (u == null) return;
                if (!reachable.TryGetValue(p, out int cost) || p == u.gridPosition) return;
                var path = Pathfinding.FindPath(map, u.gridPosition, p, cost, cm.AllUnits, u);
                if (path.Count == 0) return;

                mode = Mode.Idle;
                ClearHighlights();
                StartCoroutine(AnimateMove(u, path));
            }
            // Клик по клетке в режиме PickTarget без юнита — игнорируем (для этого нужен юнит).
        }

        private void OnUnitClicked(CombatUnit u)
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsCombatActive) return;

            if (mode == Mode.PickTarget)
            {
                if (targetFilter != null && !targetFilter(u)) return;
                int dist = Pathfinding.GridDistance(rangeOrigin.gridPosition, u.gridPosition);
                if (dist > reachRange) return;
                if (needsLos && !Pathfinding.HasLineOfSight(map, rangeOrigin.gridPosition, u.gridPosition)) return;

                var cb = onTargetPicked;
                mode = Mode.Idle;
                targetFilter = null;
                onTargetPicked = null;
                onCancel = null;
                ClearHighlights();
                cb?.Invoke(u);
                return;
            }

            // Обычный клик — активировать своего юнита.
            if (u.side == CombatSide.Player && cm.ActiveSide == CombatSide.Player)
            {
                cm.ActivatePlayerUnit(u.unitId);
                RefreshAll();
            }
        }

        // ---------- Анимация ----------

        private IEnumerator AnimateMove(CombatUnit u, List<Vector2Int> path)
        {
            var vis = unitVisuals[u.unitId];
            var rt = vis.root.GetComponent<RectTransform>();
            foreach (var step in path)
            {
                Vector2 from = rt.anchoredPosition;
                Vector2 to = CellToLocal(step);
                float t = 0f;
                float dur = 0.12f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.Clamp01(t / dur));
                    yield return null;
                }
                u.gridPosition = step;
                // При проходе по Hazard/огню — урон.
                int fire = CombatZones.FireDamageOnEnter(step);
                if (fire > 0 && !u.ignoreHazardsThisTurn)
                {
                    u.stats.TakeDamage(fire);
                    RefreshUnitVisual(vis);
                }
            }
            RefreshAll();
        }

        /// <summary>Анимация «атаки» — тряска и вспышка на цели (косметика).</summary>
        public IEnumerator PlayAttackFeedback(CombatUnit attacker, CombatUnit target, bool hit)
        {
            if (!unitVisuals.TryGetValue(target.unitId, out var tgtVis)) yield break;
            var rt = tgtVis.root.GetComponent<RectTransform>();
            Vector2 orig = rt.anchoredPosition;
            var flashColor = hit ? new Color(1f, 0.6f, 0.6f, 1f) : new Color(1f, 1f, 1f, 1f);
            tgtVis.portrait.color = flashColor;
            float shake = hit ? 4f : 2f;
            for (int i = 0; i < 6; i++)
            {
                rt.anchoredPosition = orig + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
                yield return new WaitForSecondsRealtime(0.03f);
            }
            rt.anchoredPosition = orig;
            tgtVis.portrait.color = Color.white;
            RefreshUnitVisual(tgtVis);
        }
    }
}
