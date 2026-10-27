using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Плиты по порядку (plates, T16): площадка 5×5, на ней 4–5 плит с уникальными номерами (<see cref="PlatesLayout"/>).
/// Номера видно только через Lua check(x, y).plate. Игрок должен наступить на плиты по возрастанию номеров
/// (<see cref="PlatesSequence"/>): неверная плита отжимает все. Шаги приходят из PuzzleManager
/// (PlayerEventBus.OnPlayerMoved). Плиты — «пол»: без коллайдеров и без сортировки по высоте.
/// Префаб плиты необязателен — без него рисуется квадрат, созданный в коде.
/// </summary>
public class PlatesPuzzle : PuzzleStructure
{
    /// <summary>Порядок отрисовки плит: над тайлмапами, под объектами (как площадка лагеря).</summary>
    public const int SortingOrder = 20;

    // Нажатая плита — темнее и чуть меньше; решённая площадка остаётся нажатой
    private static readonly Color PressedTint = new Color(0.55f, 0.8f, 0.55f, 1f);
    private const float PressedScale = 0.88f;
    // Квадрат без префаба: доля тайла и цвет камня
    private const float FallbackSize = 0.8f;
    private static readonly Color FallbackColor = new Color(0.62f, 0.6f, 0.55f, 1f);

    private static Sprite _fallbackSprite;

    private PlatesLayout _layout;
    private PlatesSequence _sequence;
    private Vector2Int _origin;
    private readonly List<PlateView> _views = new List<PlateView>();

    public override string TypeId => PuzzleTypes.Plates;
    public PlatesLayout Layout => _layout;
    public PlatesSequence Sequence => _sequence;
    /// <summary>Нижний левый тайл площадки на карте.</summary>
    public Vector2Int Origin => _origin;
    public RectInt TileArea => new RectInt(_origin.x, _origin.y, PlatesLayout.Size, PlatesLayout.Size);
    public Vector2Int CenterTile => _origin + PlatesLayout.Center;
    public int PlateCount => _layout != null ? _layout.Count : 0;
    public int PressedCount => _sequence != null ? _sequence.PressedCount : 0;
    public override Rect Bounds =>
        new Rect(_origin.x - 0.5f, _origin.y - 0.5f, PlatesLayout.Size, PlatesLayout.Size);

    /// <summary>Создаёт плиты. platePrefab может быть null — тогда квадрат из кода.</summary>
    public void Build(PlatesLayout layout, Vector2Int origin, IMapDataProvider mapData, GameObject platePrefab)
    {
        _layout = layout;
        _origin = origin;
        _sequence = new PlatesSequence(layout.Numbers);

        for (int i = 0; i < layout.Count; i++)
        {
            Vector2Int tile = origin + layout.Tiles[i];
            Vector3 world = mapData.TileIndexToWorld(tile.x, tile.y);
            GameObject plate = platePrefab != null
                ? Instantiate(platePrefab, world, Quaternion.identity, transform)
                : CreateFallbackPlate(world, mapData);
            plate.name = $"Plate_{i + 1}";
            PrepareAsFloor(plate);
            _views.Add(new PlateView(plate));
        }

        RefreshViews();
    }

    // ─── Queries ──────────────────────────────────────────────

    /// <summary>Плита на тайле карты: номер и нажата ли.</summary>
    public bool TryGetPlate(Vector2Int tile, out int number, out bool pressed)
    {
        int index = IndexAt(tile);
        if (index < 0)
        {
            number = 0;
            pressed = false;
            return false;
        }

        number = _sequence.GetNumber(index);
        pressed = IsSolved || _sequence.IsPressed(index);
        return true;
    }

    public bool IsPlate(Vector2Int tile) => IndexAt(tile) >= 0;

    /// <summary>Игрок встал на тайл карты (зовёт PuzzleManager).</summary>
    public PlateStepResult Step(Vector2Int tile)
    {
        if (_sequence == null) return PlateStepResult.NotAPlate;
        int index = IndexAt(tile);
        if (index < 0) return PlateStepResult.NotAPlate;
        if (IsSolved) return PlateStepResult.AlreadySolved;

        PlateStepResult result = _sequence.Step(index);
        RefreshViews();
        return result;
    }

    protected override void OnSolved()
    {
        RefreshViews();
    }

    // ─── Helpers ──────────────────────────────────────────────

    private int IndexAt(Vector2Int tile) => _layout != null ? _layout.IndexOf(tile - _origin) : -1;

    private void RefreshViews()
    {
        for (int i = 0; i < _views.Count; i++)
            _views[i].SetPressed(IsSolved || (_sequence != null && _sequence.IsPressed(i)));
    }

    private GameObject CreateFallbackPlate(Vector3 world, IMapDataProvider mapData)
    {
        var plate = new GameObject("Plate");
        plate.transform.SetParent(transform, false);
        plate.transform.position = world;

        // Размер тайла в мире — как у стен лабиринта
        Vector3 unit = mapData.MapToWorld(Vector2.one) - mapData.MapToWorld(Vector2.zero);
        plate.transform.localScale = new Vector3(Mathf.Abs(unit.x) * FallbackSize, Mathf.Abs(unit.y) * FallbackSize, 1f);

        var renderer = plate.AddComponent<SpriteRenderer>();
        renderer.sprite = GetFallbackSprite();
        renderer.color = FallbackColor;
        return plate;
    }

    // Белый квадрат 1×1 единицу мира (общий на всю игру; текстура создаётся один раз)
    internal static Sprite GetFallbackSprite()
    {
        if (_fallbackSprite != null) return _fallbackSprite;

        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "PlateSquare" };
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        _fallbackSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        _fallbackSprite.name = "PlateSquare";
        return _fallbackSprite;
    }

    // Плита — пол: по ней ходят (сплошные коллайдеры → триггеры), сортировка по высоте не нужна
    internal static void PrepareAsFloor(GameObject plate)
    {
        foreach (Collider2D collider in plate.GetComponentsInChildren<Collider2D>())
        {
            if (collider.isTrigger) continue;
            collider.isTrigger = true;
            Debug.LogWarning("PlatesPuzzle: коллайдер плиты переключён в триггер — по плитам ходят", plate);
        }

        foreach (DepthSortedSprite sorting in plate.GetComponentsInChildren<DepthSortedSprite>())
            sorting.enabled = false;

        foreach (SpriteRenderer sr in plate.GetComponentsInChildren<SpriteRenderer>())
            sr.sortingOrder = SortingOrder;
    }

    /// <summary>Вид одной плиты: исходные цвета и масштаб, нажатая — оттенок и меньше.</summary>
    private sealed class PlateView
    {
        private readonly Transform _transform;
        private readonly Vector3 _baseScale;
        private readonly SpriteRenderer[] _renderers;
        private readonly Color[] _baseColors;

        public PlateView(GameObject plate)
        {
            _transform = plate.transform;
            _baseScale = _transform.localScale;
            _renderers = plate.GetComponentsInChildren<SpriteRenderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _baseColors[i] = _renderers[i].color;
        }

        public void SetPressed(bool pressed)
        {
            if (_transform == null) return;
            _transform.localScale = pressed ? _baseScale * PressedScale : _baseScale;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].color = pressed ? _baseColors[i] * PressedTint : _baseColors[i];
            }
        }
    }
}
