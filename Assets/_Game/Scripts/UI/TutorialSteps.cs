using System;
using System.Collections.Generic;

/// <summary>Где идёт шаг туториала: экран лагеря или забег.</summary>
public enum TutorialMode
{
    Hub,
    Run
}

/// <summary>
/// Элемент UI, к которому привязана подсказка (рамка + стрелка). Элемент по ключу находит CodeEditorManager.
/// </summary>
public enum TutorialAnchor
{
    None,
    StartRunButton,
    HubAchievementsButton,
    MainWindow,
    LineCosts,
    RunButton,
    Energy,
    Inventory,
    DayLabel,
    HelpButton,
    CraftButton,
    EndDayButton,
    AchievementsButton,
    ExitRunButton,
    AddWindowButton,
    LibraryTab,
    ThreatLabel,
    /// <summary>Панель построек лагеря (этап 4).</summary>
    HubBuildPanel
}

/// <summary>
/// Факты сцены — то, что уже случилось (события UI, GameEventBus, запуски кода). Шаги ждут их в Done.
/// </summary>
public static class TutorialFacts
{
    public const string StartRunRequested = "start_run";
    public const string RunStarted = "run_started";
    public const string RunFinished = "run_finished";
    public const string HelpOpened = "help_opened";
    public const string CraftingOpened = "crafting_opened";
    public const string LibraryOpened = "library_opened";
    public const string AchievementsOpened = "achievements_opened";
    public const string WindowSaved = "window_saved";
    public const string AuxWindowCreated = "aux_window";
    public const string Mined = "mined";
    public const string EnemyFound = "enemy_found";
    /// <summary>В лагере поставлена постройка (GameEventBus HubBuildingBuilt).</summary>
    public const string HubBuilt = "hub_built";

    /// <summary>Конструкция была в коде запуска (GameEventBus ConstructUsed), id — из LuaConstructs.</summary>
    public static string ConstructUsed(string constructId) => "construct:" + constructId;

    /// <summary>Скрафчен предмет (ключ результата в нижнем регистре).</summary>
    public static string Crafted(string itemKey) => "crafted:" + itemKey?.ToLowerInvariant();
}

/// <summary>
/// Шаг UI-гайда (GameDesign §10.5): элемент UI, текст, условие перехода.
/// Текст — английский; `код` в обратных кавычках подсвечивается как код, {runs} — запусков в день.
/// </summary>
public sealed class TutorialStep
{
    public string Id;
    public TutorialMode Mode;
    public TutorialAnchor Anchor;
    public string Text;
    /// <summary>Шаг показывается только после этого шага (null — без зависимости).</summary>
    public string After;
    /// <summary>Шаг можно показать (открыта команда, наступил день…). null — всегда.</summary>
    public Func<TutorialState, bool> Requires;
    /// <summary>Условие перехода. null — шаг закрывается кнопкой Next.</summary>
    public Func<TutorialState, bool> Done;
    /// <summary>Шаг больше не актуален (например, день прошёл) — отмечается пройденным без показа.</summary>
    public Func<TutorialState, bool> Skip;
    /// <summary>Done проверяется только у текущего шага (факты HasSince — после того, как шаг стал текущим).</summary>
    public bool CurrentOnly;
    /// <summary>Кнопка Next есть и у шага с условием.</summary>
    public bool AllowNext;
    /// <summary>Показывать и во время выполнения кода (остальные шаги ждут конца запуска).</summary>
    public bool ShowWhileRunning;
    /// <summary>Понятие, которое объясняет этот шаг: отдельная подсказка о нём уже не нужна.</summary>
    public string CoversConcept;
    /// <summary>Строка под текстом, пока шаг ждёт условия.</summary>
    public string Waiting;

    public bool HasNext => Done == null || AllowNext;
}

/// <summary>
/// Понятие программирования (вызов функции, аргументы, переменная, условие, цикл, функция):
/// подсказка при первом использовании в коде. Понятия, которые подробно объясняет UnlockPopup
/// при открытии (циклы, функции), — коротко.
/// </summary>
public sealed class TutorialConcept
{
    public string Id;
    public string Title;
    public string Text;
    /// <summary>Пример кода (подсвечивается), может быть пустым.</summary>
    public string Example;
    public Func<TutorialState, bool> Trigger;
}

/// <summary>
/// Шаги туториала и подсказки-понятия. Порядок в списке — приоритет: показывается первый непройденный
/// шаг, у которого выполнены After и Requires. Поэтому шаги «по времени» (день 2, угроза, окна-классы)
/// стоят раньше шагов, которые могут долго ждать события (крафт топора).
/// </summary>
public static class TutorialSteps
{
    // ─── Id понятий ───────────────────────────────────────────

    public const string ConceptCall = "call";
    public const string ConceptArguments = "arguments";
    public const string ConceptVariable = "variable";
    public const string ConceptCondition = "condition";
    public const string ConceptLoop = "loop";
    public const string ConceptFunction = "function";

    // ─── Шаги ─────────────────────────────────────────────────

    public static readonly IReadOnlyList<TutorialStep> Steps = new List<TutorialStep>
    {
        // ─── Лагерь ───
        new TutorialStep
        {
            Id = "hub_achievements", Mode = TutorialMode.Hub, Anchor = TutorialAnchor.HubAchievementsButton,
            Text = "Achievements unlock new commands and language features. Take a look at what to aim for.",
            Done = s => s.Has(TutorialFacts.AchievementsOpened), AllowNext = true,
            Waiting = "Open Achievements or press Next"
        },
        // Только когда есть что строить (открыто достижением и хватает материалов) — в первом лагере не мешает
        new TutorialStep
        {
            Id = "hub_build", Mode = TutorialMode.Hub, Anchor = TutorialAnchor.HubBuildPanel,
            Text = "Spend materials from your runs on camp buildings: each one makes every next run stronger. " +
                   "Press Build, then click a free cell on the grid.",
            Requires = s => s.CanBuildInHub,
            Done = s => s.Has(TutorialFacts.HubBuilt) || s.HasHubBuilding, AllowNext = true,
            Waiting = "Build something or press Next"
        },
        new TutorialStep
        {
            Id = "hub_start", Mode = TutorialMode.Hub, Anchor = TutorialAnchor.StartRunButton,
            Text = "Welcome to camp. Start a run to explore a new island.",
            Done = s => s.Has(TutorialFacts.StartRunRequested),
            Waiting = "Press Start Run"
        },

        // ─── Забег: основная цепочка ───
        new TutorialStep
        {
            Id = "code_window", Mode = TutorialMode.Run, Anchor = TutorialAnchor.MainWindow,
            Text = "You can't move the hero directly. Write Lua code here — the hero does what the code says."
        },
        new TutorialStep
        {
            Id = "type_obtain", Mode = TutorialMode.Run, Anchor = TutorialAnchor.MainWindow, After = "code_window",
            Text = "Click into the window and type `obtain()` — this is a command call. " +
                   "It walks to the nearest item and picks it up.",
            Done = s => s.MainCodeCalls("obtain"),
            CoversConcept = ConceptCall,
            Waiting = "Type obtain() in the Main window"
        },
        new TutorialStep
        {
            Id = "line_costs", Mode = TutorialMode.Run, Anchor = TutorialAnchor.LineCosts, After = "type_obtain",
            Text = "This number is the energy cost of the line. Ready-made commands like `obtain` " +
                   "are convenient but expensive."
        },
        new TutorialStep
        {
            Id = "press_run", Mode = TutorialMode.Run, Anchor = TutorialAnchor.RunButton, After = "line_costs",
            Text = "Press Run to execute your code. Press it again (Stop) to interrupt.",
            Done = s => s.Has(TutorialFacts.RunStarted),
            Waiting = "Press Run"
        },
        new TutorialStep
        {
            Id = "energy", Mode = TutorialMode.Run, Anchor = TutorialAnchor.Energy, After = "press_run",
            Text = "Energy is your daily budget. Every command spends it, even failed ones.",
            Done = s => s.HasSince(TutorialFacts.RunFinished), CurrentOnly = true, ShowWhileRunning = true,
            Waiting = "Wait for the code to finish"
        },
        new TutorialStep
        {
            Id = "inventory", Mode = TutorialMode.Run, Anchor = TutorialAnchor.Inventory, After = "energy",
            Text = "Gathered items go here. Gather 3 items to unlock cheaper commands and conditions.",
            Done = s => s.HasAchievement("first_harvest"),
            Waiting = "Gather 3 items — run obtain() a few times"
        },
        new TutorialStep
        {
            Id = "arguments", Mode = TutorialMode.Run, Anchor = TutorialAnchor.MainWindow, After = "inventory",
            Text = "Arguments go inside the brackets: `obtain(\"stick\")` picks up only sticks. " +
                   "Text values are written in quotes.",
            CoversConcept = ConceptArguments
        },
        new TutorialStep
        {
            Id = "runs_per_day", Mode = TutorialMode.Run, Anchor = TutorialAnchor.DayLabel, After = "arguments",
            Text = "You have {runs} runs per day. After the last one the day ends and energy refills."
        },
        new TutorialStep
        {
            Id = "help", Mode = TutorialMode.Run, Anchor = TutorialAnchor.HelpButton, After = "runs_per_day",
            Text = "Help lists every command you have and its cost.",
            Done = s => s.Has(TutorialFacts.HelpOpened),
            Waiting = "Open Help"
        },

        // ─── Забег: шаги «по времени» — раньше длинных ожиданий ───
        new TutorialStep
        {
            Id = "exit_run", Mode = TutorialMode.Run, Anchor = TutorialAnchor.ExitRunButton, After = "energy",
            Requires = s => s.Day >= 2,
            Text = "Return to camp at any time. Only materials are kept — and nothing if you die."
        },
        new TutorialStep
        {
            Id = "threat", Mode = TutorialMode.Run, Anchor = TutorialAnchor.ThreatLabel, After = "energy",
            Requires = s => s.Day == s.ThreatDay - 1,
            Skip = s => s.Day >= s.ThreatDay || s.Has(TutorialFacts.EnemyFound),
            Text = "Goblins will patrol from tomorrow. Use `findObjects(\"enemy\")` to see them before they see you."
        },
        new TutorialStep
        {
            Id = "windows", Mode = TutorialMode.Run, Anchor = TutorialAnchor.AddWindowButton, After = "energy",
            Requires = s => s.IsConstructUnlocked(LuaConstructs.Classes),
            Skip = s => s.Has(TutorialFacts.WindowSaved),
            Text = "Extra windows become functions named after the title. " +
                   "Drag a window by its header onto the library tab on the right to save it."
        },

        // ─── Забег: после открытий ───
        new TutorialStep
        {
            Id = "variable", Mode = TutorialMode.Run, Anchor = TutorialAnchor.MainWindow, After = "help",
            Requires = s => s.IsCommandUnlocked("findObjects"),
            Text = "`local items = findObjects(\"stick\")` stores a list in a variable. " +
                   "Reuse it instead of searching again — it saves energy.",
            Done = s => s.Has(TutorialFacts.ConstructUsed(LuaConstructs.Local)),
            CoversConcept = ConceptVariable,
            Waiting = "Run code that uses local"
        },
        new TutorialStep
        {
            Id = "end_day", Mode = TutorialMode.Run, Anchor = TutorialAnchor.EndDayButton, After = "help",
            Text = "End the day early to refill energy. Ending with less than 1 energy lowers tomorrow's limit."
        },
        new TutorialStep
        {
            Id = "achievements", Mode = TutorialMode.Run, Anchor = TutorialAnchor.AchievementsButton, After = "help",
            Text = "Achievements unlock new commands and language features.",
            Done = s => s.Has(TutorialFacts.AchievementsOpened),
            Waiting = "Open Achievements"
        },
        new TutorialStep
        {
            Id = "crafting", Mode = TutorialMode.Run, Anchor = TutorialAnchor.CraftButton, After = "help",
            Text = "Recipes are listed here. Crafting is done in code: `craft(\"flint\")`, then `craft(\"axe\")`.",
            Done = s => s.HasAchievement("first_axe") || s.Has(TutorialFacts.Crafted("axe")),
            Waiting = "Craft an axe with code"
        },
        new TutorialStep
        {
            Id = "mine", Mode = TutorialMode.Run, Anchor = TutorialAnchor.MainWindow, After = "crafting",
            Requires = s => s.IsCommandUnlocked("mine"),
            Text = "Trees need an axe: `mine(tree)` chops it and drops logs on the ground. " +
                   "Pick them up with `harvest`.",
            Done = s => s.Has(TutorialFacts.Mined),
            Waiting = "Chop a tree with mine()"
        }
    };

    // ─── Понятия ──────────────────────────────────────────────

    public static readonly IReadOnlyList<TutorialConcept> Concepts = new List<TutorialConcept>
    {
        new TutorialConcept
        {
            Id = ConceptCall, Title = "Function call",
            Text = "`obtain()` is a function call: the name says what to do, the brackets `()` make it happen. " +
                   "Lines run from top to bottom, one call after another. " +
                   "Every call costs energy — see the number to the right of the line.",
            Trigger = s => s.CodeHasCall
        },
        new TutorialConcept
        {
            Id = ConceptArguments, Title = "Arguments",
            Text = "Values inside the brackets are arguments — they tell the command what exactly to do. " +
                   "Text goes in quotes: `obtain(\"stick\")`. Several arguments are separated by commas: " +
                   "`moveTo(x, y)`.",
            Trigger = s => s.CodeHasArguments
        },
        new TutorialConcept
        {
            Id = ConceptVariable, Title = "Variable",
            Text = GuideText(LuaConstructs.Local),
            Example = GuideExample(LuaConstructs.Local),
            Trigger = s => s.CodeHasKeyword("local")
        },
        new TutorialConcept
        {
            Id = ConceptCondition, Title = "Condition",
            // Объяснение уже говорит, что nil и false — это «нет»
            Text = GuideText(LuaConstructs.If),
            Example = GuideExample(LuaConstructs.If),
            // if больше не стартовый (открывается за first_harvest): понятие — только после открытия,
            // при первом использовании, как циклы и функции
            Trigger = s => s.IsConstructUnlocked(LuaConstructs.If) && s.CodeHasKeyword("if")
        },
        // Циклы и функции подробно объясняет UnlockPopup при открытии — здесь коротко, при первом использовании
        new TutorialConcept
        {
            Id = ConceptLoop, Title = "Loop",
            Text = "A loop repeats its block. The cost on the line is for one pass — every iteration pays " +
                   "again for everything inside, so keep the body cheap and make sure the loop ends.",
            Trigger = s => s.CodeHasKeyword("for") || s.CodeHasKeyword("while") || s.CodeHasKeyword("repeat")
        },
        new TutorialConcept
        {
            Id = ConceptFunction, Title = "Function",
            Text = "A function is a named piece of code: define it once, call it by name like a command. " +
                   "Names in its brackets are parameters — they receive the arguments of the call. " +
                   "The body is paid on every call.",
            Trigger = s => s.CodeHasKeyword("function")
        }
    };

    // ─── Helpers ──────────────────────────────────────────────

    private static string GuideText(string constructId) =>
        LuaConstructGuide.TryGet(constructId, out LuaConstructGuide.Entry entry) ? entry.Explanation : string.Empty;

    private static string GuideExample(string constructId) =>
        LuaConstructGuide.TryGet(constructId, out LuaConstructGuide.Entry entry) ? entry.Example : null;
}
