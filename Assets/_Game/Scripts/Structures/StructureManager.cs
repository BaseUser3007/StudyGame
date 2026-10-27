using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Постройки на карте: установка с проверкой места и поиск станций рядом с персонажем.
/// Создаётся в Level, постройки складываются в объект "Structures" на сцене.
/// </summary>
public class StructureManager : IStationProvider, ISleepingPlaceProvider
{
    // Какой зазор считается «свободно»: коллайдер не должен касаться чужих (в тайлах)
    private const float PlacementSkin = 0.02f;

    private readonly IMapDataProvider _mapData;
    private readonly PlayerCommandExecutor _executor;
    private readonly float _stationRadius;
    private readonly Transform _container;
    private readonly List<Structure> _structures = new List<Structure>();
    private readonly List<Collider2D> _overlaps = new List<Collider2D>();
    private PuzzleManager _puzzles;

    public IReadOnlyList<Structure> Structures => _structures;

    public StructureManager(IMapDataProvider mapData, PlayerCommandExecutor executor, float stationRadius)
    {
        _mapData = mapData;
        _executor = executor;
        _stationRadius = stationRadius;
        _container = new GameObject("Structures").transform;
    }

    // ─── Placement ────────────────────────────────────────────

    /// <summary>
    /// Ставит постройку в точку карты. Возвращает null при успехе, иначе причину.
    /// Предмет из инвентаря не забирает — это делает вызывающий.
    /// </summary>
    public string TryPlace(StructureItem item, Vector2 position, out Structure placed)
    {
        placed = null;
        string problem = CheckPlace(item, position);
        if (problem != null) return problem;

        placed = Object.Instantiate(item.prefab, _mapData.MapToWorld(position), Quaternion.identity, _container);
        placed.Place(item, position);
        _structures.Add(placed);

        // Коллайдер новой постройки должен сразу участвовать в проверках движения
        Physics2D.SyncTransforms();
        GameEventBus.Publish(GameEventType.Built, MetaProgress.GetItemKey(item), 1, placed.Station.ToString());
        _executor.PlayAction(PlayerActionKind.Interact, position);
        return null;
    }

    /// <summary>
    /// Головоломки создаются позже построек (Level) — области головоломок передаются отдельно; null — без проверки.
    /// </summary>
    public void SetPuzzles(PuzzleManager puzzles) => _puzzles = puzzles;

    /// <summary>
    /// Можно ли поставить постройку: под её коллайдером суша, не головоломка и нет чужих коллайдеров (в том числе персонажа).
    /// </summary>
    public string CheckPlace(StructureItem item, Vector2 position)
    {
        if (item == null || item.prefab == null)
            return "this item can't be built";

        Rect area = ObjectFootprint.Get(item.prefab);
        area.position += position;

        Vector2Int min = MapPosition.ToTile(area.min);
        Vector2Int max = MapPosition.ToTile(area.max);
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (!_mapData.IsWalkable(x, y))
                    return "can't build on water";

        // Постройку не снести — на головоломке (плиты, лабиринт) или её стартовой платформе она сделала бы
        // головоломку нерешаемой
        if (_puzzles != null)
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                {
                    var tile = new Vector2Int(x, y);
                    // Платформа решённой головоломки скрыта — как и check(), её не считаем
                    if (_puzzles.GetPuzzleAt(tile) != null || LuaPuzzles.GetByStartTile(_puzzles, tile) != null)
                        return "can't build on a puzzle";
                }

        if (IsOccupied(area))
            return "place is occupied";

        return null;
    }

    // ─── Stations ─────────────────────────────────────────────

    public bool IsStationNearPlayer(CraftingStation station)
    {
        return FindStationNear(station, _executor.CurrentTile, _stationRadius) != null;
    }

    /// <summary>
    /// Стоит ли персонаж у спального места (в пределах того же радиуса, что и станции).
    /// </summary>
    public bool IsSleepingPlaceNearPlayer()
    {
        Vector2 point = _executor.CurrentTile;

        foreach (Structure structure in _structures)
        {
            if (structure == null || !structure.IsSleepingPlace) continue;

            Rect area = ObjectFootprint.Get(structure);
            area.position += structure.Position;
            if (DistanceToRect(point, area) <= _stationRadius)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Постройка, чей след (Collider2D) заходит на тайл, или null.
    /// Касание краем не считается.
    /// </summary>
    public Structure GetStructureAt(Vector2Int tile)
    {
        Rect tileRect = new Rect(tile.x - 0.5f, tile.y - 0.5f, 1f, 1f);

        foreach (Structure structure in _structures)
        {
            if (structure == null) continue;

            Rect area = ObjectFootprint.Get(structure);
            area.position += structure.Position;

            // Без коллайдера след — точка: постройка стоит в тайле, где её основание
            bool hit = area.width <= 0f || area.height <= 0f
                ? structure.Tile == tile
                : area.xMin < tileRect.xMax && area.xMax > tileRect.xMin
                  && area.yMin < tileRect.yMax && area.yMax > tileRect.yMin;

            if (hit) return structure;
        }

        return null;
    }

    /// <summary>
    /// Ближайшая постройка-станция, до которой от точки не дальше radius (до края её коллайдера).
    /// </summary>
    public Structure FindStationNear(CraftingStation station, Vector2 point, float radius)
    {
        Structure nearest = null;
        float minDistance = float.MaxValue;

        foreach (Structure structure in _structures)
        {
            if (structure == null || structure.Station != station) continue;

            Rect area = ObjectFootprint.Get(structure);
            area.position += structure.Position;
            float distance = DistanceToRect(point, area);

            if (distance <= radius && distance < minDistance)
            {
                minDistance = distance;
                nearest = structure;
            }
        }

        return nearest;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool IsOccupied(Rect mapArea)
    {
        Physics2D.SyncTransforms();

        Vector2 center = _mapData.MapToWorld(mapArea.center);
        // Постройка без коллайдера — точка; проверяем небольшой квадрат вокруг неё
        Vector2 size = new Vector2(
            Mathf.Max(mapArea.width, 0.2f) + PlacementSkin * 2f,
            Mathf.Max(mapArea.height, 0.2f) + PlacementSkin * 2f);

        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        return Physics2D.OverlapBox(center, size, 0f, filter, _overlaps) > 0;
    }

    private static float DistanceToRect(Vector2 point, Rect rect)
    {
        Vector2 closest = new Vector2(
            Mathf.Clamp(point.x, rect.xMin, rect.xMax),
            Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        return Vector2.Distance(point, closest);
    }
}
