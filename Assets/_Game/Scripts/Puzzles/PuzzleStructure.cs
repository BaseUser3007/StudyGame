using UnityEngine;

/// <summary>
/// Головоломка на карте (этап 6): тип (id из <see cref="PuzzleTypes"/>), позиция, отметка «решено»
/// на этот забег. Решением и наградой управляет <see cref="PuzzleManager"/>.
/// Позиция — свободная, в тайлах (как у ResourceObject); область — тайлы, которые головоломка занимает.
/// </summary>
public abstract class PuzzleStructure : MonoBehaviour
{
    /// <summary>Id за забег (с 1), для Lua (findObjects → obj.id → puzzle(obj)).</summary>
    public int Id { get; private set; }
    /// <summary>Тип — id события на карте ("code_task" / "maze").</summary>
    public abstract string TypeId { get; }
    /// <summary>Опорная позиция на карте (обелиск — основание, лабиринт — центральный тайл).</summary>
    public Vector2 Position { get; private set; }
    public Vector2Int Tile => MapPosition.ToTile(Position);
    public bool IsSolved { get; private set; }

    /// <summary>Прямоугольник на карте (в тайловых координатах), который головоломка занимает.</summary>
    public abstract Rect Bounds { get; }

    /// <summary>Вызывает генератор сразу после создания.</summary>
    public void Setup(int id, Vector2 position)
    {
        Id = id;
        Position = position;
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

    /// <summary>Отметить решённой (один раз). Возвращает false, если уже решена.</summary>
    public bool MarkSolved()
    {
        if (IsSolved) return false;
        IsSolved = true;
        OnSolved();
        return true;
    }

    /// <summary>Визуальная реакция на решение.</summary>
    protected virtual void OnSolved() { }
}
