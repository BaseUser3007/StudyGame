using UnityEngine;
using UnityEngine.Tilemaps;

public enum FogState
{
    Unexplored,
    Explored,
    Visible
}

public class FogOfWarSystem : MonoBehaviour, IFogProvider
{
    [SerializeField] private Tilemap fogTilemap;

    private IMapDataProvider _mapData;
    private IPlayerDataProvider _playerData;
    private FogState[,] _fogMap;
    private Vector2Int _lastPlayerTile;
    private TileBase _fogTile;
    // Области, где обзор ограничен (лабиринт), — см. AddHedgeArea
    private readonly System.Collections.Generic.List<HedgeArea> _hedges
        = new System.Collections.Generic.List<HedgeArea>();

    private static readonly Color ColorUnexplored = new Color(0f, 0f, 0f, 1f);
    private static readonly Color ColorExplored = new Color(0f, 0f, 0f, 0.5f);
    private static readonly Color ColorVisible = new Color(0f, 0f, 0f, 0f);

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(IMapDataProvider mapData, IPlayerDataProvider playerData, Vector2Int playerStartTile)
    {
        ValidateReferences(mapData, playerData);

        _mapData = mapData;
        _playerData = playerData;
        _fogTile = GetFogTileFromTilemap();

        // Объекты сортируются в диапазоне 100–1000 — туман должен быть поверх любого из них
        if (fogTilemap.TryGetComponent(out TilemapRenderer fogRenderer))
            fogRenderer.sortingOrder = DepthSorting.OverlayOrder;

        InitializeFogMap();

        PlayerEventBus.OnPlayerMoved += HandlePlayerMoved;

        RevealArea(playerStartTile);
        _lastPlayerTile = playerStartTile;
    }

    private void OnDestroy()
    {
        PlayerEventBus.OnPlayerMoved -= HandlePlayerMoved;
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences(IMapDataProvider mapData, IPlayerDataProvider playerData)
    {
        Debug.Assert(fogTilemap != null, "FogOfWarSystem: fogTilemap is not assigned");
        Debug.Assert(mapData != null, "FogOfWarSystem: mapData is not assigned");
        Debug.Assert(playerData != null, "FogOfWarSystem: playerData is not assigned");
    }

    // ─── Setup ────────────────────────────────────────────────

    private void InitializeFogMap()
    {
        int width = _mapData.MapWidth;
        int height = _mapData.MapHeight;

        _fogMap = new FogState[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _fogMap[x, y] = FogState.Unexplored;
    }

    private TileBase GetFogTileFromTilemap()
    {
        foreach (Vector3Int pos in fogTilemap.cellBounds.allPositionsWithin)
        {
            TileBase tile = fogTilemap.GetTile(pos);
            if (tile != null) return tile;
        }

        Debug.LogWarning("FogOfWarSystem: no fog tile found on Tilemap_Fog");
        return null;
    }

    // ─── Observer ─────────────────────────────────────────────

    private void HandlePlayerMoved(Vector2Int newTile)
    {
        SetAreaState(_lastPlayerTile, FogState.Explored);
        RevealArea(newTile);
        _lastPlayerTile = newTile;
    }

    // ─── Fog Logic ────────────────────────────────────────────

    private void RevealArea(Vector2Int centerTile)
    {
        int radius = _playerData.VisionRadius;

        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                int tileX = centerTile.x + x;
                int tileY = centerTile.y + y;

                if (!IsInBounds(tileX, tileY)) continue;
                if (IsHiddenByHedge(tileX, tileY, centerTile)) continue;
                SetTileState(tileX, tileY, FogState.Visible);
            }
    }

    private void SetAreaState(Vector2Int centerTile, FogState state)
    {
        int radius = _playerData.VisionRadius;

        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                int tileX = centerTile.x + x;
                int tileY = centerTile.y + y;

                if (!IsInBounds(tileX, tileY)) continue;
                if (_fogMap[tileX, tileY] != FogState.Visible) continue;

                SetTileState(tileX, tileY, state);
            }
    }

    private void SetTileState(int x, int y, FogState state)
    {
        _fogMap[x, y] = state;
        UpdateTileVisual(x, y, state);
    }

    private void UpdateTileVisual(int x, int y, FogState state)
    {
        Vector3Int pos = new Vector3Int(x, y, 0);

        switch (state)
        {
            case FogState.Visible:
                fogTilemap.SetTile(pos, null);
                break;

            case FogState.Explored:
                fogTilemap.SetTile(pos, _fogTile);
                fogTilemap.SetTileFlags(pos, TileFlags.None);
                fogTilemap.SetColor(pos, ColorExplored);
                break;

            case FogState.Unexplored:
                fogTilemap.SetTile(pos, _fogTile);
                fogTilemap.SetTileFlags(pos, TileFlags.None);
                fogTilemap.SetColor(pos, ColorUnexplored);
                break;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < _mapData.MapWidth
            && y >= 0 && y < _mapData.MapHeight;
    }

    /// <summary>
    /// Тайл внутри «изгороди» и дальше её радиуса от игрока (по Чебышёву) — обзор его не открывает.
    /// </summary>
    private bool IsHiddenByHedge(int x, int y, Vector2Int playerTile)
    {
        foreach (HedgeArea hedge in _hedges)
        {
            if (!hedge.Tiles.Contains(new Vector2Int(x, y))) continue;

            int distance = Mathf.Max(Mathf.Abs(x - playerTile.x), Mathf.Abs(y - playerTile.y));
            if (distance > hedge.Radius) return true;
        }
        return false;
    }

    // ─── Public API ───────────────────────────────────────────

    public FogState GetFogState(int x, int y)
    {
        if (!IsInBounds(x, y)) return FogState.Unexplored;
        return _fogMap[x, y];
    }

    public bool IsVisible(int x, int y) => GetFogState(x, y) == FogState.Visible;

    /// <summary>
    /// Открывает прямоугольник тайлов (хаб — площадка с сеткой) с полями margin.
    /// Состояние Visible сохраняется, пока на него не наступит уход игрока из радиуса обзора.
    /// </summary>
    public void RevealRect(RectInt tiles, int margin = 0)
    {
        for (int x = tiles.xMin - margin; x < tiles.xMax + margin; x++)
            for (int y = tiles.yMin - margin; y < tiles.yMax + margin; y++)
                if (IsInBounds(x, y))
                    SetTileState(x, y, FogState.Visible);
    }

    /// <summary>Тайл хоть раз был в поле зрения (Explored или Visible); за картой — false.</summary>
    public bool IsExplored(int x, int y) => GetFogState(x, y) != FogState.Unexplored;

    /// <summary>
    /// «Густая изгородь» (лабиринт): внутри tiles туман снимается только в radius тайлов от игрока, а не
    /// во всём радиусе обзора, — check() видит лишь соседние тайлы. Видимые сейчас тайлы области
    /// закрываются (Explored), если игрок не рядом.
    /// </summary>
    public void AddHedgeArea(RectInt tiles, int radius)
    {
        _hedges.Add(new HedgeArea(tiles, Mathf.Max(0, radius)));
        if (_fogMap == null) return;

        for (int x = tiles.xMin; x < tiles.xMax; x++)
            for (int y = tiles.yMin; y < tiles.yMax; y++)
                if (IsInBounds(x, y) && _fogMap[x, y] == FogState.Visible && IsHiddenByHedge(x, y, _lastPlayerTile))
                    SetTileState(x, y, FogState.Explored);
    }

    private readonly struct HedgeArea
    {
        public readonly RectInt Tiles;
        public readonly int Radius;

        public HedgeArea(RectInt tiles, int radius)
        {
            Tiles = tiles;
            Radius = radius;
        }
    }
}