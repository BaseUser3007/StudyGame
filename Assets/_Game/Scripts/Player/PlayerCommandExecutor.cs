using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class PlayerCommandExecutor : MonoBehaviour
{
    private IMapDataProvider _mapData;
    private PlayerConfig _playerConfig;
    private Vector2Int _currentTile;

    private Collider2D _bodyCollider;

    // Зазор, на котором персонаж останавливается перед коллайдером (в тайлах).
    // Больше contact offset физики — иначе следующий Cast начинается «в контакте» и движение вбок блокируется
    private const float CollisionSkin = 0.06f;
    // Считаем, что персонаж дошёл до цели
    private const float ArriveDistance = 0.01f;
    // Приближение к цели меньше этого не считается прогрессом (защита от дрожания и кружения)
    private const float ProgressEpsilon = 0.05f;
    // Сколько раз за кадр движение может «переломиться» о препятствия при скольжении
    private const int SlideIterations = 3;

    // Буфер физического запроса — без аллокаций на каждый шаг
    private readonly List<RaycastHit2D> _hits = new List<RaycastHit2D>();

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(IMapDataProvider mapData, PlayerConfig playerConfig, Vector2Int startTile)
    {
        ValidateReferences(mapData, playerConfig);

        _mapData = mapData;
        _playerConfig = playerConfig;
        _currentTile = startTile;

        _bodyCollider = FindBodyCollider();
        if (_bodyCollider == null)
            Debug.LogWarning("PlayerCommandExecutor: на персонаже нет Collider2D — коллизии с объектами не проверяются", this);
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences(IMapDataProvider mapData, PlayerConfig playerConfig)
    {
        Debug.Assert(mapData != null, "PlayerCommandExecutor: mapData is not assigned");
        Debug.Assert(playerConfig != null, "PlayerCommandExecutor: playerConfig is not assigned");
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Перемещает персонажа в указанном направлении на указанное количество тайлов.
    /// Вода останавливает сразу, объекты персонаж обходит (см. MoveTowardsAsync).
    /// Возвращает, сколько раз персонаж перешёл в новый тайл.
    /// </summary>
    public UniTask<int> MoveAsync(
        Direction direction, int steps, CancellationToken ct = default)
    {
        Vector2Int step = DirectionToVector(direction);
        Vector2Int target = _currentTile;

        // Идём до последнего тайла суши в этом направлении
        for (int i = 0; i < steps; i++)
        {
            Vector2Int next = target + step;
            if (!_mapData.IsWalkable(next.x, next.y)) break;
            target = next;
        }

        return MoveTowardsAsync(target, ct);
    }

    /// <summary>
    /// Перемещает персонажа к целевому тайлу по прямой.
    /// Вода останавливает сразу, объекты персонаж обходит (см. MoveTowardsAsync).
    /// Возвращает, сколько раз персонаж перешёл в новый тайл.
    /// stopWhen — необязательное условие досрочной остановки (например, «объект уже в досягаемости»),
    /// проверяется перед каждым шагом.
    /// </summary>
    public UniTask<int> MoveToAsync(
        Vector2Int targetTile, CancellationToken ct = default, System.Func<bool> stopWhen = null)
    {
        Vector2Int target = _currentTile;

        // Идём до последнего тайла суши на прямой к цели
        while (target != targetTile)
        {
            Vector2Int next = StepTowards(target, targetTile);
            if (!_mapData.IsWalkable(next.x, next.y)) break;
            target = next;
        }

        return MoveTowardsAsync(target, ct, stopWhen);
    }

    // ─── Private Movement ─────────────────────────────────────

    /// <summary>
    /// Непрерывное движение к центру тайла с постоянной скоростью.
    /// Упёршись в коллайдер, персонаж останавливается перед ним и скользит вдоль поверхности —
    /// так круглые объекты обходятся. Если stuckTimeout секунд персонаж не приближается к цели
    /// (застрял или ходит кругами), движение завершается.
    /// </summary>
    private async UniTask<int> MoveTowardsAsync(
        Vector2Int targetTile, CancellationToken ct, System.Func<bool> stopWhen = null)
    {
        Vector2 target = _mapData.TileIndexToWorld(targetTile.x, targetTile.y);
        // moveSpeed — секунды на один тайл
        float speed = 1f / Mathf.Max(_playerConfig.moveSpeed, 0.0001f);

        int tilesChanged = 0;
        float bestDistance = Vector2.Distance(transform.position, target);
        float stuckTime = 0f;

        try
        {
            while (true)
            {
                if (stopWhen != null && stopWhen()) break;

                Vector2 position = transform.position;
                Vector2 toTarget = target - position;
                float distance = toTarget.magnitude;

                if (distance <= ArriveDistance)
                {
                    SetPosition(target);
                    if (UpdateCurrentTile()) tilesChanged++;
                    break;
                }

                Vector2 move = toTarget / distance * Mathf.Min(speed * Time.deltaTime, distance);
                SetPosition(MoveWithSlide(position, move, toTarget));
                if (UpdateCurrentTile()) tilesChanged++;

                // Прогресс — только реальное приближение к цели
                float newDistance = Vector2.Distance(transform.position, target);
                if (newDistance < bestDistance - ProgressEpsilon)
                {
                    bestDistance = newDistance;
                    stuckTime = 0f;
                }
                else
                {
                    stuckTime += Time.deltaTime;
                    if (stuckTime >= _playerConfig.stuckTimeout) break;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Остановка — персонаж остаётся там, где был; тайл уже актуален
            UpdateCurrentTile();
            throw;
        }

        return tilesChanged;
    }

    /// <summary>
    /// Сдвиг на move с учётом коллайдеров и воды. Упёршись, остаток движения
    /// проецируется на касательную к препятствию (скольжение).
    /// </summary>
    private Vector2 MoveWithSlide(Vector2 position, Vector2 move, Vector2 toTarget)
    {
        for (int i = 0; i < SlideIterations && move.sqrMagnitude > 1e-8f; i++)
        {
            float length = move.magnitude;
            Vector2 direction = move / length;

            if (!TryCast(position, direction, length, out RaycastHit2D hit))
            {
                position = ClampToLand(position, position + move);
                break;
            }

            // Доходим до препятствия, оставляя зазор
            float travel = Mathf.Max(0f, hit.distance - CollisionSkin);
            position = ClampToLand(position, position + direction * travel);

            // Остаток — вдоль поверхности
            float remaining = length - travel;
            Vector2 tangent = new Vector2(-hit.normal.y, hit.normal.x);
            float along = Vector2.Dot(direction * remaining, tangent);

            // Упёрлись строго в лоб — обходим с той стороны, куда хоть немного смещена цель
            if (Mathf.Abs(along) < remaining * 0.05f)
            {
                float side = Vector2.Dot(toTarget, tangent);
                along = remaining * (side >= 0f ? 1f : -1f);
            }

            move = tangent * along;
        }

        return position;
    }

    /// <summary>
    /// Не даём уйти в воду: если новая точка не на суше, пробуем сдвиг по каждой оси отдельно.
    /// </summary>
    private Vector2 ClampToLand(Vector2 from, Vector2 to)
    {
        if (IsLand(to)) return to;

        Vector2 alongX = new Vector2(to.x, from.y);
        if (IsLand(alongX)) return alongX;

        Vector2 alongY = new Vector2(from.x, to.y);
        if (IsLand(alongY)) return alongY;

        return from;
    }

    private bool IsLand(Vector2 worldPosition)
    {
        Vector2Int tile = _mapData.WorldToTileIndex(worldPosition);
        return _mapData.IsWalkable(tile.x, tile.y);
    }

    private void SetPosition(Vector2 position)
    {
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    /// <summary>
    /// Логический тайл — тот, где персонаж реально стоит. Возвращает true, если тайл сменился.
    /// </summary>
    private bool UpdateCurrentTile()
    {
        Vector2Int tile = _mapData.WorldToTileIndex(transform.position);
        if (tile == _currentTile) return false;

        _currentTile = tile;
        PlayerEventBus.PublishPlayerMoved(_currentTile);
        return true;
    }

    // ─── Collision ────────────────────────────────────────────

    /// <summary>
    /// «Протаскивает» коллайдер персонажа (его реальную форму у ног) из position вдоль direction.
    /// Возвращает ближайшее препятствие, в сторону которого идёт движение.
    /// </summary>
    private bool TryCast(Vector2 position, Vector2 direction, float distance, out RaycastHit2D nearest)
    {
        nearest = default;
        if (_bodyCollider == null) return false;

        // Cast считает от текущей позиции коллайдера; физика синхронизирует её с transform только в FixedUpdate
        SetPosition(position);
        Physics2D.SyncTransforms();

        // Все слои, триггеры игнорируются; свои коллайдеры Cast пропускает сам
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        int count = _bodyCollider.Cast(direction, filter, _hits, distance + CollisionSkin, true);

        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;

            // Препятствие сбоку или позади — не мешает (так можно отойти от того, во что упёрлись)
            if (Vector2.Dot(direction, hit.normal) >= 0f) continue;

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Коллайдер персонажа для проверки пути — первый не-триггер на нём или дочерних объектах.
    /// </summary>
    private Collider2D FindBodyCollider()
    {
        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>())
            if (!collider.isTrigger)
                return collider;
        return null;
    }

    // ─── Helpers ──────────────────────────────────────────────

    public static Vector2Int DirectionToVector(Direction direction)
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
