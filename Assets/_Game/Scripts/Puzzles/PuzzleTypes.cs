/// <summary>
/// Id типов головоломок. Совпадают с id событий на карте (UnlockKind.MapEvent,
/// MetaProgress.unlockedEvents) и с Id события GameEventBus PuzzleSolved.
/// </summary>
public static class PuzzleTypes
{
    /// <summary>Обелиск с задачей на код (sum/max/count).</summary>
    public const string CodeTask = "code_task";
    /// <summary>Лабиринт 7×7 с сундуком в центре.</summary>
    public const string Maze = "maze";
    /// <summary>Площадка 5×5 с плитами: наступить по возрастанию номеров (номера — через check).</summary>
    public const string Plates = "plates";

    /// <summary>Все типы, которые есть в игре (для UnlockRegistry.SetKnownMapEvents).</summary>
    public static readonly string[] All = { CodeTask, Maze, Plates };

    /// <summary>
    /// Название события на карте для UI на языке игры (Loc): «Obelisks», «Maze», «Pressure plates».
    /// Неизвестный id возвращается как есть.
    /// </summary>
    public static string GetDisplayName(string id)
    {
        switch (id)
        {
            case CodeTask: return Loc.T("Obelisks");
            case Maze: return Loc.T("Maze");
            case Plates: return Loc.T("Pressure plates");
            default: return id ?? string.Empty;
        }
    }
}
