using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Итог попытки решить задачу обелиска.</summary>
public enum PuzzleSolveResult
{
    /// <summary>Ответ верный: решено, награда выдана, PuzzleSolved.</summary>
    Correct,
    /// <summary>Ответ неверный: следующая попытка у этого обелиска — только в следующем запуске кода.</summary>
    Wrong,
    /// <summary>Уже решена в этом забеге.</summary>
    AlreadySolved,
    /// <summary>Игрок дальше радиуса взаимодействия.</summary>
    TooFar,
    /// <summary>Это не обелиск с задачей (или null).</summary>
    NotCodeTask,
    /// <summary>В этом запуске обелиск уже отклонил ответ (против перебора solve).</summary>
    NoAttemptsLeft,
    /// <summary>Головоломка не начата или идёт не запуск-попытка (T25): сначала — стартовая платформа.</summary>
    NotStarted
}

/// <summary>
/// Карточка головоломки при старте (T25): название, что сделать, награда — на языке игры (Loc).
/// Правило попытки — <see cref="PuzzleManager.AttemptRuleText"/>.
/// </summary>
public readonly struct PuzzleBriefing
{
    public readonly string Title;
    public readonly string Description;
    public readonly string Reward;

    public PuzzleBriefing(string title, string description, string reward)
    {
        Title = title;
        Description = description;
        Reward = reward;
    }
}

/// <summary>
/// Головоломки забега (этап 6): список, запросы по id / тайлу / радиусу, решение задач на код и
/// проверка лабиринта и плит (шаги игрока — PlayerEventBus.OnPlayerMoved). Plain-класс, создаётся в Level
/// (в лагере — пустой), заполняется PuzzleGenerator. Dispose — отписка от статической шины и запуска.
/// Решение: MarkSolved → награда (PuzzleRewards) → GameEventBus PuzzleSolved (Id = тип, Tag = вид задачи)
/// → уведомление → OnPuzzleSolved.
/// Попытка (T25): игрок встал на стартовую платформу (Waiting, нет активной) → Armed, ActivePuzzle,
/// OnPuzzleStarted; если код выполняется — запуск прерывается (IPuzzleRunControl.InterruptForPuzzle).
/// Следующий запуск кода → Attempt; он закончился без решения → Failed: головоломка убирается (Remove),
/// уведомление, OnPuzzleFailed. Решить можно только в Attempt (плиты, центр лабиринта, solve()).
/// </summary>
public class PuzzleManager : IDisposable
{
    /// <summary>Головоломка начата (Armed; карточка — GetBriefing). Если код шёл, прерывание уже запрошено.</summary>
    public event Action<PuzzleStructure> OnPuzzleStarted;
    /// <summary>Головоломка решена (после награды и события).</summary>
    public event Action<PuzzleStructure> OnPuzzleSolved;
    /// <summary>
    /// Попытка не удалась: головоломка уже убрана из списков, объект уничтожается сразу после события
    /// (Id/TypeId/Position ещё читаются).
    /// </summary>
    public event Action<PuzzleStructure> OnPuzzleFailed;

    private readonly Func<Vector2> _playerPosition;
    private readonly PuzzleRewards _rewards;
    private readonly List<PuzzleStructure> _puzzles = new List<PuzzleStructure>();
    private readonly Dictionary<int, PuzzleStructure> _byId = new Dictionary<int, PuzzleStructure>();
    // Обелиск → запуск кода с неверным ответом: одна попытка за запуск (вход в новом запуске другой),
    // поэтому перебор solve растягивается на много запусков, а их всего несколько в день
    private readonly Dictionary<int, int> _failedRun = new Dictionary<int, int>();
    private IPuzzleRunControl _runControl;
    private bool _disposed;

    /// <summary>Все головоломки забега (решённые тоже; провалившиеся убираются).</summary>
    public IReadOnlyList<PuzzleStructure> Puzzles => _puzzles;
    /// <summary>Радиус, в котором работают puzzle()/solve() (до края коллайдера обелиска).</summary>
    public float InteractionRadius { get; }
    /// <summary>Начатая головоломка (Armed / Attempt) или null. Одновременно — только одна.</summary>
    public PuzzleStructure ActivePuzzle { get; private set; }

    /// <summary>Правило попытки для карточки и лога (Loc).</summary>
    public static string AttemptRuleText =>
        Loc.T("One attempt: the next code run. If you don't solve it, the puzzle disappears.");

    /// <param name="playerPosition">Свободная позиция игрока на карте (PlayerCommandExecutor.Position).</param>
    /// <param name="interactionRadius">PlayerConfig.structureInteractionRadius (1,5 тайла).</param>
    public PuzzleManager(Func<Vector2> playerPosition, float interactionRadius, PuzzleRewards rewards)
    {
        _playerPosition = playerPosition;
        InteractionRadius = interactionRadius;
        _rewards = rewards;
        PlayerEventBus.OnPlayerMoved += HandlePlayerMoved;
    }

    /// <summary>
    /// Запуски кода (T25): начало запуска переводит начатую головоломку в попытку, конец попытки без
    /// решения — провал; при старте посреди запуска он прерывается. Без вызова — головоломка начинается,
    /// но попытка не наступает (тесты без песочницы).
    /// </summary>
    public void AttachRunControl(IPuzzleRunControl runControl)
    {
        if (_disposed) return;
        if (_runControl != null) _runControl.OnRunningChanged -= HandleRunningChanged;
        _runControl = runControl;
        if (_runControl != null) _runControl.OnRunningChanged += HandleRunningChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PlayerEventBus.OnPlayerMoved -= HandlePlayerMoved;
        if (_runControl != null) _runControl.OnRunningChanged -= HandleRunningChanged;
        _runControl = null;
    }

    /// <summary>Добавить созданную головоломку (генератор). Id должен быть уникальным.</summary>
    public void Register(PuzzleStructure puzzle)
    {
        if (puzzle == null || _byId.ContainsKey(puzzle.Id)) return;
        _puzzles.Add(puzzle);
        _byId[puzzle.Id] = puzzle;
    }

    /// <summary>
    /// Убрать головоломку с карты (T25, провал): из списков, «изгородь» тумана лабиринта, объекты и платформа.
    /// </summary>
    public void Remove(PuzzleStructure puzzle)
    {
        if (!Unregister(puzzle)) return;
        puzzle.Teardown();
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

    /// <summary>
    /// Головоломка, чья стартовая платформа на тайле (решённые тоже — платформа у них скрыта), или null.
    /// </summary>
    public PuzzleStructure GetPuzzleByStartTile(Vector2Int tile)
    {
        foreach (PuzzleStructure puzzle in _puzzles)
            if (puzzle != null && puzzle.HasStartTile && puzzle.StartTile == tile)
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

    // ─── Попытка (T25) ────────────────────────────────────────

    /// <summary>
    /// Начать головоломку (как будто игрок встал на её платформу): только Waiting, зарегистрированную и
    /// когда нет другой начатой. Если код выполняется — запуск прерывается. false — не начата.
    /// </summary>
    public bool TryStart(PuzzleStructure puzzle)
    {
        if (puzzle == null || ActivePuzzle != null || GetById(puzzle.Id) != puzzle) return false;
        if (!puzzle.Arm()) return false;

        ActivePuzzle = puzzle;

        // Игрок двигается кодом — почти всегда это посреди запуска: прерываем его (попытка — следующий запуск)
        if (_runControl != null && _runControl.IsRunning)
        {
            try
            {
                _runControl.InterruptForPuzzle();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        OnPuzzleStarted?.Invoke(puzzle);
        return true;
    }

    /// <summary>Карточка головоломки (T25): название, что сделать и награда — на языке игры.</summary>
    public PuzzleBriefing GetBriefing(PuzzleStructure puzzle)
    {
        switch (puzzle)
        {
            case CodeTaskPuzzle obelisk:
                return new PuzzleBriefing(
                    Loc.T("Obelisk"),
                    DescribeCodeTask(obelisk.Kind),
                    _rewards != null ? _rewards.DescribeCodeTask(obelisk.Kind) : string.Empty);

            case MazePuzzle _:
                return new PuzzleBriefing(
                    Loc.T("Maze"),
                    Loc.T("Reach the chest in the centre of the maze. Inside the maze you only see the tiles next to you."),
                    _rewards != null ? _rewards.DescribeMaze() : string.Empty);

            case PlatesPuzzle _:
                return new PuzzleBriefing(
                    Loc.T("Pressure plates"),
                    Loc.T("Step on all plates in ascending order of their numbers. A number is visible only with check(x, y).plate; a wrong plate releases all of them."),
                    _rewards != null ? _rewards.DescribePlates() : string.Empty);

            default:
                return new PuzzleBriefing(
                    puzzle != null ? PuzzleTypes.GetDisplayName(puzzle.TypeId) : string.Empty, string.Empty, string.Empty);
        }
    }

    // ─── code_task ────────────────────────────────────────────

    /// <summary>
    /// Ответ на задачу обелиска для текущего запуска кода. Энергию не списывает (это делает Lua-команда).
    /// Принимается только в запуске-попытке (State == Attempt).
    /// </summary>
    public PuzzleSolveResult TrySolveCodeTask(PuzzleStructure puzzle, double answer)
    {
        if (!(puzzle is CodeTaskPuzzle obelisk)) return PuzzleSolveResult.NotCodeTask;
        if (obelisk.IsSolved) return PuzzleSolveResult.AlreadySolved;
        if (obelisk.State != PuzzleState.Attempt) return PuzzleSolveResult.NotStarted;
        if (!IsInReach(obelisk)) return PuzzleSolveResult.TooFar;

        int runId = GameEventBus.ActiveRunId;
        if (_failedRun.TryGetValue(obelisk.Id, out int failedRun) && failedRun == runId)
            return PuzzleSolveResult.NoAttemptsLeft;

        if (!obelisk.CheckAnswer(answer))
        {
            _failedRun[obelisk.Id] = runId;
            return PuzzleSolveResult.Wrong;
        }

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
            case PuzzleSolveResult.NoAttemptsLeft:
                return "the obelisk already rejected an answer this run — try again in the next run";
            case PuzzleSolveResult.AlreadySolved: return "puzzle is already solved";
            case PuzzleSolveResult.TooFar: return "too far from the puzzle";
            case PuzzleSolveResult.NotStarted: return "puzzle is not started — step on its start platform";
            default: return "not a code puzzle";
        }
    }

    // ─── Шаги игрока: платформы, лабиринт ─────────────────────

    private void HandlePlayerMoved(Vector2Int tile)
    {
        // Стартовая платформа: только ждущая головоломка и пока нет другой начатой
        if (ActivePuzzle == null)
        {
            PuzzleStructure byStart = GetPuzzleByStartTile(tile);
            if (byStart != null && byStart.State == PuzzleState.Waiting && TryStart(byStart)) return;
        }

        // По индексу, а не foreach: решение → достижение → открытие события → PuzzleSpawner добавляет
        // головоломку в _puzzles прямо во время обхода
        for (int i = 0; i < _puzzles.Count; i++)
        {
            PuzzleStructure puzzle = _puzzles[i];
            if (puzzle is PlatesPuzzle plates)
            {
                if (StepOnPlates(plates, tile)) break;
                continue;
            }

            if (!(puzzle is MazePuzzle maze) || maze.State != PuzzleState.Attempt || maze.Layout == null) continue;
            if (maze.CenterTile != tile) continue;

            Vector2Int center = maze.CenterTile;
            Solve(maze, null, () => _rewards?.ApplyMaze(new Vector2(center.x, center.y)));
            break;
        }
    }

    // ─── plates ───────────────────────────────────────────────

    /// <summary>
    /// Шаг игрока по площадке с плитами (вызывается и из тестов). true — тайл был плитой этой площадки.
    /// Неверная плита — уведомление о сбросе; последняя верная — решение и награда.
    /// До запуска-попытки (State != Attempt) плиты не нажимаются — false.
    /// </summary>
    public bool StepOnPlates(PlatesPuzzle plates, Vector2Int tile)
    {
        if (plates == null || plates.State != PuzzleState.Attempt) return false;

        PlateStepResult result = plates.Step(tile);
        switch (result)
        {
            case PlateStepResult.NotAPlate:
                return false;
            case PlateStepResult.Reset:
                UIEventBus.PublishNotification(Loc.T("Wrong plate — all plates are released."), LogEntryType.Warning);
                break;
            case PlateStepResult.Completed:
                Vector2Int center = plates.CenterTile;
                Solve(plates, null, () => _rewards?.ApplyPlates(new Vector2(center.x, center.y)));
                break;
        }
        return true;
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleRunningChanged(bool running)
    {
        PuzzleStructure active = ActivePuzzle;
        if (active == null) return;

        if (running)
        {
            // Первый запуск после старта — попытка (прерванный запуск закончился раньше и её не тратит)
            active.BeginAttempt();
            return;
        }

        if (active.State == PuzzleState.Attempt) Fail(active);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void Solve(PuzzleStructure puzzle, string tag, Func<string> applyReward)
    {
        if (!puzzle.MarkSolved()) return;
        if (ActivePuzzle == puzzle) ActivePuzzle = null;

        string reward = applyReward?.Invoke();
        GameEventBus.Publish(GameEventType.PuzzleSolved, puzzle.TypeId, 1, tag);

        string title = Loc.T(puzzle.TypeId == PuzzleTypes.Maze ? "Maze solved!" : "Puzzle solved!");
        UIEventBus.PublishNotification(string.IsNullOrEmpty(reward) ? title : $"{title} {reward}", LogEntryType.Success);
        OnPuzzleSolved?.Invoke(puzzle);
    }

    // Попытка не удалась: убрать из списков, сообщить, затем уничтожить объекты
    private void Fail(PuzzleStructure puzzle)
    {
        puzzle.MarkFailed();
        if (ActivePuzzle == puzzle) ActivePuzzle = null;
        Unregister(puzzle);

        UIEventBus.PublishNotification(
            Loc.F("Puzzle failed: {0} is gone.", GetBriefing(puzzle).Title), LogEntryType.Warning);
        try
        {
            OnPuzzleFailed?.Invoke(puzzle);
        }
        finally
        {
            puzzle.Teardown();
        }
    }

    private bool Unregister(PuzzleStructure puzzle)
    {
        if (puzzle == null || !_byId.TryGetValue(puzzle.Id, out PuzzleStructure registered) || registered != puzzle)
            return false;

        _byId.Remove(puzzle.Id);
        _puzzles.Remove(puzzle);
        _failedRun.Remove(puzzle.Id);
        if (ActivePuzzle == puzzle) ActivePuzzle = null;
        return true;
    }

    // Что сделать у обелиска — по виду задачи (вход у каждого запуска свой, его здесь нет)
    private static string DescribeCodeTask(CodeTaskKind kind)
    {
        string goal;
        switch (kind)
        {
            case CodeTaskKind.Max:
                goal = Loc.T("Find the largest number of the task input.");
                break;
            case CodeTaskKind.Count:
                goal = Loc.T("Count the numbers of the task input that are greater than its limit.");
                break;
            default:
                goal = Loc.T("Add up all numbers of the task input.");
                break;
        }

        return goal + " " + Loc.T("Read the task with puzzle(obelisk) — the input changes every run — and answer with solve(obelisk, answer) in the same run.");
    }
}
