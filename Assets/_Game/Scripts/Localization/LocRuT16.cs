/// <summary>Русская таблица задачи T16 (см. Docs/Tasks): головоломка «плиты по порядку».</summary>
public static class LocRuT16
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // LuaPuzzles.PlatesText — puzzle(plates).text
        ("Step on the plates in ascending order of their numbers. The plates are in the area minX..maxX, minY..maxY; " +
         "a plate's number is check(x, y).plate. A wrong plate releases all of them. " +
         "Each row and each column has at most one plate.",
         "Наступите на плиты по возрастанию их номеров. Плиты лежат на площадке minX..maxX, minY..maxY; " +
         "номер плиты — check(x, y).plate. Неверная плита отжимает все. " +
         "В каждой строке и каждом столбце — не больше одной плиты."),

        // PuzzleManager / PuzzleRewards: уведомления
        ("Wrong plate — all plates are released.", "Не та плита — все плиты отжаты."),
        ("You get {0} {1}.", "Вы получаете: {1} ×{0}."),
    };
}
