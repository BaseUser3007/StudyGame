using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class PlayerCommandExecutor : MonoBehaviour
{
    private IMapDataProvider _mapData;
    private PlayerConfig _playerConfig;
    private Vector2Int _currentTile;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(IMapDataProvider mapData, PlayerConfig playerConfig, Vector2Int startTile)
    {
        ValidateReferences(mapData, playerConfig);

        _mapData = mapData;
        _playerConfig = playerConfig;
        _currentTile = startTile;
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences(IMapDataProvider mapData, PlayerConfig playerConfig)
    {
        Debug.Assert(mapData != null, "PlayerCommandExecutor: mapData is not assigned");
        Debug.Assert(playerConfig != null, "PlayerCommandExecutor: playerConfig is not assigned");
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Перемещает персонажа в указанном направлении на указанное количество шагов.
    /// Возвращает количество реально пройденных шагов.
    /// </summary>
    public async UniTask<int> MoveAsync(
        Direction direction, int steps, CancellationToken ct = default)
    {
        int stepsTaken = 0;

        for (int i = 0; i < steps; i++)
        {
            Vector2Int nextTile = _currentTile + DirectionToVector(direction);

            if (!_mapData.IsWalkable(nextTile.x, nextTile.y))
                break;

            await MoveToTileAsync(nextTile, ct);
            stepsTaken++;
        }

        return stepsTaken;
    }

    /// <summary>
    /// Перемещает персонажа к целевому тайлу по прямой.
    /// Останавливается при встрече препятствия.
    /// Возвращает количество реально пройденных шагов.
    /// </summary>
    public async UniTask<int> MoveToAsync(
        Vector2Int targetTile, CancellationToken ct = default)
    {
        int stepsTaken = 0;

        while (_currentTile != targetTile)
        {
            Vector2Int nextTile = StepTowards(_currentTile, targetTile);

            if (!_mapData.IsWalkable(nextTile.x, nextTile.y))
                break;

            await MoveToTileAsync(nextTile, ct);
            stepsTaken++;
        }

        return stepsTaken;
    }

    // ─── Private Movement ─────────────────────────────────────

    private async UniTask MoveToTileAsync(Vector2Int targetTile, CancellationToken ct)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = _mapData.TileIndexToWorld(targetTile.x, targetTile.y);

        float elapsed = 0f;
        float duration = _playerConfig.moveSpeed;

        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, endPos, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Шаг прерван — возвращаем персонажа на текущий тайл
            transform.position = startPos;
            throw;
        }

        transform.position = endPos;
        _currentTile = targetTile;

        PlayerEventBus.PublishPlayerMoved(_currentTile);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private Vector2Int DirectionToVector(Direction direction)
    {
        return direction switch
        {
            Direction.North => new Vector2Int(0, 1),
            Direction.South => new Vector2Int(0, -1),
            Direction.East => new Vector2Int(1, 0),
            Direction.West => new Vector2Int(-1, 0),
            Direction.NorthEast => new Vector2Int(1, 1),
            Direction.NorthWest => new Vector2Int(-1, 1),
            Direction.SouthEast => new Vector2Int(1, -1),
            Direction.SouthWest => new Vector2Int(-1, -1),
            _ => Vector2Int.zero
        };
    }

    private Vector2Int StepTowards(Vector2Int from, Vector2Int to)
    {
        // System.Math.Sign, а не Mathf.Sign: Mathf.Sign(0) == 1, из-за чего путь шёл зигзагом
        int x = from.x + System.Math.Sign(to.x - from.x);
        int y = from.y + System.Math.Sign(to.y - from.y);
        return new Vector2Int(x, y);
    }

    // ─── Public Data ──────────────────────────────────────────

    public Vector2Int CurrentTile => _currentTile;
    public int VisionRadius => _playerConfig.visionRadius;
}

public enum Direction
{
    North, South, East, West,
    NorthEast, NorthWest,
    SouthEast, SouthWest
}