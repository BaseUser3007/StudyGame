/// <summary>
/// Русская таблица: описания Lua-команд (ILuaCommand.Description), методов и полей списков
/// (LuaObjectList, LuaCompletionProvider) — справка, подсветка, автодополнение, попапы.
/// Имена, сигнатуры и ошибки, возвращаемые в Lua, не переводятся. Ключ — английский текст ровно
/// как в коде команды (числа подставляются из тех же констант); расхождение ловит LocCommandsTests.
/// </summary>
public static class LocRuCommands
{
    // Русский вариант LuaObjectList.Fields (входит и в описание findObjects)
    private const string FieldsRu =
        "distance, durability, x, y, id, type, kind, tool; у врагов ещё health, maxHealth, state; у головоломок — solved";

    public static readonly (string En, string Ru)[] Entries =
    {
        // ─── Команды ──────────────────────────────────────────

        ("Hit an enemy: attack(enemy) — an enemy from findObjects/check, attack(x, y) — the enemy at a point, " +
         "attack() — the nearest one in reach. Uses the best weapon in the inventory (a sword wears out) " +
         "or a fist (5 damage); reach is 1.5 tiles, does not walk up to the enemy. " +
         "A hit takes 0.5 s. Returns true, the damage and whether the enemy died, or nil and a reason",
         "Ударить врага: attack(enemy) — врага из findObjects/check, attack(x, y) — врага в точке, " +
         "attack() — ближайшего в досягаемости. Бьёт лучшим оружием из инвентаря (меч изнашивается) " +
         "или кулаком (5 урона); дальность удара — 1.5 клетки, сам к врагу не подходит. " +
         "Удар длится 0.5 с. Возвращает true, урон и погиб ли враг, или nil и причину"),

        ("Ready-made scouting command: walks up to 6 tiles toward where the most unseen land opens up " +
         "(if everything around is revealed — to the nearest fog edge). Returns the number of land tiles revealed. " +
         $"With a type (stick, tree, goblin, obelisk…) — scouts until such an object comes into view (up to {AutoExploreCommand.MaxStepsPerCall} steps) " +
         "and returns the nearest one, like findObjects(type):getNear(), or nil and a reason. " +
         $"Each step after the first costs {AutoExploreCommand.CallCost:0} more energy. Costs more than check + move",
         "Готовая команда разведки: пройти до 6 клеток туда, где откроется больше всего ещё не виденной суши " +
         "(если вокруг всё открыто — к ближайшей границе тумана). Возвращает число открытых клеток суши. " +
         $"С типом (stick, tree, goblin, obelisk…) — разведывать, пока такой объект не появится в поле зрения (до {AutoExploreCommand.MaxStepsPerCall} шагов), " +
         "и вернуть ближайший, как findObjects(type):getNear(), или nil и причину. " +
         $"Каждый шаг после первого — ещё {AutoExploreCommand.CallCost:0} энергии. Дороже, чем check + move"),

        ("Place a structure from the inventory (e.g. build(\"workbench\")). " +
         "Without coordinates — on a free spot next to the player, with coordinates — at that point " +
         "(within reach). The ground under it must be land with no other objects. " +
         "Returns true, x, y or false and a reason",
         "Поставить постройку из инвентаря (например, build(\"workbench\")). " +
         "Без координат — на свободное место рядом с персонажем, с координатами — в эту точку " +
         "(в пределах досягаемости). Под постройкой должна быть суша без других объектов. " +
         "Возвращает true, x, y или false и причину"),

        ("What is in a tile: check(x, y), check(obj) or check(\"north\", 2) — relative to the player " +
         "(directions as in move, distance in tiles, 1 by default, diagonally — steps along both axes). " +
         "Only tiles without fog. Returns a table { x, y, type, terrain, walkable, biome, structure, player, objects, enemies, puzzle } " +
         "or nil and a reason. type is the main thing in the tile: a structure (maze wall — \"wall\", obelisk — \"obelisk\", maze center — \"chest\"), " +
         "else an enemy (its type, e.g. \"goblin\"), else the first object, else \"water\" or \"empty\"; " +
         "walkable = false on a maze wall; objects — the resources in the tile, enemies — the enemies in it (with the same methods as findObjects); " +
         "puzzle — the puzzle whose area contains the tile (like a findObjects item), or nil",
         "Что находится в клетке: check(x, y), check(obj) или check(\"north\", 2) — относительно персонажа " +
         "(направления как у move, расстояние в клетках, по умолчанию 1, по диагонали — шаги по обеим осям). " +
         "Только клетки без тумана. Возвращает таблицу { x, y, type, terrain, walkable, biome, structure, player, objects, enemies, puzzle } " +
         "или nil и причину. type — главное в клетке: постройка (стена лабиринта — \"wall\", обелиск — \"obelisk\", центр лабиринта — \"chest\"), " +
         "иначе враг (его тип, например \"goblin\"), иначе первый объект, иначе \"water\" или \"empty\"; " +
         "на стене лабиринта walkable = false; objects — массив ресурсов клетки, enemies — массив врагов в нём (с теми же методами, что у findObjects); " +
         "puzzle — головоломка, в область которой входит клетка (как элемент findObjects), или nil"),

        ("Craft an item by name (recipes are in the crafting panel). " +
         "Spends the ingredients and the recipe energy. Returns true or false and a reason",
         "Скрафтить предмет по названию (рецепты — в панели крафта). " +
         "Тратит ингредиенты и энергию рецепта. Возвращает true или false и причину"),

        ("Drop items on the ground next to the player: drop(1) — the whole slot 1, " +
         "drop(\"stone\") — all stones, the second argument sets the amount. " +
         "Dropped items are picked up with harvest (items with no ground object disappear). " +
         "Returns true and the number dropped or false and a reason",
         "Выбросить предметы на землю рядом с персонажем: drop(1) — всю ячейку 1, " +
         "drop(\"stone\") — все камни, вторым аргументом можно указать количество. " +
         "Выброшенное подбирается через harvest (предметы без объекта на земле исчезают). " +
         "Возвращает true и число выброшенных или false и причину"),

        ("Eat one piece of food: eat(\"berries\") or eat(1) — by slot number. " +
         "Restores energy and/or health, but not above the maximum. " +
         "Returns true and the amount restored (energy, health) " +
         "or nil and a reason (\"already full\" — the food is not used up)",
         "Съесть одну штуку еды: eat(\"berries\") или eat(1) — по номеру ячейки. " +
         "Восстанавливает энергию и/или здоровье, но не выше максимума. " +
         "Возвращает true и сколько восстановлено (энергия, здоровье) " +
         "или nil и причину (\"already full\" — еда не тратится)"),

        ("Array of visible objects of a type: tree, stone, boulder, gold_ore, berry_bush, mushroom, sheep, stick, pebble, log, flint (dropped); " +
         "enemies — enemy (all) or goblin; puzzles — obelisk, maze or puzzle (all). " +
         "Without an argument — everything visible, enemies and puzzles included. " +
         $"Objects have the fields {LuaObjectList.Fields} (kind — \"object\", \"enemy\" or \"puzzle\"; " +
         "a maze also has entranceX, entranceY — the tile in front of the entrance). " +
         "The array has count and methods for searching, filtering and sorting " +
         "(getNear, getFar, getMin, getMax, where, sortBy, take, find …) — see the Help section",
         "Массив объектов типа в поле зрения: tree, stone, boulder, gold_ore, berry_bush, mushroom, sheep, stick, pebble, log, flint (выброшенные); " +
         "враги — enemy (все) или goblin; головоломки — obelisk, maze или puzzle (все). " +
         "Без аргумента — всё, что видно, вместе с врагами и головоломками. " +
         $"У объекта есть поля {FieldsRu} (kind — \"object\", \"enemy\" или \"puzzle\"; " +
         "у лабиринта ещё entranceX, entranceY — клетка перед входом). " +
         "У массива есть count и методы для поиска, фильтрации и сортировки " +
         "(getNear, getFar, getMin, getMax, where, sortBy, take, find …) — см. раздел в справке"),

        ("Inventory contents: a list of occupied slots { slot, name, count }, " +
         "tools also have tool, durability and maxDurability, " +
         "food has food = true, energy and health (how much eat restores). " +
         "The result has count (occupied slots) and free (free slots)",
         "Содержимое инвентаря: список занятых ячеек { slot, name, count }, " +
         "у инструментов ещё tool, durability и maxDurability, " +
         "у еды — food = true, energy и health (сколько восстанавливает eat). " +
         "У результата есть count (занято ячеек) и free (свободно)"),

        ("Current position: a table { x, y }",
         "Текущая позиция: таблица { x, y }"),

        ("Status: a table { energy, maxEnergy, spent, health, maxHealth, day, runsLeft, threatDay, daysToThreat, threat }. " +
         "spent — energy spent since the current code run started (this call included), " +
         "threatDay — the day goblins start patrolling the map, daysToThreat — days left until then " +
         "(0 — the threat is on), threat — whether the threat is on. " +
         "Ending a day with less than 1 energy lowers tomorrow's maximum, " +
         "sleeping by a bed (build(\"bed\")) gives a bonus",
         "Состояние: таблица { energy, maxEnergy, spent, health, maxHealth, day, runsLeft, threatDay, daysToThreat, threat }. " +
         "spent — энергия, потраченная с начала текущего запуска кода (включая этот вызов), " +
         "threatDay — день, с которого гоблины патрулируют карту, daysToThreat — сколько дней до него " +
         "(0 — угроза уже идёт), threat — идёт ли угроза. " +
         "Закончить день с энергией меньше 1 — штраф к максимуму на завтра, " +
         "ночь у кровати (build(\"bed\")) — бонус"),

        ("Pick up from the ground into the inventory: sticks, stones, dropped loot. " +
         "Without arguments — the nearest one to the player, harvest(x, y) or harvest(obj) — at a nearby point. " +
         "Costs only the call. Trees and boulders can't be picked up — use mine() for them, enemies are hit with attack(). " +
         "Returns true or false and a reason",
         "Подобрать с земли в инвентарь: палки, камни, выпавшую добычу. " +
         "Без аргументов — ближайшее к персонажу, harvest(x, y) или harvest(obj) — в точке рядом. " +
         "Платится только за вызов. Деревья и валуны не подбираются — для них mine(), врагов бьют attack(). " +
         "Возвращает true или false и причину"),

        ("Mine a tree (needs an axe), a boulder or gold_ore (pickaxe), etc. Without arguments — the nearest one to the player, " +
         "mine(x, y) or mine(obj) — at a nearby point. The tool wears out, " +
         "the loot falls on the ground — pick it up with harvest. " +
         "Spends energy by the loot durability. Returns true or false and a reason",
         "Добыть дерево (нужен топор), валун или gold_ore (кирка) и т. п. Без аргументов — ближайший к персонажу, " +
         "mine(x, y) или mine(obj) — в точке рядом. Инструмент изнашивается, " +
         "добыча выпадает на землю — собрать её можно через harvest. " +
         "Тратит энергию по прочности добычи. Возвращает true или false и причину"),

        ("Walk in a direction: north, south, east, west, northeast, northwest, southeast, southwest. " +
         "Water stops you, objects are walked around; if stuck — stops after a couple of seconds. " +
         "Returns the number of tiles walked",
         "Идти в направлении: north, south, east, west, northeast, northwest, southeast, southwest. " +
         "Вода останавливает, объекты обходятся; если застрял — останавливается через пару секунд. " +
         "Возвращает число пройденных клеток"),

        ("Walk in a straight line to the tile containing the point (x, y); moveTo(obj) — to an object or enemy from findObjects/check " +
         "(an enemy — to where it was when found). Water stops you, objects are walked around; " +
         "if stuck — stops after a couple of seconds. Returns the number of tiles walked",
         "Идти по прямой к клетке, в которой лежит точка (x, y); moveTo(obj) — к объекту или врагу из findObjects/check " +
         "(врага — туда, где он был при поиске). Вода останавливает, объекты обходятся; " +
         "если застрял — останавливается через пару секунд. Возвращает число пройденных клеток"),

        ("Ready-made command: walks to the nearest visible resource (a type can be given: stone, stick…) " +
         "and picks it up like harvest (stops as soon as the object is in reach; ignores trees and boulders — use mine for them). " +
         "Costs more than findObjects + moveTo + harvest. Returns true or false and a reason",
         "Готовая команда: дойти до ближайшего видимого ресурса (можно указать тип: stone, stick…) " +
         "и подобрать его, как harvest (останавливается, как только объект в досягаемости; деревья и валуны не трогает — для них mine). " +
         "Дороже, чем findObjects + moveTo + harvest. Возвращает true или false и причину"),

        ("Prints values to the log under the main window: numbers, text, nil, " +
         "tables with fields (one level deep), for a findObjects list — count and the first " +
         $"{PrintCommand.ListPreviewItems} items. A line is at most {PrintCommand.MaxLength} characters. " +
         "Inside a loop it is paid on every pass",
         "Выводит значения в лог под главным окном: числа, текст, nil, " +
         "таблицы с полями (на один уровень), у списка findObjects — count и первые " +
         $"{PrintCommand.ListPreviewItems} элемента. Строка не длиннее {PrintCommand.MaxLength} символов. " +
         "Внутри цикла оплачивается на каждом проходе"),

        ("Conditions of a nearby puzzle: puzzle(obj) — an obelisk or maze from findObjects/check, puzzle(x, y) — at a point, " +
         "puzzle() — the nearest one in reach (1.5 tiles). Returns a table { kind, text, input, limit, solved } " +
         "(kind — \"sum\", \"max\", \"count\" or \"maze\"; input — an array of numbers, new on every code run) " +
         "or nil and a reason. The answer is given with solve(obj, answer)",
         "Условие головоломки рядом: puzzle(obj) — обелиска или лабиринта из findObjects/check, puzzle(x, y) — в точке, " +
         "puzzle() — ближайшей в досягаемости (1.5 клетки). Возвращает таблицу { kind, text, input, limit, solved } " +
         "(kind — \"sum\", \"max\", \"count\" или \"maze\"; input — массив чисел, новый в каждом запуске кода) " +
         "или nil и причину. Ответ отдаётся через solve(obj, answer)"),

        ("Answer to an obelisk task from findObjects/check: solve(obj, answer), the player within 1.5 tiles of the obelisk. " +
         "The answer must be computed by code in the same run (puzzle(obj).input changes every run). " +
         "Correct — a reward until the end of the run and true; otherwise nil and a reason (\"wrong answer\"); one attempt per obelisk per code run",
         "Ответ на задачу обелиска из findObjects/check: solve(obj, answer), персонаж — не дальше 1.5 клетки от обелиска. " +
         "Ответ надо вычислить кодом в том же запуске (puzzle(obj).input меняется при каждом запуске). " +
         "Верно — награда до конца забега и true; иначе nil и причина (\"wrong answer\"); одна попытка на обелиск за запуск кода"),

        // ─── Методы списков (LuaObjectList.Methods) ───────────

        ("nearest object", "ближайший объект"),
        ("farthest object", "самый дальний объект"),
        ("object with the smallest field value, e.g. getMin(\"durability\")",
         "объект с наименьшим значением поля, например getMin(\"durability\")"),
        ("object with the largest field value", "объект с наибольшим значением поля"),
        ("random object", "случайный объект"),
        ("object closest to the point (no farther than 0.5 by default)",
         "объект, ближайший к точке (по умолчанию не дальше 0.5)"),
        ("first object whose field equals the value, e.g. find(\"id\", other.id)",
         "первый объект, у которого поле равно значению, например find(\"id\", other.id)"),
        ("new list of the matching objects; op: ==, ~=, <, <=, >, >=",
         "новый список из подходящих объектов; op: ==, ~=, <, <=, >, >="),
        ("new list sorted by the field (descending = true — largest first)",
         "новый список, отсортированный по полю (descending = true — по убыванию)"),
        ("new list of the first n objects", "новый список из первых n объектов"),
        ("whether the object is in the list (compared by id)", "есть ли объект в списке (сравнение по id)"),
        ("index of the object in the list or nil", "номер объекта в списке или nil"),
        ("sum of a numeric field, e.g. sum(\"durability\") — energy to mine them all",
         "сумма числового поля, например sum(\"durability\") — энергия на добычу всех"),

        // Поля объекта списка (справка подставляет LuaObjectList.Fields)
        (LuaObjectList.Fields, FieldsRu),

        // ─── Автодополнение: поля таблиц и функции окон ───────

        ("x coordinate", "координата x"),
        ("y coordinate", "координата y"),
        ("unique object number — compare with a.id == b.id", "уникальный номер объекта — для сравнения a.id == b.id"),
        ("object type", "тип объекта"),
        ("distance to the player", "расстояние до персонажа"),
        ("required tool or nil", "нужный инструмент или nil"),
        ("energy to mine / tool durability", "энергия на добычу / прочность инструмента"),
        ("number of items", "количество элементов"),
        ("free inventory slots", "число свободных ячеек инвентаря"),
        ("item name", "название предмета"),
        ("inventory slot number", "номер ячейки инвентаря"),
        ("maximum tool durability", "максимальная прочность инструмента"),
        ("getStatus(): energy spent since the run started", "getStatus(): энергия, потраченная с начала запуска"),
        (LuaCompletionProvider.WindowFunctionDescription, "Функция из окна {0}"),
    };
}
