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
}
