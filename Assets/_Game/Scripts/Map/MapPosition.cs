using UnityEngine;

/// <summary>
/// Свободные координаты карты: единица — один тайл, целые значения — центры тайлов.
/// </summary>
public static class MapPosition
{
    /// <summary>
    /// Тайл, содержащий точку. Граница тайла — на половине (x.5).
    /// </summary>
    public static Vector2Int ToTile(Vector2 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x + 0.5f),
            Mathf.FloorToInt(position.y + 0.5f));
    }

    public static int ToTile(double coordinate)
    {
        return Mathf.FloorToInt((float)coordinate + 0.5f);
    }

    /// <summary>
    /// Мировые координаты → свободная позиция на карте (обратное к IMapDataProvider.MapToWorld).
    /// Для подвижных объектов (игрок, враги); тайл из неё — через ToTile, не округлением transform.position.
    /// </summary>
    public static Vector2 FromWorld(IMapDataProvider mapData, Vector3 world)
    {
        Vector3 origin = mapData.MapToWorld(Vector2.zero);
        Vector3 unit = mapData.MapToWorld(Vector2.one) - origin;
        return new Vector2(
            Mathf.Approximately(unit.x, 0f) ? 0f : (world.x - origin.x) / unit.x,
            Mathf.Approximately(unit.y, 0f) ? 0f : (world.y - origin.y) / unit.y);
    }
}
