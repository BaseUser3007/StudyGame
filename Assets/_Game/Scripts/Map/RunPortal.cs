using UnityEngine;

/// <summary>
/// Портал забега: игрок появляется вплотную к нему и уходит через него в лагерь (Lua-команда enterPortal
/// зовёт RunController.RequestExitThroughPortal, если игрок рядом — IsNear).
/// Стоит на RunLocation.PortalArea. Коллайдер сплошной (на портал нельзя ничего построить, через него не
/// ходят; игрок появляется рядом), кроме запасного случая, когда игрок появляется на самом портале, — тогда
/// все коллайдеры становятся триггерами, чтобы не запереть игрока.
/// Вид — префаб MapGenerationConfig.portalPrefab (опорная точка — основание); без префаба — рисованный
/// на лету овал, чтобы портал был виден до появления арта. Сортировка по высоте (DepthSortedSprite).
/// </summary>
[DisallowMultipleComponent]
public class RunPortal : MonoBehaviour
{
    // Основание спрайта чуть выше нижнего края площади портала
    private const float BaseInset = 0.1f;
    // Размер коллайдера по умолчанию (если в префабе его нет) — доля площади портала
    private const float ColliderWidthShare = 0.8f;
    private const float ColliderHeightShare = 0.6f;
    // Рисованный портал: ширина — доля площади, текстура 24×32
    private const float FallbackWidthShare = 0.9f;
    private const int FallbackTextureWidth = 24;
    private const int FallbackTextureHeight = 32;

    private static Sprite _fallbackSprite;

    // Play Mode без перезагрузки домена: спрайт прошлого запуска уничтожен вместе с текстурой
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (_fallbackSprite == null)
        {
            _fallbackSprite = null;
            return;
        }

        Texture2D texture = _fallbackSprite.texture;
        Object.Destroy(_fallbackSprite);
        if (texture != null) Object.Destroy(texture);
        _fallbackSprite = null;
    }

    /// <summary>Центр портала в координатах карты (тайлы, целые — центры тайлов).</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Тайлы портала (position — левый нижний).</summary>
    public RectInt TileArea { get; private set; }

    /// <summary>Левый нижний тайл портала.</summary>
    public Vector2Int Tile => TileArea.position;

    /// <summary>Площадь портала в координатах карты (края тайлов на x.5).</summary>
    public Rect Bounds => new Rect(TileArea.xMin - 0.5f, TileArea.yMin - 0.5f, TileArea.width, TileArea.height);

    /// <summary>Сплошной ли коллайдер (false — игрок появился на портале, коллайдеры — триггеры).</summary>
    public bool IsSolid { get; private set; }

    /// <summary>
    /// Проходимый тайл перед порталом — тайл появления игрока (RunLocation.SpawnTile, вплотную к порталу):
    /// отсюда enterPortal() дотягивается, а moveTo в центр упёрся бы в сплошной коллайдер.
    /// </summary>
    public Vector2Int ApproachTile { get; private set; }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Расстояние от точки карты до края портала (0 — точка на портале).</summary>
    public float DistanceTo(Vector2 mapPosition)
    {
        Rect bounds = Bounds;
        Vector2 closest = new Vector2(
            Mathf.Clamp(mapPosition.x, bounds.xMin, bounds.xMax),
            Mathf.Clamp(mapPosition.y, bounds.yMin, bounds.yMax));
        return Vector2.Distance(mapPosition, closest);
    }

    /// <summary>Точка карты не дальше radius от края портала.</summary>
    public bool IsNear(Vector2 mapPosition, float radius)
    {
        return DistanceTo(mapPosition) <= radius;
    }

    public bool ContainsTile(Vector2Int tile) => TileArea.Contains(tile);

    /// <summary>
    /// Создаёт портал на месте забега. prefab может быть null (рисованный портал);
    /// fallbackMaterial — материал рисованного спрайта (как у игрока), null — по умолчанию.
    /// </summary>
    public static RunPortal Create(GameObject prefab, IMapDataProvider map, RunLocation location,
        Material fallbackMaterial = null)
    {
        Debug.Assert(map != null, "RunPortal: map is not assigned");
        Debug.Assert(location != null, "RunPortal: location is not assigned");

        RectInt area = location.PortalArea;
        Vector2 basePoint = new Vector2(
            area.xMin + (area.width - 1) * 0.5f,
            area.yMin - 0.5f + BaseInset);
        Vector3 world = map.MapToWorld(basePoint);

        GameObject root = prefab != null
            ? Instantiate(prefab, world, Quaternion.identity)
            : new GameObject();
        root.name = "RunPortal";
        root.transform.position = world;

        if (!root.TryGetComponent(out RunPortal portal))
            portal = root.AddComponent<RunPortal>();
        portal.Setup(map, location, prefab == null, fallbackMaterial);
        return portal;
    }

    // ─── Setup ────────────────────────────────────────────────

    private void Setup(IMapDataProvider map, RunLocation location, bool drawFallback, Material fallbackMaterial)
    {
        TileArea = location.PortalArea;
        Position = location.PortalPosition;
        IsSolid = !location.SpawnInsidePortal;
        ApproachTile = location.SpawnTile;

        // Размер тайла в мире
        Vector3 unit = map.MapToWorld(Vector2.one) - map.MapToWorld(Vector2.zero);
        Vector2 tileSize = new Vector2(Mathf.Abs(unit.x), Mathf.Abs(unit.y));

        if (drawFallback)
            CreateFallbackVisual(tileSize, fallbackMaterial);

        if (GetComponentInChildren<Collider2D>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(
                TileArea.width * tileSize.x * ColliderWidthShare,
                TileArea.height * tileSize.y * ColliderHeightShare);
            box.offset = new Vector2(0f, box.size.y * 0.5f);
        }

        if (!IsSolid)
            foreach (Collider2D collider in GetComponentsInChildren<Collider2D>())
                collider.isTrigger = true;

        if (!TryGetComponent(out DepthSortedSprite sorting))
            sorting = gameObject.AddComponent<DepthSortedSprite>();
        sorting.Refresh();

        // Лагеря врагов и постройки проверяют пересечения коллайдеров сразу после создания портала
        Physics2D.SyncTransforms();
    }

    private void CreateFallbackVisual(Vector2 tileSize, Material material)
    {
        var visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);

        // Спрайт шириной 1 единица мира (pixelsPerUnit = ширина текстуры), растягиваем на площадь портала
        float scale = TileArea.width * tileSize.x * FallbackWidthShare;
        visual.transform.localScale = new Vector3(scale, scale, 1f);

        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = GetFallbackSprite();
        if (material != null) renderer.sharedMaterial = material;
    }

    // Овал-портал: фиолетовая рамка, голубое свечение внутри (общий на всю игру)
    private static Sprite GetFallbackSprite()
    {
        if (_fallbackSprite != null) return _fallbackSprite;

        int width = FallbackTextureWidth;
        int height = FallbackTextureHeight;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            name = "RunPortalOval"
        };

        var pixels = new Color32[width * height];
        var rim = new Color32(140, 80, 230, 255);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = (x + 0.5f - width * 0.5f) / (width * 0.5f);
                float dy = (y + 0.5f - height * 0.5f) / (height * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                Color32 color = new Color32(0, 0, 0, 0);
                if (r <= 1f)
                    color = r > 0.75f
                        ? rim
                        : new Color32(90, 200, 255, (byte)(255 * Mathf.Lerp(0.95f, 0.45f, r / 0.75f)));
                pixels[y * width + x] = color;
            }

        texture.SetPixels32(pixels);
        texture.Apply();
        _fallbackSprite = Sprite.Create(
            texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), width);
        _fallbackSprite.name = "RunPortalOval";
        return _fallbackSprite;
    }
}
