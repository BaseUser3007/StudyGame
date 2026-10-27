/// <summary>Русская таблица задачи T25 (часть A): стартовая платформа и одна попытка на головоломку.</summary>
public static class LocRuT25
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // PuzzleManager — правило попытки, провал
        ("One attempt: the next code run. If you don't solve it, the puzzle disappears.",
            "Одна попытка: следующий запуск. Не решите — головоломка исчезнет."),
        ("Puzzle failed: {0} is gone.", "Попытка не удалась — «{0}» больше нет."),

        // PuzzleManager.GetBriefing — названия и что сделать
        ("Obelisk", "Обелиск"),
        ("Add up all numbers of the task input.", "Сложите все числа входа задачи."),
        ("Find the largest number of the task input.", "Найдите наибольшее число входа задачи."),
        ("Count the numbers of the task input that are greater than its limit.",
            "Посчитайте числа входа задачи, которые больше её порога (limit)."),
        ("Read the task with puzzle(obelisk) — the input changes every run — and answer with solve(obelisk, answer) in the same run.",
            "Прочитайте задачу через puzzle(obelisk) — вход меняется в каждом запуске — и ответьте solve(obelisk, answer) в том же запуске."),
        ("Reach the chest in the centre of the maze. Inside the maze you only see the tiles next to you.",
            "Доберитесь до сундука в центре лабиринта. Внутри видно только соседние тайлы."),
        ("Step on all plates in ascending order of their numbers. A number is visible only with check(x, y).plate; a wrong plate releases all of them.",
            "Наступите на все плиты по возрастанию номеров. Номер виден только через check(x, y).plate; неверная плита отжимает все."),

        // PuzzleRewards — награда на карточке
        ("+{0} max health for this run and full health.", "+{0} к максимуму здоровья до конца забега и полное здоровье."),
        ("The threat comes {0} day(s) later.", "Угроза придёт на {0} дн. позже."),
        ("+{0} max energy for this run and +{0} energy now.",
            "+{0} к максимуму энергии до конца забега и +{0} энергии сейчас."),
        ("Nothing.", "Ничего."),
        ("{0} × {1} into the inventory.", "{0} × {1} в инвентарь."),

        // Часть B (Lua): LuaErrorExplainer — solve() до попытки
        ("The puzzle has not started yet: it is solved only in its attempt — the code run after the player steps on its start platform.",
            "Головоломка ещё не начата: её решают только в попытке — запуске кода после того, как персонаж встал на её стартовую платформу."),
        ("Walk onto the platform first: `moveTo(p.startX, p.startY)` (that run stops there), then solve the puzzle in the next run.",
            "Сначала встаньте на платформу: `moveTo(p.startX, p.startY)` (этот запуск на ней остановится), а решайте в следующем запуске."),

        // Часть C (UI): карточка PuzzleBriefingPopup, строка лога, индикатор в TopBar
        ("Puzzle started", "Головоломка начата"),
        ("Execution interrupted by a puzzle", "Выполнение прервано головоломкой"),
        ("Reward", "Награда"),
        ("Puzzle started: {0}. {1}", "Головоломка начата: {0}. {1}"),
        ("Puzzle: the next run is your attempt", "Головоломка: попытка — следующий запуск"),
        ("Puzzle attempt in progress", "Идёт попытка решить головоломку"),
    };
}
