using System;
using System.Collections.Generic;
using Lua;
using UnityEngine;

/// <summary>
/// Головоломки для Lua-команд (этап 6): имена типов, видимость под туманом, поиск головоломки по аргументам
/// (таблица из findObjects/check или точка) и тексты ошибок для harvest/mine/attack.
/// </summary>
public static class LuaPuzzles
{
    /// <summary>Тип в Lua для обелиска с задачей (PuzzleTypes.CodeTask).</summary>
    public const string ObeliskTypeName = "obelisk";
    /// <summary>Тип в Lua для лабиринта (PuzzleTypes.Maze).</summary>
    public const string MazeTypeName = "maze";
    /// <summary>Тип в Lua для площадки с плитами (PuzzleTypes.Plates).</summary>
    public const string PlatesTypeName = "plates";
    /// <summary>findObjects("puzzle") — все головоломки.</summary>
    public const string AnyTypeName = "puzzle";

    /// <summary>Имена в check: стена лабиринта и сундук в его центре.</summary>
    public const string WallName = "wall";
    public const string ChestName = "chest";
    /// <summary>Имя в check для тайла с плитой (номер — поле plate).</summary>
    public const string PlateName = "plate";
    /// <summary>Имя в check для стартовой платформы головоломки (T25): встал на неё — головоломка начинается.</summary>
    public const string StartPlatformName = "start_platform";

    /// <summary>Условие лабиринта для puzzle(maze).</summary>
    public const string MazeText = "Reach the center of the maze.";

    /// <summary>Условие плит для puzzle(plates).</summary>
    public const string PlatesText =
        "Step on the plates in ascending order of their numbers. The plates are in the area minX..maxX, minY..maxY; " +
        "a plate's number is check(x, y).plate. A wrong plate releases all of them. " +
        "Each row and each column has at most one plate.";

    /// <summary>solve() на плитах: их решают ногами.</summary>
    public const string PlatesSolveError = "plates are solved by stepping on them in ascending order of their numbers";

    // Насколько далеко от точки может быть край головоломки, если точка не попала в её область
    private const float PickTolerance = 0.5f;

    // ─── Типы ─────────────────────────────────────────────────

    /// <summary>Имя типа для Lua: "obelisk" / "maze".</summary>
    public static string GetTypeName(PuzzleStructure puzzle)
    {
        if (puzzle == null) return AnyTypeName;
        if (puzzle.TypeId == PuzzleTypes.CodeTask) return ObeliskTypeName;
        if (puzzle.TypeId == PuzzleTypes.Maze) return MazeTypeName;
        if (puzzle.TypeId == PuzzleTypes.Plates) return PlatesTypeName;
        return puzzle.TypeId;
    }

    /// <summary>
    /// Имя — тип головоломки для findObjects: "obelisk"/"code_task", "maze", "plates" или "puzzle" (typeId = null — все).
    /// </summary>
    public static bool TryParseType(string typeName, out string typeId)
    {
        typeId = null;
        if (string.IsNullOrEmpty(typeName)) return false;

        switch (typeName.ToLowerInvariant())
        {
            case AnyTypeName: return true;
            case ObeliskTypeName:
            case PuzzleTypes.CodeTask: typeId = PuzzleTypes.CodeTask; return true;
            case MazeTypeName: typeId = PuzzleTypes.Maze; return true;
            case PlatesTypeName: typeId = PuzzleTypes.Plates; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Состояние для Lua (поле state): "waiting" — не начата (встаньте на платформу), "armed" — начата, попытка —
    /// следующий запуск, "attempt" — идёт попытка, "solved", "failed" (исчезла — в списках её уже нет).
    /// </summary>
    public static string GetStateName(PuzzleState state)
    {
        switch (state)
        {
            case PuzzleState.Armed: return "armed";
            case PuzzleState.Attempt: return "attempt";
            case PuzzleState.Solved: return "solved";
            case PuzzleState.Failed: return "failed";
            default: return "waiting";
        }
    }

    /// <summary>Тайл стартовой платформы: startX, startY (нет платформы — полей нет).</summary>
    public static void AddStartTile(LuaTable table, PuzzleStructure puzzle)
    {
        if (puzzle == null || !puzzle.HasStartTile) return;
        table["startX"] = (double)puzzle.StartTile.x;
        table["startY"] = (double)puzzle.StartTile.y;
    }

    /// <summary>
    /// Головоломка, чья стартовая платформа на тайле, — пока платформа действует (не решена; у решённой она скрыта).
    /// </summary>
    public static PuzzleStructure GetByStartTile(PuzzleManager puzzles, Vector2Int tile)
    {
        PuzzleStructure puzzle = puzzles?.GetPuzzleByStartTile(tile);
        return puzzle != null && !puzzle.IsSolved && puzzle.State != PuzzleState.Failed ? puzzle : null;
    }

    /// <summary>Имена для подсказки в ошибке findObjects.</summary>
    public static string KnownTypeNames => $"{ObeliskTypeName}, {PuzzleTypes.CodeTask}, {MazeTypeName}, {PlatesTypeName}, {AnyTypeName}";

    // ─── Видимость ────────────────────────────────────────────

    /// <summary>
    /// Головоломка видна, если хотя бы один тайл её области без тумана (fog = null — видно всё).
    /// </summary>
    public static bool IsVisible(PuzzleStructure puzzle, IFogProvider fog)
    {
        if (puzzle == null) return false;
        if (fog == null) return true;

        Rect bounds = puzzle.Bounds;
        if (bounds.width <= 0f || bounds.height <= 0f)
            return fog.IsVisible(puzzle.Tile.x, puzzle.Tile.y);

        int minX = Mathf.FloorToInt(bounds.xMin + 0.5f);
        int maxX = Mathf.CeilToInt(bounds.xMax - 0.5f);
        int minY = Mathf.FloorToInt(bounds.yMin + 0.5f);
        int maxY = Mathf.CeilToInt(bounds.yMax - 0.5f);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                if (puzzle.ContainsTile(new Vector2Int(x, y)) && fog.IsVisible(x, y))
                    return true;
        return false;
    }

    /// <summary>
    /// Видимые головоломки типа typeId (null — все), ближние к точке — первыми.
    /// </summary>
    public static List<PuzzleStructure> GetVisible(PuzzleManager puzzles, IFogProvider fog, Vector2 point, string typeId)
    {
        var result = new List<PuzzleStructure>();
        if (puzzles == null) return result;

        foreach (PuzzleStructure puzzle in puzzles.GetPuzzlesInRadius(point, float.MaxValue, typeId))
            if (IsVisible(puzzle, fog))
                result.Add(puzzle);
        return result;
    }

    // ─── Аргументы ────────────────────────────────────────────

    /// <summary>
    /// Головоломка из аргумента: таблица головоломки (по id), иначе точка (таблица с x, y или x, y) —
    /// головоломка, чья область заходит на её тайл. null + причина, если не нашлась.
    /// usage — текст ошибки, если аргументы не подходят.
    /// </summary>
    public static PuzzleStructure Resolve(
        LuaFunctionExecutionContext context, PuzzleManager puzzles, string usage, out string error)
    {
        error = null;

        if (context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out LuaTable table)
            && LuaObjectList.TryGetPuzzleId(table, out int puzzleId))
        {
            PuzzleStructure byId = puzzles?.GetById(puzzleId);
            if (byId == null) error = "puzzle is gone";
            return byId;
        }

        if (context.ArgumentCount == 0 || !HarvestCommand.TryReadPoint(context, out Vector2 point))
        {
            error = usage;
            return null;
        }

        PuzzleStructure found = FindAt(puzzles, point);
        if (found == null) error = System.FormattableString.Invariant($"no puzzle at ({point.x:0.##}, {point.y:0.##})");
        return found;
    }

    /// <summary>Головоломка в точке карты: по тайлу, иначе ближайшая не дальше 0,5 тайла.</summary>
    public static PuzzleStructure FindAt(PuzzleManager puzzles, Vector2 point)
    {
        if (puzzles == null) return null;

        PuzzleStructure atTile = puzzles.GetPuzzleAt(MapPosition.ToTile(point));
        if (atTile != null) return atTile;

        List<PuzzleStructure> near = puzzles.GetPuzzlesInRadius(point, PickTolerance);
        return near.Count > 0 ? near[0] : null;
    }

    // ─── Ошибки ───────────────────────────────────────────────

    /// <summary>
    /// Для harvest/mine/attack: цель — головоломка (её таблица, стена лабиринта или обелиск в точке)?
    /// Возвращает понятную причину или null. action — "harvested" / "mined" / "attacked".
    /// Таблицу проверяет всегда, точку — только когда в ней нет ресурса/врага (это решает вызывающий).
    /// </summary>
    public static string GetNotTargetError(
        LuaFunctionExecutionContext context, Vector2 point, PuzzleManager puzzles, string action, bool checkPoint)
    {
        if (context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out LuaTable table)
            && LuaObjectList.TryGetPuzzleId(table, out int puzzleId))
        {
            PuzzleStructure byId = puzzles?.GetById(puzzleId);
            string typeName = byId != null ? GetTypeName(byId)
                : table["type"].TryRead(out string type) && !string.IsNullOrEmpty(type) ? type : AnyTypeName;
            return Explain(typeName, byId, action);
        }

        if (!checkPoint || puzzles == null) return null;

        Vector2Int tile = MapPosition.ToTile(point);
        if (puzzles.IsMazeWall(tile))
            return $"maze wall can't be {action} — find a way around it";

        PuzzleStructure puzzle = puzzles.GetPuzzleAt(tile);
        if (puzzle is PlatesPuzzle plates)
            return plates.IsPlate(tile) ? Explain(PlateName, puzzle, action) : null;
        return puzzle is CodeTaskPuzzle ? Explain(ObeliskTypeName, puzzle, action) : null;
    }

    private static string Explain(string typeName, PuzzleStructure puzzle, string action)
    {
        string hint = puzzle is MazePuzzle || typeName == MazeTypeName ? "walk to its center"
            : puzzle is PlatesPuzzle || typeName == PlatesTypeName || typeName == PlateName
                ? "step on the plates in ascending order of their numbers"
                : "use puzzle() and solve()";
        return $"{typeName} can't be {action} — {hint}";
    }

    // ─── Таблица задачи ───────────────────────────────────────

    /// <summary>
    /// Результат puzzle(): { kind, text, input = {..}, limit, solved, state, startX, startY, id, type }.
    /// У обелиска kind — "sum"/"max"/"count" (вход — текущего запуска кода), у лабиринта — "maze",
    /// у плит — "plates" (+ plates — сколько плит, pressedCount — сколько нажато, minX/minY/maxX/maxY — площадка).
    /// state — <see cref="GetStateName"/>, startX/startY — стартовая платформа (T25).
    /// </summary>
    public static LuaTable BuildTaskTable(PuzzleStructure puzzle)
    {
        if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));

        var table = new LuaTable();
        var input = new LuaTable();
        table["id"] = (double)(LuaObjectList.PuzzleIdOffset + puzzle.Id);
        table["type"] = GetTypeName(puzzle);
        table["solved"] = puzzle.IsSolved;
        table["state"] = GetStateName(puzzle.State);
        AddStartTile(table, puzzle);

        if (puzzle is CodeTaskPuzzle obelisk)
        {
            CodeTask task = obelisk.GetTask();
            table["kind"] = task.KindId;
            table["text"] = Loc.T(task.Text);
            for (int i = 0; i < task.Input.Count; i++)
                input[i + 1] = (double)task.Input[i];
            table["limit"] = (double)task.Limit;
        }
        else if (puzzle is PlatesPuzzle plates)
        {
            table["kind"] = PlatesTypeName;
            table["text"] = Loc.T(PlatesText);
            table["limit"] = 0d;
            table["plates"] = (double)plates.PlateCount;
            table["pressedCount"] = (double)(plates.IsSolved ? plates.PlateCount : plates.PressedCount);
            AddArea(table, plates);
        }
        else
        {
            table["kind"] = MazeTypeName;
            table["text"] = Loc.T(MazeText);
            table["limit"] = 0d;
        }

        table["input"] = input;
        return table;
    }

    /// <summary>Тайлы площадки с плитами: minX, minY, maxX, maxY (включительно) — для обхода check в цикле.</summary>
    public static void AddArea(LuaTable table, PlatesPuzzle plates)
    {
        RectInt area = plates.TileArea;
        table["minX"] = (double)area.xMin;
        table["minY"] = (double)area.yMin;
        table["maxX"] = (double)(area.xMax - 1);
        table["maxY"] = (double)(area.yMax - 1);
    }
}
