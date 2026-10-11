using UnityEngine;

/// <summary>
/// Постройка на карте (верстак и т.д.). Ставится командой build из предмета-постройки.
/// Место на земле определяет Collider2D префаба (см. ObjectFootprint), опорная точка — основание.
/// </summary>
public class Structure : MonoBehaviour
{
    [Tooltip("Какую станцию крафта даёт постройка (None — просто постройка)")]
    [SerializeField] private CraftingStation station = CraftingStation.None;

    [Tooltip("Спальное место: закончив день рядом с ним, игрок получает бонус к энергии на следующий день")]
    [SerializeField] private bool isSleepingPlace;

    public CraftingStation Station => station;
    public bool IsSleepingPlace => isSleepingPlace;
    public StructureItem Item { get; private set; }

    /// <summary>
    /// Позиция на карте в тайлах (не привязана к центру тайла).
    /// </summary>
    public Vector2 Position { get; private set; }
    public Vector2Int Tile => MapPosition.ToTile(Position);

    private DepthSortedSprite _depthSorting;

    private void Awake()
    {
        // Сортировка по высоте, как у остальных объектов карты
        if (!TryGetComponent(out _depthSorting))
            _depthSorting = gameObject.AddComponent<DepthSortedSprite>();
    }

    /// <summary>
    /// Вызывается после установки: transform уже стоит на месте.
    /// </summary>
    public void Place(StructureItem item, Vector2 position)
    {
        Item = item;
        Position = position;
        _depthSorting.Refresh();
    }
}
