/// <summary>Русская таблица: события на карте, открываемые числом выполненных достижений.</summary>
public static class LocRuEvents
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // PuzzleTypes.GetDisplayName — названия событий на карте
        ("Obelisks", "Обелиски"),
        ("Maze", "Лабиринт"),
        ("Pressure plates", "Плиты"),

        // AchievementText.FormatMapEvents — строка над списком достижений
        ("Achievements: {0}/{1} · next map event: {2} at {3}",
         "Достижения: {0}/{1} · следующее событие карты: {2} (нужно достижений: {3})"),
        ("Achievements: {0}/{1} · all map events unlocked",
         "Достижения: {0}/{1} · все события карты открыты"),
    };
}
