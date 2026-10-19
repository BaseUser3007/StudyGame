using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Результат проверки или попытки постройки в хабе.</summary>
public enum HubBuildResult
{
    Ok,
    /// <summary>Определения нет в каталоге.</summary>
    Unknown,
    /// <summary>Постройка не открыта достижением.</summary>
    Locked,
    /// <summary>Уже стоит в хабе (в срезе каждая — один раз).</summary>
    AlreadyBuilt,
    /// <summary>Не хватает материалов на складе хаба.</summary>
    NotAffordable,
    /// <summary>Область выходит за сетку.</summary>
    OutOfBounds,
    /// <summary>Область пересекается с другой постройкой.</summary>
    Occupied
}

/// <summary>
/// Постройка, стоящая в хабе: определение (null — id из сохранения, которого нет в каталоге) и клетка
/// левого нижнего угла.
/// </summary>
public readonly struct HubPlacement
{
    public readonly string Id;
    public readonly HubBuildingDefinition Definition;
    public readonly Vector2Int Cell;

    public HubPlacement(string id, HubBuildingDefinition definition, Vector2Int cell)
    {
        Id = id;
        Definition = definition;
        Cell = cell;
    }

    /// <summary>Занятая область; неизвестная постройка занимает одну клетку.</summary>
    public RectInt Area => new RectInt(Cell, Definition != null ? Definition.Size : Vector2Int.one);
}

/// <summary>
/// Логика хаба (этап 4, plain class из Level в режиме Hub): сетка, проверка места, оплата материалами
/// со склада MetaProgress, запись расстановки и сохранение. Клетки — (0,0) левый нижний угол сетки,
/// постройка ставится левым нижним углом в cell. Каждая постройка — один раз, без сноса.
/// Открытость — через UnlockRegistry (награды достижений). Мировую часть рисует HubGrid по OnBuilt.
/// </summary>
public class HubService
{
    /// <summary>Постройка поставлена (уже оплачена и сохранена).</summary>
    public event Action<HubBuildingDefinition, Vector2Int> OnBuilt;

    private readonly HubBuildingCatalog _catalog;
    private readonly MetaProgress _progress;
    private readonly UnlockRegistry _unlocks;

    public Vector2Int GridSize { get; }

    /// <summary>Все постройки каталога (порядок панели). Пусто без каталога.</summary>
    public IReadOnlyList<HubBuildingDefinition> Definitions { get; }

    public MetaProgress Progress => _progress;

    /// <param name="unlocks">Открытые постройки; null — все открыты (тесты).</param>
    public HubService(HubBuildingCatalog catalog, MetaProgress progress, UnlockRegistry unlocks)
    {
        Debug.Assert(progress != null, "HubService: MetaProgress is null");

        _catalog = catalog;
        _progress = progress;
        _unlocks = unlocks;
        GridSize = catalog != null ? catalog.GridSize : HubBuildingCatalog.DefaultGridSize;
        Definitions = CollectDefinitions(catalog);

        if (catalog == null)
            Debug.LogWarning("HubService: HubBuildingCatalog is not assigned — nothing can be built in the camp");

        foreach (HubPlacement placed in GetPlaced())
            if (placed.Definition == null)
                Debug.LogWarning($"HubService: saved camp building '{placed.Id}' is not in the catalog — shown as an occupied cell");
    }

    // ─── Состояние ────────────────────────────────────────────

    public HubBuildingDefinition Find(string id) => _catalog != null ? _catalog.Find(id) : null;

    public bool IsUnlocked(HubBuildingDefinition definition) =>
        definition != null && (_unlocks == null || _unlocks.IsHubBuildingUnlocked(definition.Id));

    public bool IsBuilt(HubBuildingDefinition definition) =>
        definition != null && _progress.HasHubBuilding(definition.Id);

    /// <summary>Сколько материала с этим ключом на складе хаба.</summary>
    public int GetStored(string key) => _progress.GetMaterialCount(key);

    /// <summary>Хватает ли материалов склада на постройку.</summary>
    public bool CanAfford(HubBuildingDefinition definition)
    {
        if (definition == null) return false;

        foreach (HubBuildingCost cost in definition.GetCost())
            if (_progress.GetMaterialCount(cost.Key) < cost.Amount)
                return false;
        return true;
    }

    /// <summary>
    /// Можно ли сейчас выбрать постройку для размещения (без учёта клетки): известна, открыта,
    /// ещё не стоит, материалов хватает. Для кнопок панели.
    /// </summary>
    public HubBuildResult CheckAvailability(HubBuildingDefinition definition)
    {
        if (definition == null || Find(definition.Id) != definition) return HubBuildResult.Unknown;
        if (IsBuilt(definition)) return HubBuildResult.AlreadyBuilt;
        if (!IsUnlocked(definition)) return HubBuildResult.Locked;
        if (!CanAfford(definition)) return HubBuildResult.NotAffordable;
        return HubBuildResult.Ok;
    }

    // ─── Сетка ────────────────────────────────────────────────

    /// <summary>Поставленные постройки (из сохранения) с определениями.</summary>
    public List<HubPlacement> GetPlaced()
    {
        var result = new List<HubPlacement>();
        foreach (PlacedHubBuilding placed in _progress.HubBuildings)
            result.Add(new HubPlacement(placed.buildingId, Find(placed.buildingId), new Vector2Int(placed.x, placed.y)));
        return result;
    }

    public bool IsCellInGrid(Vector2Int cell) =>
        cell.x >= 0 && cell.y >= 0 && cell.x < GridSize.x && cell.y < GridSize.y;

    /// <summary>Область постройки с левым нижним углом в cell целиком внутри сетки.</summary>
    public bool IsInBounds(HubBuildingDefinition definition, Vector2Int cell)
    {
        if (definition == null) return false;
        Vector2Int size = definition.Size;
        return cell.x >= 0 && cell.y >= 0 && cell.x + size.x <= GridSize.x && cell.y + size.y <= GridSize.y;
    }

    /// <summary>Область не пересекается с поставленными постройками.</summary>
    public bool IsAreaFree(HubBuildingDefinition definition, Vector2Int cell)
    {
        if (definition == null) return false;

        var area = new RectInt(cell, definition.Size);
        foreach (HubPlacement placed in GetPlaced())
            if (placed.Area.Overlaps(area))
                return false;
        return true;
    }

    /// <summary>Постройка, занимающая клетку, или null-структура (Id == null).</summary>
    public HubPlacement GetPlacementAt(Vector2Int cell)
    {
        foreach (HubPlacement placed in GetPlaced())
            if (placed.Area.Contains(cell))
                return placed;
        return default;
    }

    /// <summary>Только место: в границах и свободно (для подсветки призрака).</summary>
    public bool CanPlaceAt(HubBuildingDefinition definition, Vector2Int cell) =>
        IsInBounds(definition, cell) && IsAreaFree(definition, cell);

    /// <summary>Полная проверка постройки в клетке, без изменений.</summary>
    public HubBuildResult CheckBuild(HubBuildingDefinition definition, Vector2Int cell)
    {
        HubBuildResult availability = CheckAvailability(definition);
        if (availability != HubBuildResult.Ok) return availability;
        if (!IsInBounds(definition, cell)) return HubBuildResult.OutOfBounds;
        if (!IsAreaFree(definition, cell)) return HubBuildResult.Occupied;
        return HubBuildResult.Ok;
    }

    // ─── Постройка ────────────────────────────────────────────

    /// <summary>
    /// Ставит постройку: списывает материалы со склада, записывает расстановку, сохраняет MetaProgress,
    /// публикует GameEventBus HubBuildingBuilt (Id = id постройки) и OnBuilt. При отказе ничего не меняет.
    /// </summary>
    public HubBuildResult TryBuild(HubBuildingDefinition definition, Vector2Int cell)
    {
        HubBuildResult result = CheckBuild(definition, cell);
        if (result != HubBuildResult.Ok) return result;

        // CanAfford проверен в CheckBuild — списание всех строк пройдёт
        foreach (HubBuildingCost cost in definition.GetCost())
        {
            bool removed = _progress.RemoveMaterial(cost.Key, cost.Amount);
            Debug.Assert(removed, $"HubService: failed to take {cost.Amount} × {cost.Key} after CanAfford");
        }

        _progress.AddHubBuilding(definition.Id, cell);
        _progress.Save();

        GameEventBus.Publish(GameEventType.HubBuildingBuilt, definition.Id);
        OnBuilt?.Invoke(definition, cell);
        return HubBuildResult.Ok;
    }

    /// <summary>Текст отказа для UI (английский).</summary>
    public static string Describe(HubBuildResult result)
    {
        switch (result)
        {
            case HubBuildResult.Ok: return "Ready to build";
            case HubBuildResult.Unknown: return "Unknown building";
            case HubBuildResult.Locked: return "Not unlocked yet — complete an achievement";
            case HubBuildResult.AlreadyBuilt: return "Already built";
            case HubBuildResult.NotAffordable: return "Not enough materials";
            case HubBuildResult.OutOfBounds: return "Doesn't fit the camp grid";
            case HubBuildResult.Occupied: return "The place is occupied";
            default: return result.ToString();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static IReadOnlyList<HubBuildingDefinition> CollectDefinitions(HubBuildingCatalog catalog)
    {
        var result = new List<HubBuildingDefinition>();
        if (catalog == null) return result;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (HubBuildingDefinition definition in catalog.Buildings)
            if (definition != null && ids.Add(definition.Id))
                result.Add(definition);
        return result;
    }
}
