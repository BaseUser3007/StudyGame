/// <summary>Русская таблица: интерфейс (UXML, TopBar, лагерь, окна кода, туториал, попапы, подсказки к ошибкам).</summary>
/// <remarks>
/// Ключ — английский исходник ровно как в коде/UXML (склейки строк — одной строкой, {N} — как в Loc.F).
/// Контент (достижения, предметы, постройки, награды, имена конструкций) — в LocRuContent,
/// описания команд — в LocRuCommands: здесь эти ключи не повторяются.
/// </remarks>
public static class LocRuUi
{
    public static readonly (string En, string Ru)[] Entries =
    {
        // ─── UXML: верхняя панель (TopBar.uxml) ───────────────
        ("Day 1 · Runs 0/3", "День 1 · Запуски 0/3"),
        ("HP 100 / 100", "Здоровье 100 / 100"),
        ("Energy: 100 / 100", "Энергия: 100 / 100"),
        ("Cost per pass: 0", "Цена прохода: 0"),
        ("Help", "Справка"),
        ("Achievements", "Достижения"),
        ("Hints: On", "Подсказки: вкл"),
        ("Hints: Off", "Подсказки: выкл"),
        ("Tutorial and concept hints", "Обучение и подсказки по понятиям"),
        ("Run", "Запуск"),
        ("Stop", "Стоп"),
        // Отладка: точки останова и шаги
        ("Paused", "Пауза"),
        ("Paused · {0}, line {1}", "Пауза · {0}, строка {1}"),
        ("Continue", "Продолжить"),
        ("Step", "Шаг"),
        ("Run to the next breakpoint", "Выполнять до следующей точки останова"),
        ("Run to the next line that costs energy and pause again",
            "Выполнить до следующей строки, которая стоит энергию, и снова встать"),
        ("Click a line number to set a breakpoint: the code will pause there",
            "Нажмите на номер строки, чтобы поставить точку останова: код встанет на ней"),
        ("End Day", "Конец дня"),
        ("Exit Run", "Выйти из забега"),
        ("Return to the camp: only materials are carried over", "Вернуться в лагерь: с собой уносятся только материалы"),
        ("Simulation", "Симуляция"),
        ("Commands", "Команды"),

        // ─── TopBar (C#) ──────────────────────────────────────
        ("Day {0} · Runs {1}/{2}", "День {0} · Запуски {1}/{2}"),
        ("Threat!", "Угроза!"),
        ("Threat tomorrow!", "Угроза завтра!"),
        ("Threat in {0} days", "Угроза через {0} дн."),
        ("Enemy patrols roam the map since day {0}", "Вражеские патрули бродят по карте с {0}-го дня"),
        ("Enemy patrols appear on day {0}", "Вражеские патрули появятся на {0}-й день"),
        ("Cost per pass: {0}", "Цена прохода: {0}"),
        ("Energy: {0} / {1}", "Энергия: {0} / {1}"),
        ("HP {0} / {1}", "Здоровье {0} / {1}"),

        // ─── Окна кода (Main/Auxiliary.uxml, RunLog) ──────────

        ("Add a method window", "Добавить окно-метод"),
        ("Energy cost of each line", "Цена энергии каждой строки"),
        ("Run log", "Лог"),
        ("Clear the log", "Очистить лог"),
        ("Cost {0}", "Цена {0}"),
        ("Try: {0}", "Попробуйте: {0}"),
        ("free", "бесплатно"),
        ("{0} en", "{0} эн"),

        // ─── Инвентарь и крафт (CraftingPanel) ───────────────
        ("Craft", "Крафт"),
        ("Crafting", "Крафт"),
        ("Select an item", "Выберите предмет"),
        ("Requires:", "Нужно:"),
        ("Station: {0}", "Станция: {0}"),
        ("Station: {0} (must be nearby)", "Станция: {0} (нужна рядом)"),
        ("Station: {0} (nearby)", "Станция: {0} (рядом)"),
        ("Energy: {0}", "Энергия: {0}"),

        // ─── Библиотека кода (CodeLibraryPanel) ──────────────
        ("Code library: drag a window here to save it", "Библиотека кода: перетащите окно сюда, чтобы сохранить"),
        ("Code library", "Библиотека кода"),
        ("Drag a window here by its header to save its code", "Перетащите окно за заголовок сюда, чтобы сохранить его код"),
        ("Class windows are not unlocked yet: entries are kept, but they can't be opened as windows for now",
            "Окна-классы ещё не открыты: записи хранятся, но открыть их как окна пока нельзя"),
        ("Nothing saved yet", "Пока ничего не сохранено"),
        ("{0} lines · {1}", "{0} стр. · {1}"),
        ("Open", "Открыть"),
        ("Open in a new window", "Открыть в новом окне"),
        ("Class windows are not unlocked yet", "Окна-классы ещё не открыты"),
        ("Delete from the library", "Удалить из библиотеки"),
        ("Delete?", "Удалить?"),

        // ─── Справка (CommandHelp) ────────────────────────────
        ("The cost is charged on every call. Mining and crafting also spend energy for the resource and the recipe.",
            "Цена списывается при каждом вызове. Добыча и крафт дополнительно тратят энергию по ресурсу и рецепту."),
        ("findObjects list", "Список findObjects"),
        ("Methods are called with a colon: list:getNear(). where, sortBy and take return a new list, " +
         "so they can be chained. Object fields: {0}. Harvested objects are skipped.",
            "Методы вызываются через двоеточие: list:getNear(). where, sortBy и take возвращают новый список — " +
            "их можно выстраивать в цепочку. Поля объекта: {0}. Собранные объекты пропускаются."),
        ("Ready-made commands", "Готовые команды"),
        ("They do what a few basic commands do, but cost more: your own code saves energy.",
            "Делают то же, что несколько базовых команд, но стоят дороже — свой код экономит энергию."),
        ("Language constructs", "Конструкции языка"),
        ("Charged every time they execute: a condition on every check, a loop on every iteration. " +
         "Locked constructs are unlocked during the game: code that uses them won't run.",
            "Списывается каждый раз при выполнении: условие — при каждой проверке, цикл — на каждой итерации. " +
            "Закрытые (locked) конструкции открываются по ходу игры — код с ними не запустится."),
        ("{0} en.", "{0} эн."),
        ("{0} en. {1}", "{0} эн. {1}"),
        ("locked", "закрыто"),
        ("per execution", "за выполнение"),
        ("per condition check", "за проверку условия"),
        ("per branch entry", "за вход в ветку"),
        ("per iteration", "за итерацию"),
        ("per call", "за вызов"),
        ("per window call", "за вызов окна"),

        // ─── Лагерь (HubPanel.uxml, RunScreens, HubBuildPanel) ─
        ("Camp", "Лагерь"),
        ("Material stock", "Склад материалов"),
        ("Empty for now — bring materials back from runs", "Пока пусто — приносите материалы из забегов"),
        ("Start Run", "Начать забег"),
        ("Reset tutorial", "Сбросить обучение"),
        ("Reset progress", "Сбросить прогресс"),
        ("Click again to reset", "Нажмите ещё раз для сброса"),
        ("Click again to reset ALL", "Нажмите ещё раз, чтобы сбросить ВСЁ"),
        ("Buildings", "Постройки"),
        ("Buildings are paid with the material stock and make every next run stronger.",
            "Постройки оплачиваются со склада материалов и усиливают каждый следующий забег."),
        ("No buildings to build", "Строить нечего"),
        ("Click a cell to build · Right click / Esc to cancel", "Клик по клетке — построить · ПКМ / Esc — отмена"),
        ("No runs yet", "Забегов ещё не было"),
        ("Runs finished: {0} (defeats: {1})", "Забегов завершено: {0} (поражений: {1})"),
        ("{0}×{1} cells", "{0}×{1} клет."),
        ("Cancel", "Отмена"),
        ("Build", "Построить"),
        ("Placing — click a free cell", "Размещение — кликните по свободной клетке"),
        ("Ready to build", "Можно строить"),
        ("Built", "Построено"),
        ("Locked · Unlock: {0}", "Закрыто · Открывает: {0}"),
        ("Locked · complete an achievement", "Закрыто · выполните достижение"),
        ("Placing: {0}", "Размещение: {0}"),
        ("{0} built", "{0}: построено"),
        // HubService.Describe (уведомления и состояние карточки)
        ("Unknown building", "Неизвестная постройка"),
        ("Not unlocked yet — complete an achievement", "Ещё не открыто — выполните достижение"),
        ("Already built", "Уже построено"),
        ("Not enough materials", "Не хватает материалов"),
        ("Doesn't fit the camp grid", "Не помещается в сетку лагеря"),
        ("The place is occupied", "Место занято"),

        // ─── Выход из забега и итоги (RunScreens.uxml) ────────
        ("Leave the run?", "Покинуть забег?"),
        ("Only materials are carried to the camp. Tools, buildings and food stay here.",
            "В лагерь уносятся только материалы. Инструменты, постройки и еда останутся здесь."),
        ("Stay", "Остаться"),
        ("Leave", "Уйти"),
        ("Run complete", "Забег завершён"),
        ("Defeat", "Поражение"),
        ("Carried to the camp", "Унесено в лагерь"),
        ("Nothing", "Ничего"),
        ("None this run", "В этом забеге — нет"),
        ("To the Camp", "В лагерь"),
        ("Your health ran out on day {0}. Everything gathered in this run is lost.",
            "Здоровье закончилось на {0}-й день. Всё собранное в этом забеге потеряно."),
        ("Days in the run: {0}", "Дней в забеге: {0}"),
        ("Stays in the run: {0}", "Остаётся в забеге: {0}"),

        // ─── Достижения (AchievementsPanel.uxml, тосты) ───────
        ("Progress is kept between runs, even after a defeat. Rewards unlock commands, language constructs and recipes.",
            "Прогресс сохраняется между забегами, даже после поражения. Награды открывают команды, конструкции языка и рецепты."),
        ("No achievements yet", "Достижений пока нет"),
        ("Completed", "Выполнено"),
        ("This run: {0} / {1}", "Этот запуск: {0} / {1}"),
        ("Achievement unlocked", "Достижение получено"),
        ("+{0} more", "ещё +{0}"),

        // ─── Окно открытия (UnlockPopup) ──────────────────────
        ("Unlocked", "Открыто"),
        ("OK", "OK"),
        ("Next ({0})", "Далее ({0})"),
        ("New camp building", "Новая постройка лагеря"),
        ("It can now be built in the camp.", "Теперь её можно построить в лагере."),
        ("New map event", "Новое событие на карте"),
        ("It can now appear on the map during runs.", "Теперь оно может появиться на карте во время забегов."),
        ("New command", "Новая команда"),
        ("New commands", "Новые команды"),
        ("This command is now available in your code.", "Теперь эту команду можно использовать в коде."),
        ("Example", "Пример"),
        ("Energy", "Энергия"),
        ("Costs {0} energy per call.", "Стоит {0} энергии за вызов."),
        ("Ready-made command: it does what a few basic commands do, but costs more — your own code saves energy.",
            "Готовая команда: делает то же, что несколько базовых, но стоит дороже — свой код экономит энергию."),
        ("It is now listed in Help and suggested by autocompletion.",
            "Теперь она есть в справке и в автодополнении."),
        ("New language construct", "Новая конструкция языка"),
        ("Decompiled", "Декомпиляция"),
        ("The same, with your own code", "То же самое своим кодом"),
        ("Your code", "Ваш код"),
        ("Your own code saves {0} energy on every pass — and you decide which steps you actually need.",
            "Свой код экономит {0} энергии на каждом проходе — и вы сами решаете, какие шаги на самом деле нужны."),
        ("{0}() stays available: handy, but expensive.", "{0}() остаётся доступной: удобно, но дорого."),
        ("The new commands are listed in Help and suggested by autocompletion.",
            "Новые команды есть в справке и в автодополнении."),
        ("New recipe", "Новый рецепт"),
        ("It now shows up in the crafting panel.", "Теперь он есть в панели крафта."),
        ("Craft it with:", "Сделать так:"),
        ("Ingredients", "Ингредиенты"),
        ("Needs a {0} nearby.", "Нужна станция рядом: {0}."),
        ("Crafting costs {0} energy.", "Крафт стоит {0} энергии."),
        ("Costs {0} energy each time it runs.", "Стоит {0} энергии при каждом выполнении."),
        ("Costs {0} energy per condition check, else — {1}.", "Стоит {0} энергии за проверку условия, else — {1}."),
        ("Costs {0} energy per iteration.", "Стоит {0} энергии за итерацию."),
        ("Costs {0} energy per condition check.", "Стоит {0} энергии за проверку условия."),
        ("Calling a window costs {0} energy, like a function call.", "Вызов окна стоит {0} энергии, как вызов функции."),

        // ─── Уведомления (CodeEditorManager, Core, Enemies, Puzzles) ─
        ("Tutorial reset: hints will guide you again", "Обучение сброшено: подсказки снова проведут вас"),
        ("{0}: nothing to save, the code is empty", "{0}: нечего сохранять, код пуст"),
        ("{0} updated in the library", "{0}: обновлено в библиотеке"),
        ("{0} saved to the library", "{0}: сохранено в библиотеку"),
        ("Failed to save {0}: {1}", "Не удалось сохранить {0}: {1}"),
        ("{0} are not unlocked yet", "{0} ещё не открыты"),
        (" — spent {0} energy", " — потрачено {0} энергии"),
        ("Execution completed", "Выполнение завершено"),
        ("Execution stopped", "Выполнение остановлено"),
        ("Run finished", "Запуск завершён"),
        ("Can't end the day while the code is running", "Нельзя закончить день, пока выполняется код"),
        ("Can't reset progress while the code is running", "Нельзя сбросить прогресс, пока выполняется код"),
        ("Can't leave the run while the code is running", "Нельзя покинуть забег, пока выполняется код"),
        ("Failed to save progress: {0}", "Не удалось сохранить прогресс: {0}"),
        ("Day {0}: max energy {1}. {2}", "День {0}: максимум энергии {1}. {2}"),
        ("−{0}: energy ran out", "−{0}: энергия кончилась"),
        ("+{0}: slept in bed ({1} nights)", "+{0}: сон в кровати ({1} ноч.)"),
        ("+{0}: slept in bed ({1} night)", "+{0}: сон в кровати ({1} ночь)"),
        (", +{0} health", ", +{0} здоровья"),
        ("Goblins will start patrolling tomorrow.", "Завтра гоблины начнут патрулировать."),
        ("The threat has begun: goblin patrols come out of the fog every day.",
            "Угроза началась: каждый день из тумана выходят патрули гоблинов."),
        ("An enemy patrol came out of the fog", "Из тумана вышел вражеский патруль"),
        ("{0} enemy patrols came out of the fog", "Из тумана вышло вражеских патрулей: {0}"),
        ("Maze solved!", "Лабиринт пройден!"),
        ("Puzzle solved!", "Головоломка решена!"),
        ("The threat is delayed by {0} days (day {1}).", "Угроза отложена на {0} дн. (день {1})."),
        ("The threat is delayed by {0} day (day {1}).", "Угроза отложена на {0} день (день {1})."),
        ("The chest is empty.", "Сундук пуст."),
        ("Found {0} {1} in the chest.", "В сундуке: {1} ×{0}."),
        ("Found {0} {1}: {2} taken, {3} on the ground (inventory is full).",
            "Найдено: {1} ×{0}: {2} взято, {3} на земле (инвентарь полон)."),
        ("Found {0} {1}, but the inventory is full — {2} lost.", "Найдено: {1} ×{0}, но инвентарь полон — {2} потеряно."),
        ("Max energy +{0} for this run, +{0} energy now.", "Максимум энергии +{0} на этот забег, +{0} энергии сейчас."),
        ("Max health +{0} for this run, health restored.", "Максимум здоровья +{0} на этот забег, здоровье восстановлено."),

        // ─── Туториал: оверлей и контроллер ──────────────────
        ("Tutorial", "Обучение"),
        ("Skip tutorial", "Пропустить обучение"),
        ("Hints off", "Выключить подсказки"),
        ("Next", "Далее"),
        ("Got it", "Понятно"),
        ("Concept", "Понятие"),
        ("Camp · {0}/{1}", "Лагерь · {0}/{1}"),
        ("Tutorial · {0}/{1}", "Обучение · {0}/{1}"),
        ("Tutorial skipped. Concept hints stay on — turn them off with the Hints button.",
            "Обучение пропущено. Подсказки по понятиям остаются — их выключает кнопка «Подсказки»."),
        ("Hints are off. Turn them back on with the Hints button.",
            "Подсказки выключены. Включить их снова можно кнопкой «Подсказки»."),

        // ─── Туториал: шаги (TutorialSteps.Steps) ────────────
        ("Achievements unlock new commands and language features. Take a look at what to aim for.",
            "Достижения открывают новые команды и возможности языка. Посмотрите, к чему стремиться."),
        ("Open Achievements or press Next", "Откройте «Достижения» или нажмите «Далее»"),
        ("Spend materials from your runs on camp buildings: each one makes every next run stronger. " +
         "Press Build, then click a free cell on the grid.",
            "Тратьте материалы из забегов на постройки лагеря: каждая усиливает все следующие забеги. " +
            "Нажмите «Построить», затем кликните по свободной клетке сетки."),
        ("Build something or press Next", "Постройте что-нибудь или нажмите «Далее»"),
        ("Welcome to camp. Start a run to explore a new island.", "Добро пожаловать в лагерь. Начните забег, чтобы исследовать новый остров."),
        ("Press Start Run", "Нажмите «Начать забег»"),
        ("You can't move the hero directly. Write Lua code here — the hero does what the code says.",
            "Героем нельзя управлять напрямую. Пишите здесь код на Lua — герой делает то, что сказано в коде."),
        ("Click into the window and type `obtain()` — this is a command call. " +
         "It walks to the nearest item and picks it up.",
            "Кликните в окно и напишите `obtain()` — это вызов команды. " +
            "Герой дойдёт до ближайшего предмета и подберёт его."),
        ("Type obtain() in the Main window", "Напишите obtain() в окне Main"),
        ("This number is the energy cost of the line. Ready-made commands like `obtain` " +
         "are convenient but expensive.",
            "Это число — цена строки в энергии. Готовые команды вроде `obtain` " +
            "удобны, но дороги."),
        ("Press Run to execute your code. Press it again (Stop) to interrupt.",
            "Нажмите «Запуск», чтобы выполнить код. Нажмите ещё раз («Стоп»), чтобы прервать."),
        ("Press Run", "Нажмите «Запуск»"),
        ("Energy is your daily budget. Every command spends it, even failed ones.",
            "Энергия — ваш бюджет на день. Её тратит каждая команда, даже неудачная."),
        ("Wait for the code to finish", "Дождитесь окончания кода"),
        ("Gathered items go here. Gather 3 items to unlock cheaper commands and conditions.",
            "Собранные предметы попадают сюда. Соберите 3 предмета, чтобы открыть более дешёвые команды и условия."),
        ("Gather 3 items — run obtain() a few times", "Соберите 3 предмета — запустите obtain() несколько раз"),
        ("Arguments go inside the brackets: `obtain(\"stick\")` picks up only sticks. " +
         "Text values are written in quotes.",
            "Аргументы пишутся в скобках: `obtain(\"stick\")` подбирает только палки. " +
            "Текст пишется в кавычках."),
        ("You have {runs} runs per day. After the last one the day ends and energy refills.",
            "В день у вас {runs} запуска кода. После последнего день заканчивается и энергия восполняется."),
        ("Help lists every command you have and its cost.", "В справке — все ваши команды и их цена."),
        ("Open Help", "Откройте «Справку»"),
        ("Return to camp at any time. Only materials are kept — and nothing if you die.",
            "Вернуться в лагерь можно в любой момент. Сохраняются только материалы — а при гибели ничего."),
        ("Goblins will patrol from tomorrow. Use `findObjects(\"enemy\")` to see them before they see you.",
            "С завтрашнего дня патрулируют гоблины. `findObjects(\"enemy\")` покажет их раньше, чем они заметят вас."),
        ("Extra windows become functions named after the title. " +
         "Drag a window by its header onto the library tab on the right to save it.",
            "Дополнительные окна становятся функциями с именем из заголовка. " +
            "Перетащите окно за заголовок на вкладку библиотеки справа, чтобы сохранить его."),
        ("`local items = findObjects(\"stick\")` stores a list in a variable. " +
         "Reuse it instead of searching again — it saves energy.",
            "`local items = findObjects(\"stick\")` сохраняет список в переменной. " +
            "Используйте её вместо повторного поиска — это экономит энергию."),
        ("Run code that uses local", "Запустите код с local"),
        ("End the day early to refill energy. Ending with less than 1 energy lowers tomorrow's limit.",
            "Закончите день досрочно, чтобы восполнить энергию. Если закончить день с энергией меньше 1, завтрашний лимит снизится."),
        ("Achievements unlock new commands and language features.", "Достижения открывают новые команды и возможности языка."),
        ("Open Achievements", "Откройте «Достижения»"),
        ("Recipes are listed here. Crafting is done in code: `craft(\"flint\")`, then `craft(\"axe\")`.",
            "Здесь список рецептов. Крафт делается в коде: `craft(\"flint\")`, затем `craft(\"axe\")`."),
        ("Craft an axe with code", "Сделайте топор кодом"),
        ("Trees need an axe: `mine(tree)` chops it and drops logs on the ground. " +
         "Pick them up with `harvest`.",
            "Для деревьев нужен топор: `mine(tree)` рубит дерево, и брёвна падают на землю. " +
            "Подберите их через `harvest`."),
        ("Chop a tree with mine()", "Срубите дерево через mine()"),

        // ─── Туториал: понятия (TutorialSteps.Concepts) ──────
        ("Function call", "Вызов функции"),
        ("`obtain()` is a function call: the name says what to do, the brackets `()` make it happen. " +
         "Lines run from top to bottom, one call after another. " +
         "Every call costs energy — see the number to the right of the line.",
            "`obtain()` — вызов функции: имя говорит, что сделать, а скобки `()` это выполняют. " +
            "Строки выполняются сверху вниз, один вызов за другим. " +
            "Каждый вызов стоит энергии — смотрите число справа от строки."),
        ("Arguments", "Аргументы"),
        ("Values inside the brackets are arguments — they tell the command what exactly to do. " +
         "Text goes in quotes: `obtain(\"stick\")`. Several arguments are separated by commas: " +
         "`moveTo(x, y)`.",
            "Значения в скобках — аргументы: они уточняют, что именно сделать команде. " +
            "Текст пишется в кавычках: `obtain(\"stick\")`. Несколько аргументов разделяются запятыми: " +
            "`moveTo(x, y)`."),
        ("Variable", "Переменная"),
        ("Condition", "Условие"),
        ("Loop", "Цикл"),
        ("A loop repeats its block. The cost on the line is for one pass — every iteration pays " +
         "again for everything inside, so keep the body cheap and make sure the loop ends.",
            "Цикл повторяет свой блок. Цена на строке — за один проход: каждая итерация снова оплачивает " +
            "всё, что внутри, поэтому держите тело дешёвым и следите, чтобы цикл заканчивался."),
        ("Function", "Функция"),
        ("A function is a named piece of code: define it once, call it by name like a command. " +
         "Names in its brackets are parameters — they receive the arguments of the call. " +
         "The body is paid on every call.",
            "Функция — именованный кусок кода: объявите её один раз и вызывайте по имени, как команду. " +
            "Имена в её скобках — параметры, они получают аргументы вызова. " +
            "Тело оплачивается при каждом вызове."),

        // ─── Конструкции языка (LuaConstructGuide; примеры кода не переводятся) ─
        ("A variable keeps a value under a name, so you can use it again instead of asking the world twice. " +
         "Every findObjects() call costs energy — store its result in a local and reuse it.",
            "Переменная хранит значение под именем, чтобы использовать его снова, а не спрашивать мир дважды. " +
            "Каждый вызов findObjects() стоит энергии — сохраните результат в local и используйте повторно."),
        ("obtain() never complained: when nothing was in sight, it simply did nothing. Your own code is less " +
         "forgiving — findObjects(\"stick\"):getNear() returns nil when there is no stick, and stick.x on nil " +
         "stops the run with an error.",
            "obtain() никогда не жаловалась: если ничего не было видно, она просто ничего не делала. Свой код " +
            "строже — findObjects(\"stick\"):getNear() возвращает nil, если палки нет, а stick.x у nil " +
            "останавливает запуск с ошибкой."),
        ("if runs a block only when its condition is true. nil and false count as \"no\", anything else " +
         "as \"yes\". elseif and else add alternatives.",
            "if выполняет блок, только если условие истинно. nil и false считаются «нет», всё остальное — " +
            "«да». elseif и else добавляют другие варианты."),
        ("Each check costs 0.5. An error ends the run and wastes one of today's runs — checking first is cheaper.",
            "Каждая проверка стоит 0.5. Ошибка обрывает запуск и сжигает один из сегодняшних запусков — проверить заранее дешевле."),
        ("You have called harvest() 6 times — line after line of the same code. One tree drops 4 items, " +
         "and each of them needed its own lines.",
            "Вы вызвали harvest() 6 раз — строка за строкой одного и того же кода. Одно дерево даёт 4 предмета, " +
            "и для каждого нужны свои строки."),
        ("for repeats a block a set number of times. Its variable (i) counts the passes: 1, 2, 3… " +
         "Use count to go through a whole list.",
            "for повторяет блок заданное число раз. Его переменная (i) считает проходы: 1, 2, 3… " +
            "Чтобы пройти весь список, используйте count."),
        ("A loop saves lines, not energy: every pass costs 1 plus everything inside. Search once before " +
         "the loop — a findObjects() inside it is paid on every pass. Here: 1 + 4 per item, instead of 5 " +
         "per item with a new search each time.",
            "Цикл экономит строки, а не энергию: каждый проход стоит 1 плюс всё, что внутри. Ищите один раз до " +
            "цикла — findObjects() внутри оплачивается на каждом проходе. Здесь: 1 + 4 за предмет вместо 5 " +
            "за предмет с новым поиском каждый раз."),
        ("You paid 10 energy for every autoExplore(). Walking yourself is cheaper — but you never know how " +
         "far the land goes, so how many move() lines should you write?",
            "Вы платили 10 энергии за каждый autoExplore(). Ходить самому дешевле — но заранее не знаешь, " +
            "докуда тянется суша, так сколько строк move() писать?"),
        ("while repeats a block as long as its condition is true. Use it when you don't know the number of " +
         "passes in advance: walk until something stops you, chop while trees are in sight.",
            "while повторяет блок, пока условие истинно. Он нужен, когда число проходов заранее " +
            "неизвестно: идти, пока что-то не остановит, рубить, пока видны деревья."),
        ("Each check of the condition costs 1: here 2 energy per 6 tiles, against 10 for autoExplore(). " +
         "Make sure the condition becomes false — an endless loop drains the whole day.",
            "Каждая проверка условия стоит 1: здесь 2 энергии за 6 клеток против 10 за autoExplore(). " +
            "Следите, чтобы условие стало ложным — бесконечный цикл съест весь день."),
        ("Clean code — 4 logs on a tight budget. But while checks before the first pass, and sometimes you " +
         "have to act first and only then see whether it was enough.",
            "Чистый код — 4 бревна на скромном бюджете. Но while проверяет условие до первого прохода, а иногда " +
            "нужно сначала сделать шаг и только потом посмотреть, хватило ли."),
        ("repeat … until runs the body first and checks the condition after it, so the body always runs at " +
         "least once. The loop stops when the until condition becomes true. Locals of the body are visible in until.",
            "repeat … until сначала выполняет тело и лишь потом проверяет условие, поэтому тело выполняется хотя " +
            "бы раз. Цикл останавливается, когда условие until становится истинным. Локальные переменные тела видны в until."),
        ("Each until check costs 1, like a while check. Here: 3 energy per 3 tiles walked.",
            "Каждая проверка until стоит 1, как проверка while. Здесь: 3 энергии за 3 пройденные клетки."),
        ("You have written a for loop in 5 runs — probably with the same \"find, walk, pick up\" lines inside " +
         "every time. A copied block has to be fixed in every copy.",
            "Вы написали цикл for в 5 запусках — наверное, каждый раз с теми же строками «найти, дойти, подобрать» " +
            "внутри. Скопированный блок приходится исправлять в каждой копии."),
        ("function gives a name to a piece of code; call it by that name like a command. Parameters (kind) " +
         "receive the arguments of the call, and return hands a value back.",
            "function даёт имя куску кода; вызывайте его по этому имени, как команду. Параметры (kind) " +
            "получают аргументы вызова, а return возвращает значение."),
        ("Defining a function is free; each call costs 0.5 and its body is paid as usual. collect() below " +
         "costs 6 per call — still less than obtain() at 8.",
            "Объявить функцию бесплатно; каждый вызов стоит 0.5, а тело оплачивается как обычно. collect() ниже " +
            "стоит 6 за вызов — всё равно меньше, чем obtain() за 8."),
        ("Every new island starts with an empty editor: the functions you wrote last run are gone, and you " +
         "type them again.",
            "Каждый новый остров начинается с пустого редактора: функции из прошлого забега пропали, и вы " +
            "набираете их заново."),
        ("Extra code windows: press + on the main window. Each window becomes a function named after its " +
         "title, which Main can call with arguments (read them with ...). Drag a window by its header onto " +
         "the code library tab on the right to save it and reuse it in later runs.",
            "Дополнительные окна кода: нажмите + на главном окне. Каждое окно становится функцией с именем из " +
            "заголовка, и Main может вызвать её с аргументами (читайте их через ...). Перетащите окно за заголовок " +
            "на вкладку библиотеки кода справа, чтобы сохранить его и использовать в следующих забегах."),
        ("Calling a window costs the same as calling a function (0.5). Saved windows are kept between runs — " +
         "besides materials, your code is the only thing you take home.",
            "Вызов окна стоит столько же, сколько вызов функции (0.5). Сохранённые окна остаются между забегами — " +
            "кроме материалов, домой вы уносите только свой код."),

        // ─── Новые команды (LuaCommandGuide) ──────────────────
        ("obtain() and harvest() only pick things up from the ground. A tree has to be chopped first — " +
         "and now you have an axe.",
            "obtain() и harvest() только подбирают вещи с земли. Дерево сначала нужно срубить — " +
            "а теперь у вас есть топор."),
        ("mine(obj) chops or breaks an object with the right tool (tree — axe, boulder — pickaxe). " +
         "The loot falls on the ground around it: pick it up with harvest().",
            "mine(obj) рубит или разбивает объект подходящим инструментом (дерево — топор, валун — кирка). " +
            "Добыча падает на землю вокруг: подберите её через harvest()."),
        ("1 per call plus the loot: a tree costs 1 + 12. The axe wears out after 20 uses.",
            "1 за вызов плюс добыча: дерево стоит 1 + 12. Топор изнашивается за 20 использований."),
        ("Your code stopped with an error. The quickest way to find out why is to look at what your code " +
         "actually sees.",
            "Ваш код остановился с ошибкой. Быстрее всего понять почему — посмотреть, что код " +
            "видит на самом деле."),
        ("print(...) writes values to the log under the main window: numbers, text, nil, and tables with their fields.",
            "print(...) пишет значения в лог под главным окном: числа, текст, nil и таблицы с их полями."),
        ("0.5 per call. Remove debug prints once the code works — inside a loop they are paid on every pass.",
            "0.5 за вызов. Уберите отладочный вывод, когда код заработает, — внутри цикла он оплачивается на каждом проходе."),
        ("Goblins stand between you and the gold. You can sneak around them — or fight.",
            "Между вами и золотом стоят гоблины. Можно прокрасться мимо — или сражаться."),
        ("attack(enemy) hits an enemy within 1.5 tiles: 5 damage bare-handed, 15 with a sword. " +
         "It does not walk up to the enemy — use moveTo first, or wait for it to come.",
            "attack(enemy) бьёт врага в пределах 1.5 клетки: 5 урона голыми руками, 15 мечом. " +
            "К врагу команда не подходит — сначала moveTo или дождитесь, пока он подойдёт сам."),
        ("2 per hit. The real price is health: fight with a sword.",
            "2 за удар. Настоящая цена — здоровье: сражайтесь с мечом."),

        // ─── Декомпиляция (LuaDecompileGuide) ─────────────────
        ("Every obtain() cost you 8 energy — three of them, 24.", "Каждый obtain() стоил вам 8 энергии — три штуки, 24."),
        ("obtain() was never magic. Inside, it looked for the nearest visible object it could pick up " +
         "(findObjects + getNear), walked up to it (moveTo) and picked it up (harvest). " +
         "Now you can write the same steps yourself and pay only for the ones you need.",
            "В obtain() никогда не было магии. Внутри она искала ближайший видимый объект, который можно подобрать " +
            "(findObjects + getNear), подходила к нему (moveTo) и подбирала (harvest). " +
            "Теперь вы можете написать те же шаги сами и платить только за нужные."),
        ("Every autoExplore() cost you 10 energy, and it only walks a few tiles.",
            "Каждый autoExplore() стоил вам 10 энергии, а проходит он всего несколько клеток."),
        ("autoExplore() was a walk with a good memory. It compared the 8 directions, picked the one where " +
         "6 tiles of walking (move) would reveal the most land you had never seen, and stopped at water; " +
         "when everything around was already explored, it headed for the nearest edge of the fog. " +
         "check only sees tiles that are in sight, so your code can't count hidden land — but it doesn't need to: " +
         "keep walking one way while check shows land ahead, and turn when it shows water. " +
         "Every step goes into new ground, and a check plus a move costs a fraction of autoExplore().",
            "autoExplore() был прогулкой с хорошей памятью. Он сравнивал 8 направлений, выбирал то, где " +
            "6 клеток пути (move) откроют больше всего невиданной суши, и останавливался у воды; " +
            "когда вокруг всё было исследовано, шёл к ближайшему краю тумана. " +
            "check видит только клетки в поле зрения, поэтому ваш код не сосчитает скрытую сушу — но это и не нужно: " +
            "идите в одну сторону, пока check показывает впереди сушу, и поворачивайте, когда видна вода. " +
            "Каждый шаг ведёт на новую землю, а check плюс move стоят малую долю autoExplore()."),

        // ─── Ошибки Lua (LuaErrorExplainer; сырой текст ошибки не переводится) ─
        ("Line {0} in {1}", "Строка {0} в {1}"),
        ("In window {0}", "В окне {0}"),
        ("A `[[` text is not closed: it needs `]]` at the end.", "Текст `[[` не закрыт: в конце нужно `]]`."),
        ("Add `]]` where the text should end.", "Добавьте `]]` там, где текст должен закончиться."),
        ("A `--[[` comment is not closed, so all the code after it is treated as a comment.",
            "Комментарий `--[[` не закрыт, поэтому весь код после него считается комментарием."),
        ("Add `]]` where the comment should end, or use `--` for a one-line comment.",
            "Добавьте `]]` там, где комментарий должен закончиться, или используйте `--` для однострочного комментария."),
        ("A text in quotes is not closed: every \" or ' needs a matching closing quote on the same line.",
            "Текст в кавычках не закрыт: каждой \" или ' нужна такая же закрывающая кавычка в той же строке."),
        ("Add the missing quote, for example `move(\"up\", 1)`.", "Добавьте недостающую кавычку, например `move(\"up\", 1)`."),
        ("A `for` loop needs `=` (counting loop) or `in` (going through a list) after the variable name.",
            "Циклу `for` после имени переменной нужно `=` (цикл со счётчиком) или `in` (обход списка)."),
        ("Write it like `for i = 1, 10 do ... end` or `for _, obj in ipairs(list) do ... end`.",
            "Пишите так: `for i = 1, 10 do ... end` или `for _, obj in ipairs(list) do ... end`."),
        ("A number is written wrongly, for example `3x` or `1.2.3`.", "Число записано неправильно, например `3x` или `1.2.3`."),
        ("Put an operator between a number and a name: `3 * x`.", "Поставьте оператор между числом и именем: `3 * x`."),
        ("After `obj:name` Lua expects brackets with arguments.", "После `obj:name` Lua ждёт скобки с аргументами."),
        ("Add brackets: `list:getNear()`.", "Добавьте скобки: `list:getNear()`."),
        ("The {0} of the `for` loop is not a number (maybe it is nil).", "{0} цикла `for` — не число (возможно, nil)."),
        ("initial value", "Начальное значение"),
        ("limit", "Предел"),
        ("step", "Шаг"),
        ("In `for i = 1, 10, 1 do` the start, end and step must be numbers. Check the variables used there.",
            "В `for i = 1, 10, 1 do` начало, конец и шаг должны быть числами. Проверьте переменные, которые там используются."),
        ("Your energy ran out at {0}: it needed {1}, but only {2} was left. Every command call, loop pass and condition check costs energy.",
            "Энергия кончилась на {0}: нужно было {1}, а осталось только {2}. Каждый вызов команды, проход цикла и проверка условия стоят энергии."),
        ("Look at the cost next to each line and do fewer steps or loop passes. End Day restores energy for tomorrow.",
            "Посмотрите на цену рядом с каждой строкой и сделайте меньше шагов или проходов цикла. «Конец дня» восстановит энергию на завтра."),
        ("The code ran too long without a pause, so the game stopped it. Usually this is a loop that never ends, like `while true do` with nothing that stops it.",
            "Код слишком долго работал без паузы, и игра его остановила. Обычно это бесконечный цикл, например `while true do`, который ничто не останавливает."),
        ("Make sure the loop condition becomes false at some point, or leave the loop with `break`.",
            "Убедитесь, что условие цикла когда-нибудь станет ложным, или выйдите из цикла через `break`."),
        ("A function keeps calling itself (or two functions call each other) and never stops, so Lua ran out of space.",
            "Функция вызывает сама себя (или две функции вызывают друг друга) и не останавливается, поэтому у Lua кончилось место."),
        ("Add a condition that ends the calls, for example `if n <= 0 then return end` at the start of the function.",
            "Добавьте условие, которое прекращает вызовы, например `if n <= 0 then return end` в начале функции."),
        ("The command `{0}` exists, but you haven't unlocked it yet.", "Команда `{0}` существует, но вы её ещё не открыли."),
        ("Open Achievements to see which goal unlocks it. Until then, use the commands listed in Help.",
            "Откройте «Достижения», чтобы узнать, какая цель её открывает. А пока пользуйтесь командами из справки."),
        ("Extra code windows (classes) aren't unlocked yet, but the window `{0}` has code in it.",
            "Дополнительные окна кода (классы) ещё не открыты, а в окне `{0}` есть код."),
        ("Move that code into Main or clear the window. Achievements unlock class windows later.",
            "Перенесите этот код в Main или очистите окно. Окна-классы позже откроют достижения."),
        ("`goto` and `::labels::` are not part of this game: they would let the code repeat for free.",
            "`goto` и `::метки::` в этой игре недоступны: с ними код повторялся бы бесплатно."),
        ("Use a loop instead: `for`, `while` or `repeat`.", "Используйте цикл: `for`, `while` или `repeat`."),
        ("Your code uses {0}, which you haven't unlocked yet.", "В вашем коде есть {0}, а это ещё не открыто."),
        ("Rewrite this part without it (Help shows what you can use), or complete achievements to unlock it.",
            "Перепишите эту часть без неё (в справке видно, что доступно) или выполните достижения, чтобы открыть её."),
        ("Did you mean \"{0}\"? Write it like `{1}(\"{0}\")`.", "Может быть, \"{0}\"? Пишите так: `{1}(\"{0}\")`."),
        ("Type names are lowercase words, for example `{0}(\"{1}\")`.", "Имена типов — слова строчными буквами, например `{0}(\"{1}\")`."),
        ("Check the type name in Help.", "Проверьте имя типа в справке."),
        ("`{0}` doesn't know the type \"{1}\".", "`{0}` не знает тип \"{1}\"."),
        ("`{0}` needs more arguments: argument {1} is missing.", "`{0}` нужно больше аргументов: нет аргумента {1}."),
        ("Argument {0} of `{1}` has the wrong kind of value ({2}).", "Аргумент {0} у `{1}` — значение не того вида ({2})."),
        ("Check the order and the kinds of arguments in Help.", "Проверьте порядок и виды аргументов в справке."),
        ("`{0}` was called with arguments it doesn't understand.", "`{0}` вызвана с аргументами, которые она не понимает."),
        ("Correct forms: {0}", "Правильные варианты: {0}"),
        ("The list method `{0}` got a wrong argument: {1}.", "Метод списка `{0}` получил неверный аргумент: {1}."),
        ("Write it like `list:{0}`.", "Пишите так: `list:{0}`."),
        ("`{0}` couldn't run: {1}.", "`{0}` не смогла выполниться: {1}."),
        ("Check the arguments in Help.", "Проверьте аргументы в справке."),
        ("Write it like `{0}`.", "Пишите так: `{0}`."),
        ("A block is not closed: every `if`, `for`, `while` and `function` needs its own `end`.",
            "Блок не закрыт: каждому `if`, `for`, `while` и `function` нужен свой `end`."),
        (" The `{0}` on line {1} has no `end`.", " У `{0}` в строке {1} нет `end`."),
        ("Add `end` where the block should finish. Indenting the code inside a block helps to see where it ends.",
            "Добавьте `end` там, где блок должен закончиться. Отступы внутри блока помогают увидеть, где он кончается."),
        ("After the condition of `if` (or `elseif`) Lua needs the word `then`.", "После условия `if` (или `elseif`) Lua нужно слово `then`."),
        ("Write it like `if x > 3 then ... end`. To compare use `==` (equal) and `~=` (not equal), not `=` or `!=`.",
            "Пишите так: `if x > 3 then ... end`. Для сравнения используйте `==` (равно) и `~=` (не равно), а не `=` или `!=`."),
        ("After `for ...` or `while ...` Lua needs the word `do`.", "После `for ...` или `while ...` Lua нужно слово `do`."),
        ("Write it like `for i = 1, 3 do ... end` or `while x < 3 do ... end`.",
            "Пишите так: `for i = 1, 3 do ... end` или `while x < 3 do ... end`."),
        ("Lua expected `=` here.", "Здесь Lua ждала `=`."),
        ("To store a value write `name = value`. To call a command add brackets: `harvest()`.",
            "Чтобы сохранить значение, пишите `name = value`. Чтобы вызвать команду, добавьте скобки: `harvest()`."),
        ("A bracket `(` is not closed, or a comma between arguments is missing.", "Скобка `(` не закрыта, или между аргументами нет запятой."),
        ("Separate arguments with commas, like `moveTo(3, 4)`, and close every `(` with `)`.",
            "Разделяйте аргументы запятыми, как в `moveTo(3, 4)`, и закрывайте каждую `(` скобкой `)`."),
        ("A list `{` is not closed, or a comma between its items is missing.", "Список `{` не закрыт, или между элементами нет запятой."),
        ("Write it like `{1, 2, 3}`.", "Пишите так: `{1, 2, 3}`."),
        ("A `[` is not closed with `]`.", "`[` не закрыта скобкой `]`."),
        ("Write it like `list[1]`.", "Пишите так: `list[1]`."),
        ("Lua expected a comma here. A counting `for` loop needs a start and an end separated by a comma.",
            "Здесь Lua ждала запятую. Циклу `for` со счётчиком нужны начало и конец через запятую."),
        ("Write it like `for i = 1, 10 do ... end`.", "Пишите так: `for i = 1, 10 do ... end`."),
        ("A `repeat` loop needs `until condition` at the end.", "Циклу `repeat` в конце нужно `until условие`."),
        ("Write it like `repeat ... until x > 3`.", "Пишите так: `repeat ... until x > 3`."),
        ("Add `end` where the block should finish.", "Добавьте `end` там, где блок должен закончиться."),
        ("There is something extra after the code has ended: usually an extra `end`, or code after `return` / `break` in the same block.",
            "После конца кода есть что-то лишнее: обычно лишний `end` или код после `return` / `break` в том же блоке."),
        ("Remove the extra `end` (one per `if`, `for`, `while` and `function`). `return` and `break` must be the last line of their block.",
            "Уберите лишний `end` (по одному на `if`, `for`, `while` и `function`). `return` и `break` должны быть последней строкой своего блока."),
        ("Lua expected a name here: letters, digits and `_`, not starting with a digit and not a Lua word.",
            "Здесь Lua ждала имя: буквы, цифры и `_`, не с цифры в начале и не слово Lua."),
        ("For example `local count = 0` or `function Helper()` — a function needs a name.",
            "Например, `local count = 0` или `function Helper()` — функции нужно имя."),
        ("Lua expected `{0}` here.", "Здесь Lua ждала `{0}`."),
        ("Compare this line with the examples in Help.", "Сравните эту строку с примерами из справки."),
        ("Lua doesn't use `!`.", "В Lua нет `!`."),
        ("\"Not equal\" is written `~=`, and \"not\" is written `not`.", "«Не равно» пишется `~=`, а «не» — `not`."),
        ("The code ends too early: something is missing, for example after `=` or `+` (`x = 1 +`).",
            "Код обрывается слишком рано: чего-то не хватает, например после `=` или `+` (`x = 1 +`)."),
        ("Finish the expression or remove the extra operator.", "Допишите выражение или уберите лишний оператор."),
        ("Lua found {0} where it didn't expect it.", "Lua встретила {0} там, где не ожидала."),
        ("Check for a missing comma between arguments (`move(\"up\", 1)`), missing quotes around text, or an extra bracket.",
            "Проверьте, нет ли пропущенной запятой между аргументами (`move(\"up\", 1)`), кавычек вокруг текста или лишней скобки."),
        ("Lua couldn't understand this line. Often a word stands on its own: a command name without brackets, or a value that isn't stored or used.",
            "Lua не поняла эту строку. Часто слово стоит само по себе: имя команды без скобок или значение, которое никуда не сохраняется и не используется."),
        ("Commands need brackets: `harvest()`, not `harvest`. To store a value write `x = value`.",
            "Командам нужны скобки: `harvest()`, а не `harvest`. Чтобы сохранить значение, пишите `x = value`."),
        ("Your code called something that doesn't exist (it is nil).", "Код вызвал то, чего нет (это nil)."),
        ("Check the spelling of command and function names in Help.", "Проверьте написание имён команд и функций в справке."),
        ("`{0}` is not a command or function that Lua knows, so it can't be called.",
            "`{0}` — не команда и не функция, известная Lua, поэтому её нельзя вызвать."),
        ("Did you mean `{0}`? Names are case-sensitive.", "Может быть, `{0}`? Регистр букв в именах важен."),
        ("Did you mean `{0}`?", "Может быть, `{0}`?"),
        ("Check the spelling in Help. Names are case-sensitive: `moveTo`, not `moveto`.",
            "Проверьте написание в справке. Регистр букв в именах важен: `moveTo`, а не `moveto`."),
        ("This value", "Это значение"),
        ("{0} is a {1}, not a function, so it can't be called with `( )`.", "{0} — это {1}, а не функция, его нельзя вызвать через `( )`."),
        ("Maybe a variable has the same name as a command or function — give the variable another name.",
            "Возможно, переменная называется так же, как команда или функция, — дайте переменной другое имя."),
        ("`{0}` is nil (empty)", "`{0}` — nil (пусто)"),
        ("A value is nil (empty)", "Значение — nil (пусто)"),
        ("{0}, so you can't use `.` or `:` on it. This often happens when a command found nothing: `getNear()`, `find()` or `check()` returned nil.",
            "{0}, поэтому к нему нельзя применить `.` или `:`. Так часто бывает, когда команда ничего не нашла: `getNear()`, `find()` или `check()` вернули nil."),
        ("Make sure something is in sight before you look for it (`findObjects(...).count` shows how many), or explore first. Conditions (`if`) unlock after your first harvest.",
            "Убедитесь, что искомое в поле зрения (`findObjects(...).count` покажет, сколько его), или сначала исследуйте. Условия (`if`) откроются после первого сбора."),
        ("Check it before use: `if {0} then ... end`. Also check the spelling of the variable.",
            "Проверьте перед использованием: `if {0} then ... end`. И проверьте написание переменной."),
        ("Check the value before use: `if obj then ... end`.", "Проверьте значение перед использованием: `if obj then ... end`."),
        ("{0} is a {1}, not a table, so it has no fields to read with `.`.", "{0} — это {1}, а не таблица, у него нет полей для `.`."),
        ("Fields of found objects are plain values: write `obj.x`, not `obj.x.y`.",
            "Поля найденных объектов — простые значения: пишите `obj.x`, а не `obj.x.y`."),
        ("You did math with {0} that is nil (has no value). Maybe the name is misspelled, it was never set, or a command returned nothing.",
            "Вы считали с {0}, а это nil (нет значения). Возможно, имя написано с ошибкой, значение не задано или команда ничего не вернула."),
        ("a value", "значением"),
        ("Give it a value first (`local count = 0`) or check it: `if x then ... end`.",
            "Сначала задайте значение (`local count = 0`) или проверьте его: `if x then ... end`."),
        ("You did math with text ({0}). Numbers in quotes are text: \"5\" is not 5.",
            "Вы считали с текстом ({0}). Число в кавычках — это текст: \"5\" не то же, что 5."),
        ("You did math with text. Numbers in quotes are text: \"5\" is not 5.",
            "Вы считали с текстом. Число в кавычках — это текст: \"5\" не то же, что 5."),
        ("Remove the quotes or convert the text: `tonumber(\"5\")`.", "Уберите кавычки или преобразуйте текст: `tonumber(\"5\")`."),
        ("You did math with a {0} value ({1}). Only numbers can be added, subtracted or multiplied.",
            "Вы считали со значением типа {0} ({1}). Складывать, вычитать и умножать можно только числа."),
        ("You did math with a {0} value. Only numbers can be added, subtracted or multiplied.",
            "Вы считали со значением типа {0}. Складывать, вычитать и умножать можно только числа."),
        ("If it is a found object, use its number field: `obj.x`, `obj.distance`.",
            "Если это найденный объект, используйте его числовое поле: `obj.x`, `obj.distance`."),
        ("`#` gives the length of a list or text, but this value is a {0}.", "`#` даёт длину списка или текста, а это значение — {0}."),
        ("Check that the list exists. For findObjects results you can also use `list.count`.",
            "Проверьте, что список существует. Для результата findObjects можно использовать и `list.count`."),
        ("One side of the comparison (`<`, `>`, `<=`, `>=`) is nil — it has no value, so Lua can't compare it.",
            "Одна сторона сравнения (`<`, `>`, `<=`, `>=`) — nil, у неё нет значения, и Lua не может сравнить."),
        ("Maybe a name is misspelled or a command returned nil. Check first: `if x and x > 3 then ... end`.",
            "Возможно, имя написано с ошибкой или команда вернула nil. Сначала проверьте: `if x and x > 3 then ... end`."),
        ("You compared a {0} with a {1}. `<` and `>` work only for two numbers (or two texts).",
            "Вы сравнили {0} с {1}. `<` и `>` работают только для двух чисел (или двух текстов)."),
        ("Compare a field instead, for example `obj.distance < 5`.", "Сравните лучше поле, например `obj.distance < 5`."),
        ("`..` joins texts, but one of the parts is nil (has no value).", "`..` склеивает тексты, но одна из частей — nil (нет значения)."),
        ("Wrap the value in `tostring(...)`, for example `\"x = \" .. tostring(x)`.",
            "Оберните значение в `tostring(...)`, например `\"x = \" .. tostring(x)`."),
        ("`..` joins texts and numbers, but one of the parts is a {0}.", "`..` склеивает тексты и числа, но одна из частей — {0}."),
        ("Join a field instead, for example `\"found \" .. obj.type`.", "Склейте лучше поле, например `\"found \" .. obj.type`."),
    };
}
