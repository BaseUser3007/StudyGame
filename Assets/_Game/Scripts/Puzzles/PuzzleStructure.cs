using UnityEngine;

/// <summary>
/// Состояние головоломки (T25): Waiting (не начата) → Armed (игрок встал на стартовую платформу, ждёт
/// запуск-попытку) → Attempt (идёт запуск-попытка) → Solved или Failed (исчезла).
/// </summary>
public enum PuzzleState
{
    Waiting,
    Armed,
    Attempt,
    Solved,
    Failed
}

/// <summary>
/// Головоломка на карте (этап 6): тип (id из <see cref="PuzzleTypes"/>), позиция, состояние на этот забег
/// (<see cref="PuzzleState"/>) и стартовая платформа (T25). Решением, попыткой и наградой управляет
/// <see cref="PuzzleManager"/>.
/// Позиция — свободная, в тайлах (как у ResourceObject); область — тайлы, которые головоломка занимает.
/// </summary>
public abstract class PuzzleStructure : MonoBehaviour
{
    /// <summary>Id за забег (с 1), для Lua (findObjects → obj.id → puzzle(obj)).</summary>
    public int Id { get; private set; }
    /// <summary>Тип — id события на карте ("code_task" / "maze" / "plates").</summary>
    public abstract string TypeId { get; }
    /// <summary>Опорная позиция на карте (обелиск — основание, лабиринт — центральный тайл).</summary>
    public Vector2 Position { get; private set; }
    public Vector2Int Tile => MapPosition.ToTile(Position);

    /// <summary>Состояние на этот забег (меняет только PuzzleManager).</summary>
    public PuzzleState State { get; private set; } = PuzzleState.Waiting;
    public bool IsSolved => State == PuzzleState.Solved;

    /// <summary>Есть ли стартовая платформа (у головоломок генератора — всегда; в тестах может не быть).</summary>
    public bool HasStartTile { get; private set; }
    /// <summary>
    /// Тайл стартовой платформы: лабиринт — вход, плиты и обелиск — тайл вплотную к области со стороны подхода.
    /// Встал на него (PlayerEventBus.OnPlayerMoved) — головоломка начинается.
    /// </summary>
    public Vector2Int StartTile { get; private set; }

    /// <summary>Прямоугольник на карте (в тайловых координатах), который головоломка занимает.</summary>
    public abstract Rect Bounds { get; }

    // Вид платформы (отдельный объект, не дочерний: у обелиска DepthSortedSprite перекрасил бы его порядок)
    private GameObject _startPlatformView;

    /// <summary>Вызывает генератор сразу после создания.</summary>
    public void Setup(int id, Vector2 position)
    {
        Id = id;
        Position = position;
    }

    /// <summary>Стартовая платформа: тайл и её вид (может быть null). Вид уничтожается вместе с головоломкой.</summary>
    public void SetStartPlatform(Vector2Int tile, GameObject view)
    {
        StartTile = tile;
        HasStartTile = true;
        if (_startPlatformView != null && _startPlatformView != view) DestroySafely(_startPlatformView);
        _startPlatformView = view;
        RefreshStartPlatform();
    }

    /// <summary>Заходит ли головоломка на тайл (касание краем не считается; без площади — тайл позиции).</summary>
    public bool ContainsTile(Vector2Int tile)
    {
        Rect bounds = Bounds;
        if (bounds.width <= 0f || bounds.height <= 0f) return Tile == tile;

        return bounds.xMin < tile.x + 0.5f && bounds.xMax > tile.x - 0.5f
            && bounds.yMin < tile.y + 0.5f && bounds.yMax > tile.y - 0.5f;
    }

    /// <summary>Расстояние от точки карты до края области (0 — внутри).</summary>
    public float DistanceTo(Vector2 point)
    {
        Rect bounds = Bounds;
        Vector2 closest = new Vector2(
            Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
            Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
        return Vector2.Distance(point, closest);
    }

    /// <summary>Отметить решённой (один раз). Возвращает false, если уже решена или провалена.</summary>
    public bool MarkSolved()
    {
        if (State == PuzzleState.Solved || State == PuzzleState.Failed) return false;
        State = PuzzleState.Solved;
        RefreshStartPlatform();
        OnSolved();
        return true;
    }

    // ─── Попытка (T25, вызывает PuzzleManager) ────────────────

    /// <summary>Начата: Waiting → Armed.</summary>
    internal bool Arm()
    {
        if (State != PuzzleState.Waiting) return false;
        State = PuzzleState.Armed;
        RefreshStartPlatform();
        return true;
    }

    /// <summary>Запуск-попытка начался: Armed → Attempt.</summary>
    internal bool BeginAttempt()
    {
        if (State != PuzzleState.Armed) return false;
        State = PuzzleState.Attempt;
        return true;
    }

    /// <summary>Попытка не удалась (до уничтожения объекта).</summary>
    internal void MarkFailed()
    {
        if (State == PuzzleState.Solved) return;
        State = PuzzleState.Failed;
    }

    /// <summary>
    /// Убрать головоломку с карты: своя очистка (OnRemoved — изгородь тумана у лабиринта), платформа, объект.
    /// </summary>
    internal void Teardown()
    {
        OnRemoved();
        if (_startPlatformView != null) DestroySafely(_startPlatformView);
        _startPlatformView = null;
        DestroySafely(gameObject);
    }

    // ─── Overridables ─────────────────────────────────────────

    /// <summary>Визуальная реакция на решение.</summary>
    protected virtual void OnSolved() { }

    /// <summary>Перед уничтожением (провал): снять то, что головоломка добавила вне своих объектов.</summary>
    protected virtual void OnRemoved() { }

    // ─── Helpers ──────────────────────────────────────────────

    // Платформа видна, пока головоломку можно начать или она идёт; решённой — скрыта
    private void RefreshStartPlatform()
    {
        if (_startPlatformView != null)
            _startPlatformView.SetActive(State != PuzzleState.Solved && State != PuzzleState.Failed);
    }

    // В EditMode-тестах Destroy недоступен
    private static void DestroySafely(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
