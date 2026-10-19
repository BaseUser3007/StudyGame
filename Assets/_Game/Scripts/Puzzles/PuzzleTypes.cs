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

    /// <summary>Все типы, которые есть в игре (для UnlockRegistry.SetKnownMapEvents).</summary>
    public static readonly string[] All = { CodeTask, Maze };
}
