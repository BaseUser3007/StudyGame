using UnityEngine;

/// <summary>
/// Место забега: где стоит портал (вход и выход из забега) и где появляется игрок.
/// Строится генератором места (<see cref="IRunLocationGenerator"/>) после генерации карты;
/// дальше всё, что раскладывается «от спавна» (стартовый набор, лагеря, головоломки, туман, камера),
/// читает только SpawnTile, а зону портала — PortalArea.
/// </summary>
public sealed class RunLocation
{
    /// <summary>Seed, от которого построено место (обычно IMapDataProvider.Seed).</summary>
    public int Seed { get; }

    /// <summary>Тайл появления игрока — проходимый, вплотную к порталу (или внутри него, см. SpawnInsidePortal).</summary>
    public Vector2Int SpawnTile { get; }

    /// <summary>Тайлы, которые занимает портал (position — левый нижний тайл).</summary>
    public RectInt PortalArea { get; }

    /// <summary>Место выбрано запасным способом (нет подходящей суши) — портал может стоять неудачно.</summary>
    public bool UsedFallback { get; }

    public RunLocation(int seed, Vector2Int spawnTile, RectInt portalArea, bool usedFallback = false)
    {
        Seed = seed;
        SpawnTile = spawnTile;
        PortalArea = portalArea;
        UsedFallback = usedFallback;
    }

    /// <summary>Левый нижний тайл портала.</summary>
    public Vector2Int PortalTile => PortalArea.position;

    /// <summary>Центр портала в свободных координатах карты (тайлы, целые — центры тайлов).</summary>
    public Vector2 PortalPosition => new Vector2(
        PortalArea.xMin + (PortalArea.width - 1) * 0.5f,
        PortalArea.yMin + (PortalArea.height - 1) * 0.5f);

    /// <summary>Площадь портала в координатах карты (края тайлов — на x.5).</summary>
    public Rect PortalBounds => new Rect(
        PortalArea.xMin - 0.5f, PortalArea.yMin - 0.5f, PortalArea.width, PortalArea.height);

    /// <summary>
    /// Игрок появляется на самом портале (только запасной вариант без места рядом) —
    /// тогда коллайдер портала должен быть триггером, иначе он запрёт игрока.
    /// </summary>
    public bool SpawnInsidePortal => PortalArea.Contains(SpawnTile);
}
