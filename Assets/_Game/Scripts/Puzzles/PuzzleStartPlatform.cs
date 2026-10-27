using UnityEngine;

/// <summary>
/// Вид стартовой платформы головоломки (T25): «пол» как у плит (PlatesPuzzle.SortingOrder, без коллайдеров
/// и сортировки по высоте). Префаб необязателен (PuzzleConfig.startPlatformPrefab) — без него золотистый
/// квадрат из кода, чтобы отличался от серых плит.
/// </summary>
public static class PuzzleStartPlatform
{
    // Квадрат без префаба: доля тайла и цвет
    private const float FallbackSize = 0.9f;
    private static readonly Color FallbackColor = new Color(0.95f, 0.78f, 0.25f, 0.9f);

    /// <summary>Создаёт вид платформы на тайле под parent. prefab может быть null.</summary>
    public static GameObject Create(Transform parent, Vector2Int tile, IMapDataProvider mapData, GameObject prefab)
    {
        Vector3 world = mapData.TileIndexToWorld(tile.x, tile.y);
        GameObject platform;
        if (prefab != null)
        {
            platform = Object.Instantiate(prefab, world, Quaternion.identity, parent);
        }
        else
        {
            platform = new GameObject();
            platform.transform.SetParent(parent, false);
            platform.transform.position = world;

            // Размер тайла в мире — как у плит
            Vector3 unit = mapData.MapToWorld(Vector2.one) - mapData.MapToWorld(Vector2.zero);
            platform.transform.localScale =
                new Vector3(Mathf.Abs(unit.x) * FallbackSize, Mathf.Abs(unit.y) * FallbackSize, 1f);

            var renderer = platform.AddComponent<SpriteRenderer>();
            renderer.sprite = PlatesPuzzle.GetFallbackSprite();
            renderer.color = FallbackColor;
        }

        platform.name = $"StartPlatform_{tile.x}_{tile.y}";
        PlatesPuzzle.PrepareAsFloor(platform);
        return platform;
    }
}
