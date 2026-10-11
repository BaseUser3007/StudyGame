using UnityEngine;

/// <summary>
/// Базовый класс для всех ресурсов игры.
/// </summary>
public abstract class ResourceItem : ScriptableObject
{
    [Header("Base")]
    public string itemId;
    public string displayName;

    [Header("World")]
    [Tooltip("Объект, который появляется на земле, когда предмет выбрасывают командой drop " +
             "(его можно подобрать через harvest). Пусто — выброшенный предмет исчезает")]
    public ResourceObject worldPrefab;

    [Header("Icon")]
    [Tooltip("Префаб, с которого берётся иконка (первый SpriteRenderer со спрайтом, включая дочерние). " +
             "Не задан — берётся с World Prefab. Меняете спрайт в префабе — меняется и иконка в инвентаре и крафте")]
    public GameObject iconPrefab;
    [Tooltip("Запасная иконка — для предметов без префаба (инструменты, бревно)")]
    public Sprite sprite;

    [Header("Harvest")]
    [Tooltip("Прочность: сколько энергии тратится на добычу одной единицы ресурса")]
    [Min(0f)] public float durability = 1f;

    [Header("Inventory")]
    public int maxStackSize = 20;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>
    /// Иконка для UI: спрайт префаба (iconPrefab или, у построек, сам префаб постройки),
    /// иначе запасной sprite. Везде в интерфейсе используйте её, а не sprite напрямую.
    /// </summary>
    public Sprite Icon => SpriteFrom(IconSource) ?? sprite;

    /// <summary>
    /// Префаб для иконки. Наследники могут подставить свой (StructureItem — префаб постройки).
    /// </summary>
    protected virtual GameObject IconSource =>
        iconPrefab != null ? iconPrefab : worldPrefab != null ? worldPrefab.gameObject : null;

    private static Sprite SpriteFrom(GameObject prefab)
    {
        if (prefab == null) return null;

        foreach (SpriteRenderer renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer.sprite != null)
                return renderer.sprite;

        return null;
    }

    /// <summary>
    /// Подходит ли название из Lua (itemId, отображаемое имя или имя ассета, без учёта регистра).
    /// </summary>
    public bool MatchesName(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        query = query.Trim();

        return string.Equals(query, itemId, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, displayName, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, name, System.StringComparison.OrdinalIgnoreCase);
    }
}