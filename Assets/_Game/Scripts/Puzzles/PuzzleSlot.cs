using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Зарезервированное место головоломки (T24): тип, тайлы области, опорная позиция, вид задачи у обелиска,
/// seed раскладки (лабиринт / плиты / вход задач обелиска). Id выдаётся при резерве и сохраняется при
/// постройке и переносе, поэтому id головоломок забега уникальны.
/// </summary>
public sealed class PuzzleSlot
{
    /// <summary>Id головоломки за забег (с 1).</summary>
    public int Id { get; }
    /// <summary>Тип — id события на карте (<see cref="PuzzleTypes"/>).</summary>
    public string TypeId { get; }
    /// <summary>Тайлы области без поля (лабиринт 7×7, плиты 5×5, обелиск — тайлы под коллайдером).</summary>
    public RectInt Tiles { get; }
    /// <summary>Опорная позиция на карте (обелиск — основание, лабиринт и плиты — центральный тайл).</summary>
    public Vector2 Position { get; }
    /// <summary>Прямоугольник на карте, как Bounds построенной головоломки.</summary>
    public Rect Bounds { get; }
    /// <summary>Вид задачи (только обелиск).</summary>
    public CodeTaskKind Kind { get; }
    /// <summary>Seed раскладки: MazeLayout / PlatesLayout, у обелиска — seed задач (с солью сцены).</summary>
    public int LayoutSeed { get; }
    /// <summary>
    /// Тайл стартовой платформы (T25): лабиринт — вход (внутри области), плиты и обелиск — тайл вплотную к
    /// области со стороны спавна (в поле суши, которое резерв уже требует).
    /// </summary>
    public Vector2Int StartTile { get; }

    /// <summary>Занятая область для проверок расстояния (без площади — тайл позиции).</summary>
    public Rect OccupiedRect =>
        Bounds.width > 0f && Bounds.height > 0f ? Bounds : new Rect(Position.x - 0.5f, Position.y - 0.5f, 1f, 1f);

    public PuzzleSlot(int id, string typeId, RectInt tiles, Vector2 position, Rect bounds, CodeTaskKind kind,
        int layoutSeed, Vector2Int startTile)
    {
        Id = id;
        TypeId = typeId;
        Tiles = tiles;
        Position = position;
        Bounds = bounds;
        Kind = kind;
        LayoutSeed = layoutSeed;
        StartTile = startTile;
    }
}

/// <summary>
/// Места головоломок забега (T24): все зарезервированные при старте, из них — ждущие (тип ещё не открыт).
/// Создаётся <see cref="PuzzleGenerator.Generate"/>; ждущие строит <see cref="PuzzleSpawner"/>, когда событие
/// открывается посреди забега. Постройка — тот же код, что при старте.
/// </summary>
public sealed class PuzzlePlan
{
    private readonly PuzzleGenerator.Context _context;
    private readonly Transform _container;
    private readonly PuzzleManager _manager;
    private readonly int _salt;
    private readonly List<PuzzleSlot> _slots = new List<PuzzleSlot>();
    private readonly List<PuzzleSlot> _pending = new List<PuzzleSlot>();

    /// <summary>Все места в порядке резерва (лабиринты, плиты, обелиски), построенные и ждущие.</summary>
    public IReadOnlyList<PuzzleSlot> Slots => _slots;
    /// <summary>Места, ещё не построенные (тип закрыт или постройка не удалась).</summary>
    public IReadOnlyList<PuzzleSlot> Pending => _pending;

    internal PuzzlePlan(PuzzleGenerator.Context context, Transform container, PuzzleManager manager, int salt)
    {
        _context = context;
        _container = container;
        _manager = manager;
        _salt = salt;
    }

    internal void AddSlot(PuzzleSlot slot)
    {
        _slots.Add(slot);
        _pending.Add(slot);
    }

    /// <summary>Ждущие места типа (копия — можно строить по ходу обхода).</summary>
    public List<PuzzleSlot> GetPending(string typeId)
    {
        var result = new List<PuzzleSlot>();
        foreach (PuzzleSlot slot in _pending)
            if (string.Equals(slot.TypeId, typeId, StringComparison.Ordinal))
                result.Add(slot);
        return result;
    }

    /// <summary>Место ещё ни разу не открыто (вся область с полем) и areaBlocked (враги) его не запрещает.</summary>
    public bool IsStillHidden(PuzzleSlot slot, Func<RectInt, bool> areaBlocked) =>
        PuzzleGenerator.IsStillHidden(_context, slot, areaBlocked);

    /// <summary>
    /// Переносит ждущее место туда, где область ни разу не открыта и areaBlocked — false (правила старта).
    /// Возвращает новый слот (в списке ожидания заменяет старый) или null — места нет, старый остаётся.
    /// </summary>
    public PuzzleSlot Relocate(PuzzleSlot slot, Func<RectInt, bool> areaBlocked)
    {
        int index = _pending.IndexOf(slot);
        if (index < 0) return null;

        PuzzleSlot moved = PuzzleGenerator.Relocate(_context, slot, areaBlocked);
        if (moved == null) return null;

        _pending[index] = moved;
        int slotIndex = _slots.IndexOf(slot);
        if (slotIndex >= 0) _slots[slotIndex] = moved;
        return moved;
    }

    /// <summary>
    /// Строит ждущее место (общий путь старта и посреди забега) и регистрирует в PuzzleManager.
    /// Physics2D.SyncTransforms — на вызывающем (один раз после пачки построек). Null — не построено.
    /// </summary>
    public PuzzleStructure Build(PuzzleSlot slot)
    {
        if (slot == null || !_pending.Contains(slot)) return null;

        PuzzleStructure puzzle = PuzzleGenerator.Build(_context, slot, _container, _salt);
        if (puzzle == null) return null;

        _pending.Remove(slot);
        _manager.Register(puzzle);
        return puzzle;
    }
}
