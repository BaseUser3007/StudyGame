using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Итог попытки решить задачу обелиска.</summary>
public enum PuzzleSolveResult
{
    /// <summary>Ответ верный: решено, награда выдана, PuzzleSolved.</summary>
    Correct,
    /// <summary>Ответ неверный (попытки не ограничены).</summary>
    Wrong,
    /// <summary>Уже решена в этом забеге.</summary>
    AlreadySolved,
    /// <summary>Игрок дальше радиуса взаимодействия.</summary>
    TooFar,
    /// <summary>Это не обелиск с задачей (или null).</summary>
    NotCodeTask
}

/// <summary>
/// Головоломки забега (этап 6): список, запросы по id / тайлу / радиусу, решение задач на код и
/// проверка лабиринта (игрок встал на центр — PlayerEventBus.OnPlayerMoved). Plain-класс, создаётся в Level
/// (в лагере — пустой), заполняется PuzzleGenerator. Dispose — отписка от статической шины.
/// Решение: MarkSolved → награда (PuzzleRewards) → GameEventBus PuzzleSolved (Id = тип, Tag = вид задачи)
/// → уведомление → OnPuzzleSolved.
/// </summary>
public class PuzzleManager : IDisposable
{
    /// <summary>Головоломка решена (после награды и события).</summary>
    public event Action<PuzzleStructure> OnPuzzleSolved;

    private readonly Func<Vector2> _playerPosition;
    private readonly PuzzleRewards _rewards;
    private readonly List<PuzzleStructure> _puzzles = new List<PuzzleStructure>();
    private readonly Dictionary<int, PuzzleStructure> _byId = new Dictionary<int, PuzzleStructure>();
    private bool _disposed;

    /// <summary>Все головоломки забега (решённые тоже).</summary>
    public IReadOnlyList<PuzzleStructure> Puzzles => _puzzles;
    /// <summary>Радиус, в котором работают puzzle()/solve() (до края коллайдера обелиска).</summary>
    public float InteractionRadius { get; }

    /// <param name="playerPosition">Свободная позиция игрока на карте (PlayerCommandExecutor.Position).</param>
    /// <param name="interactionRadius">PlayerConfig.structureInteractionRadius (1,5 тайла).</param>
    public PuzzleManager(Func<Vector2> playerPosition, float interactionRadius, PuzzleRewards rewards)
    {
        _playerPosition = playerPosition;
        InteractionRadius = interactionRadius;
        _rewards = rewards;
        PlayerEventBus.OnPlayerMoved += HandlePlayerMoved;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PlayerEventBus.OnPlayerMoved -= HandlePlayerMoved;
    }

    /// <summary>Добавить созданную головоломку (генератор). Id должен быть уникальным.</summary>
    public void Register(PuzzleStructure puzzle)
    {
        if (puzzle == null || _byId.ContainsKey(puzzle.Id)) return;
        _puzzles.Add(puzzle);
        _byId[puzzle.Id] = puzzle;
    }

    // ─── Queries ──────────────────────────────────────────────

    public PuzzleStructure GetById(int id) => _byId.TryGetValue(id, out PuzzleStructure puzzle) ? puzzle : null;

    /// <summary>Головоломка, чья область заходит на тайл (обелиск — по коллайдеру, лабиринт — 7×7), или null.</summary>
    public PuzzleStructure GetPuzzleAt(Vector2Int tile)
    {
        foreach (PuzzleStructure puzzle in _puzzles)
            if (puzzle != null && puzzle.ContainsTile(tile))
                return puzzle;
        return null;
    }

    /// <summary>Стена лабиринта на тайле (для check: terrain/структура «wall»).</summary>
    public bool IsMazeWall(Vector2Int tile)
    {
        foreach (PuzzleStructure puzzle in _puzzles)
            if (puzzle is MazePuzzle maze && maze.IsWall(tile))
                return true;
        return false;
    }

    /// <summary>
    /// Головоломки не дальше radius от точки (до края области), ближние — первыми. typeId — фильтр (null — все).
    /// </summary>
    public List<PuzzleStructure> GetPuzzlesInRadius(Vector2 point, float radius, string typeId = null)
    {
        var result = new List<PuzzleStructure>();
        foreach (PuzzleStructure puzzle in _puzzles)
        {
            if (puzzle == null) continue;
            if (typeId != null && !string.Equals(puzzle.TypeId, typeId, StringComparison.OrdinalIgnoreCase)) continue;
            if (puzzle.DistanceTo(point) <= radius) result.Add(puzzle);
        }

        result.Sort((a, b) => a.DistanceTo(point).CompareTo(b.DistanceTo(point)));
        return result;
    }

    /// <summary>Игрок в радиусе взаимодействия с головоломкой (до края её области).</summary>
    public bool IsInReach(PuzzleStructure puzzle)
    {
        return puzzle != null && _playerPosition != null
            && puzzle.DistanceTo(_playerPosition()) <= InteractionRadius;
    }

    // ─── code_task ────────────────────────────────────────────

    /// <summary>
    /// Ответ на задачу обелиска для текущего запуска кода. Энергию не списывает (это делает Lua-команда).
    /// </summary>
    public PuzzleSolveResult TrySolveCodeTask(PuzzleStructure puzzle, double answer)
    {
        if (!(puzzle is CodeTaskPuzzle obelisk)) return PuzzleSolveResult.NotCodeTask;
        if (obelisk.IsSolved) return PuzzleSolveResult.AlreadySolved;
        if (!IsInReach(obelisk)) return PuzzleSolveResult.TooFar;
        if (!obelisk.CheckAnswer(answer)) return PuzzleSolveResult.Wrong;

        Solve(obelisk, CodeTask.ToId(obelisk.Kind), () => _rewards?.ApplyCodeTask(obelisk.Kind));
        return PuzzleSolveResult.Correct;
    }

    /// <summary>Сообщение об ошибке для Lua (англ.); для Correct — null.</summary>
    public static string Describe(PuzzleSolveResult result)
    {
        switch (result)
        {
            case PuzzleSolveResult.Correct: return null;
            case PuzzleSolveResult.Wrong: return "wrong answer";
            case PuzzleSolveResult.AlreadySolved: return "puzzle is already solved";
            case PuzzleSolveResult.TooFar: return "too far from the puzzle";
            default: return "not a code puzzle";
        }
    }

    // ─── maze ─────────────────────────────────────────────────

    private void HandlePlayerMoved(Vector2Int tile)
    {
        foreach (PuzzleStructure puzzle in _puzzles)
        {
            if (!(puzzle is MazePuzzle maze) || maze.IsSolved || maze.Layout == null) continue;
            if (maze.CenterTile != tile) continue;

            Vector2Int center = maze.CenterTile;
            Solve(maze, null, () => _rewards?.ApplyMaze(new Vector2(center.x, center.y)));
            break;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void Solve(PuzzleStructure puzzle, string tag, Func<string> applyReward)
    {
        if (!puzzle.MarkSolved()) return;

        string reward = applyReward?.Invoke();
        GameEventBus.Publish(GameEventType.PuzzleSolved, puzzle.TypeId, 1, tag);

        string title = puzzle.TypeId == PuzzleTypes.Maze ? "Maze solved!" : "Puzzle solved!";
        UIEventBus.PublishNotification(string.IsNullOrEmpty(reward) ? title : $"{title} {reward}", LogEntryType.Success);
        OnPuzzleSolved?.Invoke(puzzle);
    }
}
