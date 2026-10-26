using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Мировая часть хаба (этап 4, plain class из Level в режиме Hub): площадка с клетками сетки поверх
/// сгенерированной карты, спрайты поставленных построек, значки их уровня (2+) и призрак размещения.
/// Клетка (0,0) — левый нижний угол сетки, лежит в тайле карты <see cref="OriginTile"/>; одна клетка — один тайл.
/// Площадка рисуется своими спрайтами поверх земли/воды (порядок ниже объектов 100–1000), поэтому вода
/// под ней не мешает; объекты и туман над площадкой убирает Level.
/// Все объекты живут под корнем "Hub" и уничтожаются вместе со сценой (или Dispose).
/// </summary>
public class HubGrid : System.IDisposable
{
    // Площадка — над тайлмапами (0–10), под объектами (DepthSorting 100–1000)
    private const int PlazaOrder = 20;
    private const int CellOrder = 21;
    // Призрак — поверх построек, под туманом (OverlayOrder)
    private const int GhostAreaOrder = DepthSorting.OverlayOrder - 10;
    private const int GhostSpriteOrder = GhostAreaOrder + 1;
    // Доля клетки, которую закрывает квадрат клетки (остальное — «линии» сетки цвета площадки)
    private const float CellFill = 0.92f;
    // Поля площадки вокруг сетки, в клетках
    private const float PlazaMargin = 0.25f;

    private static readonly Color PlazaColor = new Color(0.45f, 0.37f, 0.25f, 1f);
    private static readonly Color CellColor = new Color(0.47f, 0.66f, 0.33f, 1f);
    private static readonly Color GhostValidColor = new Color(0.3f, 1f, 0.4f, 0.45f);
    private static readonly Color GhostInvalidColor = new Color(1f, 0.3f, 0.25f, 0.45f);
    private static readonly Color GhostSpriteTint = new Color(1f, 1f, 1f, 0.65f);

    // Подсветка соседей, рядом с которыми появится бонус за соседство (T15): над постройками, под призраком
    private const int BonusOrder = GhostAreaOrder - 3;
    private const float BonusEdge = 0.08f; // толщина рамки, доля клетки
    private static readonly Color BonusFillColor = new Color(1f, 0.85f, 0.3f, 0.18f);
    private static readonly Color BonusEdgeColor = new Color(1f, 0.82f, 0.3f, 0.95f);

    // Значок уровня (T14): тёмная плашка с золотыми «пипсами» по числу уровней, у правого нижнего угла
    // постройки. Порядок — над спрайтом постройки (+1/+2 к её порядку по основанию)
    private const float BadgePip = 0.14f;      // доли клетки
    private const float BadgeGap = 0.06f;
    private const float BadgePadding = 0.05f;
    private const float BadgeLift = 0.22f;     // подъём центра плашки над основанием
    private static readonly Color BadgePlateColor = new Color(0.1f, 0.08f, 0.06f, 0.9f);
    private static readonly Color BadgePipColor = new Color(1f, 0.82f, 0.3f, 1f);

    private readonly IMapDataProvider _mapData;
    private readonly HubService _hub;
    private readonly Material _spriteMaterial;
    private readonly Vector2Int _originTile;
    private readonly Vector3 _cellStep;

    private readonly GameObject _root;
    private readonly Transform _buildingsRoot;
    private readonly Texture2D _texture;
    private readonly Sprite _square;
    private readonly Dictionary<string, GameObject> _buildingViews = new Dictionary<string, GameObject>();
    // id постройки → значок уровня (только у уровня 2+)
    private readonly Dictionary<string, GameObject> _levelBadges = new Dictionary<string, GameObject>();

    private GameObject _ghostRoot;
    private SpriteRenderer _ghostArea;
    private SpriteRenderer _ghostSprite;
    // Пул подсветок соседей-бонусов (T15), дети _ghostRoot
    private readonly List<GameObject> _bonusHighlights = new List<GameObject>();

    /// <summary>Тайл карты, в котором лежит клетка (0,0).</summary>
    public Vector2Int OriginTile => _originTile;
    public Vector2Int GridSize => _hub.GridSize;
    /// <summary>Тайлы карты, занятые сеткой.</summary>
    public RectInt TileArea => new RectInt(_originTile, _hub.GridSize);
    /// <summary>Мировая точка центра сетки — для камеры.</summary>
    public Vector3 WorldCenter => AreaCenter(new RectInt(Vector2Int.zero, _hub.GridSize));
    public Transform Root => _root.transform;

    /// <param name="spriteMaterial">Материал для созданных спрайтов (как у игрока); null — по умолчанию.</param>
    public HubGrid(IMapDataProvider mapData, HubService hub, Vector2Int originTile, Material spriteMaterial = null)
    {
        Debug.Assert(mapData != null, "HubGrid: mapData is null");
        Debug.Assert(hub != null, "HubGrid: HubService is null");

        _mapData = mapData;
        _hub = hub;
        _originTile = originTile;
        _spriteMaterial = spriteMaterial;
        _cellStep = mapData.TileIndexToWorld(1, 1) - mapData.TileIndexToWorld(0, 0);

        _root = new GameObject("Hub");
        _buildingsRoot = new GameObject("Buildings").transform;
        _buildingsRoot.SetParent(_root.transform, false);

        _texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "HubSquare" };
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        _texture.SetPixels32(pixels);
        _texture.Apply();
        // 4 пикселя на единицу — квадрат 1×1 мировых единиц с опорной точкой в центре
        _square = Sprite.Create(_texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        _square.name = "HubSquare";

        BuildPlaza();
        foreach (HubPlacement placed in _hub.GetPlaced())
            CreateBuildingView(placed);

        _hub.OnBuilt += HandleBuilt;
        _hub.OnUpgraded += HandleUpgraded;
    }

    public void Dispose()
    {
        _hub.OnBuilt -= HandleBuilt;
        _hub.OnUpgraded -= HandleUpgraded;

        if (_root != null) Object.Destroy(_root);
        if (_square != null) Object.Destroy(_square);
        if (_texture != null) Object.Destroy(_texture);
    }

    // ─── Размещение на карте ──────────────────────────────────

    /// <summary>
    /// Тайл левого нижнего угла сетки: над точкой спавна (игрок стоит у нижнего края, по центру),
    /// а если сверху не помещается в карту — под ней. Сетка не накрывает тайл игрока.
    /// </summary>
    public static Vector2Int ChooseOrigin(IMapDataProvider mapData, Vector2Int spawnTile, Vector2Int gridSize)
    {
        int maxX = Mathf.Max(0, mapData.MapWidth - gridSize.x);
        int maxY = Mathf.Max(0, mapData.MapHeight - gridSize.y);
        int x = Mathf.Clamp(spawnTile.x - gridSize.x / 2, 0, maxX);

        int above = spawnTile.y + 1;
        if (above <= maxY) return new Vector2Int(x, above);

        int below = spawnTile.y - gridSize.y;
        return new Vector2Int(x, Mathf.Clamp(below, 0, maxY));
    }

    // ─── Координаты ───────────────────────────────────────────

    /// <summary>Клетка сетки под мировой точкой (может быть вне сетки — проверяйте IsCellInGrid).</summary>
    public Vector2Int WorldToCell(Vector3 worldPosition) => _mapData.WorldToTileIndex(worldPosition) - _originTile;

    /// <summary>Центр клетки в мировых координатах.</summary>
    public Vector3 CellToWorld(Vector2Int cell) => _mapData.TileIndexToWorld(_originTile.x + cell.x, _originTile.y + cell.y);

    /// <summary>Клетка под точкой экрана (позиция мыши), по камере camera (null — Camera.main).</summary>
    public bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell, Camera camera = null)
    {
        camera ??= Camera.main;
        cell = default;
        if (camera == null) return false;

        Vector3 world = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
        cell = WorldToCell(world);
        return _hub.IsCellInGrid(cell);
    }

    public bool IsCellInGrid(Vector2Int cell) => _hub.IsCellInGrid(cell);

    /// <summary>
    /// Клетка левого нижнего угла для постройки, центрированной под курсором (для призрака):
    /// у 2×2 курсор — на правой верхней из центральных клеток.
    /// </summary>
    public Vector2Int GetPlacementCell(HubBuildingDefinition definition, Vector2Int cursorCell)
    {
        if (definition == null) return cursorCell;
        Vector2Int size = definition.Size;
        return new Vector2Int(cursorCell.x - size.x / 2, cursorCell.y - (size.y - 1) / 2);
    }

    // ─── Призрак ──────────────────────────────────────────────

    /// <summary>
    /// Показывает призрак постройки с левым нижним углом в cell: зелёный — можно, красный — нельзя.
    /// bonusAreas (T15) — области соседей, рядом с которыми появится бонус за соседство: обводятся золотой
    /// рамкой с лёгкой заливкой (null/пусто — подсветки нет).
    /// </summary>
    public void ShowGhost(HubBuildingDefinition definition, Vector2Int cell, bool valid,
        IReadOnlyList<RectInt> bonusAreas = null)
    {
        ShowGhostArea(definition, cell, valid);
        ShowBonusHighlights(definition != null ? bonusAreas : null);
    }

    private void ShowGhostArea(HubBuildingDefinition definition, Vector2Int cell, bool valid)
    {
        if (definition == null)
        {
            HideGhost();
            return;
        }

        EnsureGhost();
        _ghostRoot.SetActive(true);

        var area = new RectInt(cell, definition.Size);
        _ghostArea.transform.position = AreaCenter(area);
        _ghostArea.transform.localScale = AreaScale(area.size, 1f);
        _ghostArea.color = valid ? GhostValidColor : GhostInvalidColor;

        Sprite sprite = definition.Icon;
        _ghostSprite.enabled = sprite != null;
        if (sprite != null)
        {
            _ghostSprite.sprite = sprite;
            FitSprite(_ghostSprite.transform, sprite, area.size.x);
            _ghostSprite.transform.position += AreaBottomCenter(area);
            _ghostSprite.color = valid ? GhostSpriteTint : GhostInvalidColor * 1.5f;
        }
    }

    public void HideGhost()
    {
        if (_ghostRoot != null) _ghostRoot.SetActive(false);
    }

    /// <summary>
    /// Подсветка соседей-бонусов: по рамке (4 полосы) и заливке на область; лишние из пула выключаются.
    /// Живёт под корнем призрака — HideGhost прячет и её.
    /// </summary>
    private void ShowBonusHighlights(IReadOnlyList<RectInt> areas)
    {
        int count = areas?.Count ?? 0;
        if (count > 0) EnsureGhost();

        for (int i = 0; i < count; i++)
        {
            if (i == _bonusHighlights.Count)
                _bonusHighlights.Add(CreateBonusHighlight(i));

            GameObject highlight = _bonusHighlights[i];
            highlight.SetActive(true);
            LayoutBonusHighlight(highlight.transform, areas[i]);
        }

        for (int i = count; i < _bonusHighlights.Count; i++)
            _bonusHighlights[i].SetActive(false);
    }

    private GameObject CreateBonusHighlight(int index)
    {
        var root = new GameObject($"Bonus_{index}");
        root.transform.SetParent(_ghostRoot.transform, false);

        CreateSquare("Fill", root.transform, BonusFillColor, BonusOrder);
        for (int side = 0; side < 4; side++)
            CreateSquare($"Edge_{side}", root.transform, BonusEdgeColor, BonusOrder + 1);
        return root;
    }

    /// <summary>Заливка по области и 4 полосы рамки по её краям (порядок детей — как в CreateBonusHighlight).</summary>
    private void LayoutBonusHighlight(Transform root, RectInt area)
    {
        Vector3 center = AreaCenter(area);
        float width = area.size.x * Mathf.Abs(_cellStep.x);
        float height = area.size.y * Mathf.Abs(_cellStep.y);
        float edge = BonusEdge * Mathf.Abs(_cellStep.x);

        root.position = center;

        Transform fill = root.GetChild(0);
        fill.localPosition = Vector3.zero;
        fill.localScale = new Vector3(width, height, 1f);

        // низ, верх, лево, право
        SetBar(root.GetChild(1), new Vector3(0f, -(height - edge) * 0.5f, 0f), new Vector3(width, edge, 1f));
        SetBar(root.GetChild(2), new Vector3(0f, (height - edge) * 0.5f, 0f), new Vector3(width, edge, 1f));
        SetBar(root.GetChild(3), new Vector3(-(width - edge) * 0.5f, 0f, 0f), new Vector3(edge, height, 1f));
        SetBar(root.GetChild(4), new Vector3((width - edge) * 0.5f, 0f, 0f), new Vector3(edge, height, 1f));
    }

    private static void SetBar(Transform bar, Vector3 localPosition, Vector3 scale)
    {
        bar.localPosition = localPosition;
        bar.localScale = scale;
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleBuilt(HubBuildingDefinition definition, Vector2Int cell)
    {
        HideGhost();
        CreateBuildingView(new HubPlacement(definition.Id, definition, cell, _hub.GetLevel(definition)));
    }

    private void HandleUpgraded(HubBuildingDefinition definition, int level)
    {
        foreach (HubPlacement placed in _hub.GetPlaced())
            if (placed.Id == definition.Id)
            {
                UpdateLevelBadge(placed);
                return;
            }
    }

    // ─── Отрисовка ────────────────────────────────────────────

    private void BuildPlaza()
    {
        Vector2Int size = _hub.GridSize;
        var all = new RectInt(Vector2Int.zero, size);

        SpriteRenderer plaza = CreateSquare("Plaza", _root.transform, PlazaColor, PlazaOrder);
        plaza.transform.position = AreaCenter(all);
        plaza.transform.localScale = new Vector3(
            (size.x + PlazaMargin * 2f) * Mathf.Abs(_cellStep.x), (size.y + PlazaMargin * 2f) * Mathf.Abs(_cellStep.y), 1f);

        Transform cells = new GameObject("Cells").transform;
        cells.SetParent(_root.transform, false);

        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                SpriteRenderer cell = CreateSquare($"Cell_{x}_{y}", cells, CellColor, CellOrder);
                cell.transform.position = CellToWorld(new Vector2Int(x, y));
                cell.transform.localScale = AreaScale(Vector2Int.one, CellFill);
            }
    }

    private void CreateBuildingView(HubPlacement placed)
    {
        if (string.IsNullOrEmpty(placed.Id) || _buildingViews.ContainsKey(placed.Id)) return;

        RectInt area = placed.Area;
        GameObject view;

        if (placed.Definition != null && placed.Definition.Prefab != null)
        {
            // Префаб — опорной точкой в центр нижнего края области
            view = Object.Instantiate(placed.Definition.Prefab, AreaBottomCenter(area), Quaternion.identity, _buildingsRoot);
            view.name = placed.Id;
        }
        else
        {
            view = new GameObject(placed.Id);
            view.transform.SetParent(_buildingsRoot, false);
            view.transform.position = AreaBottomCenter(area);

            Sprite sprite = placed.Definition != null ? placed.Definition.Icon : null;
            if (sprite != null)
            {
                var spriteObject = new GameObject("Sprite");
                spriteObject.transform.SetParent(view.transform, false);
                SpriteRenderer sr = spriteObject.AddComponent<SpriteRenderer>();
                ApplyMaterial(sr);
                sr.sprite = sprite;
                FitSprite(spriteObject.transform, sprite, area.size.x);
                spriteObject.transform.position += view.transform.position;
            }
            else
            {
                // Неизвестная постройка (нет в каталоге) или без вида — тёмная клетка, чтобы место было видно занятым
                SpriteRenderer sr = CreateSquare("Missing", view.transform, new Color(0.2f, 0.2f, 0.2f, 0.8f), 0);
                sr.transform.position = AreaCenter(area);
                sr.transform.localScale = AreaScale(area.size, CellFill);
            }
        }

        // Порядок отрисовки по основанию — как у объектов карты
        if (!view.TryGetComponent(out DepthSortedSprite depth))
            depth = view.AddComponent<DepthSortedSprite>();
        depth.Refresh();

        _buildingViews[placed.Id] = view;
        UpdateLevelBadge(placed);
    }

    /// <summary>
    /// Значок уровня постройки: плашка с placed.Level золотыми квадратиками у правого нижнего угла области.
    /// Уровень 1 — без значка. Отдельный объект (не дочерний вида), чтобы DepthSortedSprite вида не
    /// перезаписал его порядок; порядок — по основанию постройки +1/+2.
    /// </summary>
    private void UpdateLevelBadge(HubPlacement placed)
    {
        if (string.IsNullOrEmpty(placed.Id)) return;

        if (_levelBadges.TryGetValue(placed.Id, out GameObject old))
        {
            if (old != null) Object.Destroy(old);
            _levelBadges.Remove(placed.Id);
        }

        int level = placed.Level;
        if (level < 2) return;

        RectInt area = placed.Area;
        float cell = Mathf.Abs(_cellStep.x);
        float pip = BadgePip * cell;
        float gap = BadgeGap * cell;
        float padding = BadgePadding * cell;
        float width = level * pip + (level - 1) * gap + padding * 2f;
        float height = pip + padding * 2f;

        Vector3 bottom = AreaBottomCenter(area);
        float right = bottom.x + area.size.x * cell * 0.5f;
        var center = new Vector3(right - width * 0.5f - padding, bottom.y + BadgeLift * Mathf.Abs(_cellStep.y), bottom.z);
        int order = DepthSorting.GetOrder(bottom.y);

        var badge = new GameObject($"{placed.Id}_Level{level}");
        badge.transform.SetParent(_buildingsRoot, false);
        badge.transform.position = center;

        SpriteRenderer plate = CreateSquare("Plate", badge.transform, BadgePlateColor, order + 1);
        plate.transform.localPosition = Vector3.zero;
        plate.transform.localScale = new Vector3(width, height, 1f);

        float x = -width * 0.5f + padding + pip * 0.5f;
        for (int i = 0; i < level; i++, x += pip + gap)
        {
            SpriteRenderer dot = CreateSquare($"Pip{i + 1}", badge.transform, BadgePipColor, order + 2);
            dot.transform.localPosition = new Vector3(x, 0f, 0f);
            dot.transform.localScale = new Vector3(pip, pip, 1f);
        }

        _levelBadges[placed.Id] = badge;
    }

    private void EnsureGhost()
    {
        if (_ghostRoot != null) return;

        _ghostRoot = new GameObject("Ghost");
        _ghostRoot.transform.SetParent(_root.transform, false);
        _ghostArea = CreateSquare("Area", _ghostRoot.transform, GhostValidColor, GhostAreaOrder);

        var spriteObject = new GameObject("Sprite");
        spriteObject.transform.SetParent(_ghostRoot.transform, false);
        _ghostSprite = spriteObject.AddComponent<SpriteRenderer>();
        ApplyMaterial(_ghostSprite);
        _ghostSprite.sortingOrder = GhostSpriteOrder;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private SpriteRenderer CreateSquare(string name, Transform parent, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        ApplyMaterial(sr);
        sr.sprite = _square;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private void ApplyMaterial(SpriteRenderer sr)
    {
        if (_spriteMaterial != null) sr.sharedMaterial = _spriteMaterial;
    }

    /// <summary>Центр области клеток в мире.</summary>
    private Vector3 AreaCenter(RectInt area)
    {
        Vector3 min = CellToWorld(area.min);
        Vector3 max = CellToWorld(area.max - Vector2Int.one);
        return (min + max) * 0.5f;
    }

    /// <summary>Середина нижнего края области — основание постройки.</summary>
    private Vector3 AreaBottomCenter(RectInt area)
    {
        Vector3 center = AreaCenter(area);
        return new Vector3(center.x, center.y - area.size.y * _cellStep.y * 0.5f, center.z);
    }

    private Vector3 AreaScale(Vector2Int size, float fill) =>
        new Vector3(size.x * Mathf.Abs(_cellStep.x) * fill, size.y * Mathf.Abs(_cellStep.y) * fill, 1f);

    /// <summary>
    /// Масштабирует спрайт по ширине области и ставит его так, чтобы низ спрайта был в локальном нуле
    /// (дальше вызывающий сдвигает на основание). Позиция выставляется мировая относительно нуля.
    /// </summary>
    private void FitSprite(Transform target, Sprite sprite, int widthInCells)
    {
        Bounds bounds = sprite.bounds;
        float width = widthInCells * Mathf.Abs(_cellStep.x);
        float scale = bounds.size.x > 0f ? width / bounds.size.x : 1f;

        target.localScale = new Vector3(scale, scale, 1f);
        target.position = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, 0f);
    }
}
