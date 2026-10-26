/// <summary>Русская таблица: контент из ассетов — названия/описания достижений, предметов, построек хаба, врагов.</summary>
public static class LocRuContent
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // ─── Достижения: название ─────────────────────────────
        ("First Haul", "Первая добыча"),
        ("Déjà Vu", "Дежавю"),
        ("Clean Code", "Чистый код"),
        ("Know Your Enemy", "Знай врага в лицо"),
        ("Wanderer", "Странник"),
        ("Sharp Idea", "Острая мысль"),
        ("First Blood", "Первая кровь"),
        ("Bug Report", "Баг-репорт"),
        ("Abstraction", "Абстракция"),
        ("Bon Appétit", "Приятного аппетита"),
        ("Home Sweet Home", "Дом, милый дом"),
        ("In the Loop", "В цикле"),
        ("Lumberjack", "Дровосек"),
        ("Pathfinder", "Следопыт"),
        ("Sweet Dreams", "Сладких снов"),
        ("Brace Yourself", "Готовьтесь"),
        ("Workshop", "Мастерская"),
        ("Hello, World", "Привет, мир"),

        // ─── Достижения: описание ─────────────────────────────
        ("Gather 3 resources.", "Соберите 3 ресурса."),
        ("Call harvest() 6 times.", "Вызовите harvest() 6 раз."),
        ("Chop trees and pick up 4 of their logs in one code run, spending ≤ 55 energy.",
            "Срубите деревья и подберите 4 бревна за один запуск кода, потратив ≤ 55 энергии."),
        ("Spot an enemy.", "Заметьте врага."),
        ("Use autoExplore() twice.", "Используйте autoExplore() дважды."),
        ("Craft an axe.", "Сделайте топор."),
        ("Defeat an enemy.", "Победите врага."),
        ("Get your first error in a code run.", "Получите первую ошибку при запуске кода."),
        ("Use functions in 3 code runs.", "Используйте функции в 3 запусках кода."),
        ("Eat something.", "Съешьте что-нибудь."),
        ("Build a structure in the hub.", "Возведите постройку в лагере."),
        ("Use a for loop in 5 code runs.", "Используйте цикл for в 5 запусках кода."),
        ("Chop down 5 trees.", "Срубите 5 деревьев."),
        ("Escape a maze.", "Выберитесь из лабиринта."),
        ("Build a bed.", "Постройте кровать."),
        ("Survive until the threat day.", "Доживите до дня угрозы."),
        ("Build a workbench.", "Постройте верстак."),
        ("Solve a code puzzle.", "Решите кодовую головоломку."),

        // ─── Достижения: награды и условие (AchievementReward.Describe, AchievementText) ───
        ("command `{0}`", "команда `{0}`"),
        ("recipe: {0}", "рецепт: {0}"),
        ("camp building: {0}", "постройка лагеря: {0}"),
        ("map event: {0}", "событие на карте: {0}"),
        ("Rewards: {0}", "Награды: {0}"),
        ("Goal: {0} × {1} in one code run, spending ≤ {2} energy",
            "Цель: {0} × {1} за один запуск кода, потратив ≤ {2} энергии"),
        ("items", "предметы"),
        // Конструкции языка (LuaConstructs.Info.DisplayName)
        ("variable `local`", "переменная `local`"),
        ("condition `if`", "условие `if`"),
        ("loop `for`", "цикл `for`"),
        ("loop `while`", "цикл `while`"),
        ("loop `repeat`", "цикл `repeat`"),
        ("function definition `function`", "объявление функции `function`"),
        ("class windows", "окна-классы"),

        // ─── Предметы (ResourceItem.displayName) ──────────────
        ("Axe", "Топор"),
        ("Pickaxe", "Кирка"),
        ("Sword", "Меч"),
        ("Workbench", "Верстак"),
        ("Bed", "Кровать"),
        ("Campfire", "Костёр"),
        ("Log", "Бревно"),
        ("Stick", "Палка"),
        ("Stone", "Камень"),
        ("Flint", "Кремень"),
        ("Gold", "Золото"),
        ("Crystal", "Кристалл"),
        ("Berries", "Ягоды"),
        ("Raw Meat", "Сырое мясо"),
        ("Cooked Meat", "Жареное мясо"),

        // ─── Объекты на карте (ResourceVariant.displayName) ───
        ("Tree", "Дерево"),
        ("Boulder", "Валун"),
        ("Gold Ore", "Золотая руда"),
        ("Berry Bush", "Ягодный куст"),
        ("Sheep", "Овца"),

        // ─── Станции крафта (CraftingStation.DisplayName — исходник пока русский) ───
        ("none", "нет"),
        ("Furnace", "Печь"),

        // ─── Враги (displayName) ──────────────────────────────
        ("Goblin", "Гоблин"),
        ("Goblin Guard", "Гоблин-страж"),

        // ─── Постройки лагеря: название и описание ────────────
        ("Storage", "Склад"),
        ("Shrine", "Святилище"),
        ("Watchtower", "Сторожевая башня"),
        ("Healer's Hut", "Хижина лекаря"),
        ("Storage — +2 inventory slots in every run.", "Склад — +2 ячейки инвентаря в каждом забеге."),
        ("Shrine — +10 energy limit in every run.", "Святилище — +10 к лимиту энергии в каждом забеге."),
        ("Watchtower — the threat comes 1 day later in every run.",
            "Сторожевая башня — в каждом забеге угроза приходит на 1 день позже."),
        ("Healer's Hut — +25 max health in every run.", "Хижина лекаря — +25 к максимуму здоровья в каждом забеге."),

        // ─── Эффекты построек (HubBuildingEffect.Describe; {0} — "+10" со знаком) ───
        ("{0} energy limit", "{0} к лимиту энергии"),
        ("{0} day(s) before the threat", "{0} к дням до угрозы"),
        ("{0} inventory slot(s)", "{0} к ячейкам инвентаря"),
        ("{0} max health", "{0} к максимуму здоровья"),
        ("{0} code run(s) per day", "{0} к запускам кода в день"),

        // ─── Головоломки: поле text из puzzle() (описание для игрока, в коде его не сравнивают) ───
        ("Return the sum of all numbers in input.", "Верните сумму всех чисел из input."),
        ("Return the largest number in input.", "Верните наибольшее число из input."),
        ("Return how many numbers in input are greater than limit.", "Верните, сколько чисел из input больше limit."),
        ("Reach the center of the maze.", "Дойдите до центра лабиринта."),
    };
}
