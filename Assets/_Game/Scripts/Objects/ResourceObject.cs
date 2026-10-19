using System;
using UnityEngine;

public abstract class ResourceObject : MonoBehaviour
{
    public event Action<ResourceObject> OnConsumed;

    [Header("Variant")]
    [SerializeField] protected ResourceVariant variant;

    public bool IsAvailable { get; private set; } = true;
    /// <summary>
    /// Объект выброшен игроком (drop): его подбор — не новая добыча, достижения его не считают.
    /// Сбрасывается при выдаче из пула.
    /// </summary>
    public bool DroppedByPlayer { get; set; }
    /// <summary>
    /// Номер запуска кода, в котором объект появился (лут от mine — GameEventBus.ActiveRunId), 0 — нет.
    /// Сбрасывается при выдаче из пула.
    /// </summary>
    public int SpawnRunId { get; set; }
    public ResourceVariant Variant => variant;
    /// <summary>
    /// Позиция на карте в тайлах (не привязана к центру тайла).
    /// </summary>
    public Vector2 Position { get; private set; }

    /// <summary>
    /// Тайл, в котором лежит объект.
    /// </summary>
    public Vector2Int Tile => MapPosition.ToTile(Position);

    public abstract ToolType? RequiredTool { get; }
    public abstract float InteractionRadius { get; }

    private DepthSortedSprite _depthSorting;

    // Спрайт настраивается в самом префабе — код его не трогает
    protected virtual void Awake()
    {
        // Сортировка по высоте нужна всем объектам — добавляем, если на префабе её нет
        if (!TryGetComponent(out _depthSorting))
            _depthSorting = gameObject.AddComponent<DepthSortedSprite>();
    }

    public void SetVariant(ResourceVariant newVariant)
    {
        variant = newVariant;
    }

    /// <summary>
    /// Логическая позиция. transform к этому моменту уже стоит на месте (ObjectPool.Get),
    /// поэтому здесь же пересчитываем порядок отрисовки.
    /// </summary>
    public void SetPosition(Vector2 position)
    {
        Position = position;
        if (_depthSorting != null)
            _depthSorting.Refresh();
    }

    public void ResetState()
    {
        IsAvailable = true;
        DroppedByPlayer = false;
        SpawnRunId = 0;
    }

    public (ResourceItem item, int amount)? Interact()
    {
        if (!IsAvailable) return null;
        return OnInteract();
    }

    protected abstract (ResourceItem item, int amount)? OnInteract();

    protected void Consume()
    {
        IsAvailable = false;
        OnConsumed?.Invoke(this);
    }
}