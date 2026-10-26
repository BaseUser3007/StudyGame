using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Lua;
using Lua.Standard;
using UnityEngine;

public class LuaSandbox : MonoBehaviour
{
    /// <summary>
    /// Сообщение для лога под главным окном: вывод print(...) (LogEntryType.Info). Вызывается
    /// в главном потоке во время выполнения кода, текст не длиннее PrintCommand.MaxLength.
    /// </summary>
    public event Action<string, LogEntryType> OnLogMessage;
    public event Action<bool> OnRunningChanged;

    /// <summary>
    /// Открытия применены: в реестре появились команды и/или открылась конструкция языка.
    /// UI перечитывает справку, подсветку, автодополнение и цены строк. Во время выполнения кода
    /// новые команды подключаются после его окончания — тогда же приходит событие.
    /// </summary>
    public event Action OnUnlocksApplied;

    /// <summary>
    /// Выполнение перешло на другую строку: окно (заголовок; главное — <see cref="MainChunkName"/>) и номер строки
    /// в нём; (null, 0) — запуск закончился. Приходит на каждом вызове команды / проверке / итерации —
    /// UI должен сам ограничивать частоту перерисовки.
    /// </summary>
    public event Action<string, int> OnExecutingLineChanged;

    /// <summary>Точки останова и пошаговое выполнение (UI задаёт BreakpointProvider и жмёт Continue/Step).</summary>
    public LuaDebugger Debugger => _debugger;

    /// <summary>Код стоит на точке останова — мир (враги) тоже стоит.</summary>
    public bool IsPaused => _debugger != null && _debugger.IsPaused;

    /// <summary>Имя главного окна в сообщениях об ошибках и в чанке Lua.</summary>
    public const string MainChunkName = "Main";

    [Tooltip("Если главный поток заблокирован Lua-кодом дольше этого времени, выполнение прерывается")]
    [SerializeField] private float freezeTimeoutSeconds = 3f;

    private PlayerCommandExecutor _executor;
    private IMapDataProvider _mapData;
    private IFogProvider _fog;
    private EnergyConfig _energyConfig;
    private ObjectManager _objectManager;
    private PlayerEnergy _playerEnergy;
    private PlayerHealth _playerHealth;
    private PlayerInventory _inventory;
    private RecipeBook _recipeBook;
    private StructureManager _structures;
    private float _buildReach;
    private DayCycle _dayCycle;
    private UnlockRegistry _unlocks;
    private EnemyManager _enemies;
    private CombatService _combat;
    private PuzzleManager _puzzles;

    private LuaState _state;
    // Все команды игры; в _registry (и в Lua) попадают только открытые
    private readonly List<ILuaCommand> _allCommands = new List<ILuaCommand>();
    private LuaCommandRegistry _registry;
    private LuaConstructGuard _constructGuard;
    private bool _unlocksPending;
    private EnergyCounter _energyCounter;
    private EnergyInstrumenter _instrumenter;
    private EnergyMeter _meter;
    private LuaLineTracker _lineTracker;
    private LuaDebugger _debugger;
    private CancellationTokenSource _cts;

    // Watchdog: метка последнего кадра главного потока (Stopwatch ticks)
    private long _lastFrameTimestamp;
    private Timer _watchdog;
    private volatile bool _timedOut;

    // Расход энергии на старте текущего запуска (getStatus().spent) и текст ошибки заглушки
    // закрытой команды, вызванной в этом запуске (итог "locked")
    private float _spentAtRunStart;
    private string _lockedCommandError;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Энергия, потраченная с начала текущего запуска кода (всё, что списано с PlayerEnergy);
    /// 0, если код не выполняется. То же значение уйдёт в CodeRunReport.EnergySpent.
    /// </summary>
    public float EnergySpentThisRun => IsRunning && _playerEnergy != null
        ? _playerEnergy.TotalSpent - _spentAtRunStart
        : 0f;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        IFogProvider fog,
        EnergyConfig energyConfig,
        ObjectManager objectManager,
        PlayerEnergy playerEnergy,
        PlayerHealth playerHealth,
        PlayerInventory inventory,
        RecipeBook recipeBook,
        StructureManager structures,
        float buildReach,
        DayCycle dayCycle,
        UnlockRegistry unlocks,
        EnemyManager enemies,
        CombatService combat,
        PuzzleManager puzzles = null)
    {
        _executor = executor;
        _mapData = mapData;
        _fog = fog;
        _energyConfig = energyConfig;
        _objectManager = objectManager;
        _playerEnergy = playerEnergy;
        _playerHealth = playerHealth;
        _inventory = inventory;
        _recipeBook = recipeBook;
        _structures = structures;
        _buildReach = buildReach;
        _dayCycle = dayCycle;
        _unlocks = unlocks;
        // Враги и бой могут отсутствовать (тесты без сцены) — команды тогда «никого не видят»
        _enemies = enemies;
        _combat = combat;
        // Головоломок нет в лагере (пустой менеджер) и в тестах без сцены (null) — команды «ничего не видят»
        _puzzles = puzzles;
        UnityEngine.Debug.Assert(_unlocks != null, "LuaSandbox: UnlockRegistry is not assigned");

        _registry = new LuaCommandRegistry();
        _constructGuard = new LuaConstructGuard(_unlocks);

        RegisterCommands();
        ValidateStartingCommands();

        var commandNames = new List<string>();
        foreach (ILuaCommand command in _allCommands)
            commandNames.Add(command.Name);
        _unlocks.SetKnownCommands(commandNames);

        _energyCounter = new EnergyCounter(_energyConfig, _registry);
        _instrumenter = new EnergyInstrumenter(_energyConfig);
        _meter = new EnergyMeter(_playerEnergy);
        _meter.OnExhausted += HandleEnergyExhausted;
        _lineTracker = new LuaLineTracker(MainChunkName);
        _lineTracker.OnLineChanged += HandleExecutingLineChanged;
        _meter.LineTracker = _lineTracker;
        _debugger = new LuaDebugger();
        _meter.Debugger = _debugger;

        CreateLuaState();

        _unlocks.OnUnlocked += HandleUnlocked;
    }

    private void RegisterCommands()
    {
        // Движение
        AddCommand(new MoveCommand(_executor, _mapData));
        AddCommand(new MoveToCommand(_executor, _mapData));
        AddCommand(new GetPositionCommand(_executor, _mapData));

        // Авто команды
        AddCommand(new AutoExploreCommand(_executor, _mapData, _fog, _objectManager, _enemies, _puzzles));

        // Объекты
        var harvester = new ResourceHarvester(_executor, _mapData, _objectManager, _playerEnergy, _inventory);
        AddCommand(new FindObjectsCommand(_executor, _mapData, _objectManager, _enemies, _fog, _puzzles));
        AddCommand(new CheckCommand(_executor, _mapData, _fog, _objectManager, _structures, _enemies, _puzzles));
        AddCommand(new HarvestCommand(_executor, _mapData, harvester, _objectManager, _enemies, _puzzles));
        AddCommand(new MineCommand(_executor, _mapData, harvester, _objectManager, _enemies, _puzzles));

        // Бой (удар — CombatService, общий с будущими готовыми командами)
        AddCommand(new AttackCommand(_executor, _mapData, _enemies, _combat, _puzzles));

        // Готовые команды: удобнее, но дороже самописного аналога
        AddCommand(new ObtainCommand(_executor, _mapData, _objectManager, harvester));

        // Крафт
        AddCommand(new CraftCommand(_recipeBook, _playerEnergy, _inventory, _structures, _unlocks));

        // Постройки
        AddCommand(new BuildCommand(_executor, _mapData, _inventory, _structures, _buildReach));

        // Инвентарь
        AddCommand(new DropCommand(_executor, _mapData, _inventory, _objectManager));
        AddCommand(new GetInventoryCommand(_inventory));

        // Еда: энергия и здоровье (не выше максимума)
        AddCommand(new EatCommand(_playerEnergy, _playerHealth, _inventory));

        // Состояние дня, энергии и здоровья
        AddCommand(new GetStatusCommand(_playerEnergy, _playerHealth, _dayCycle, GetEnergySpentThisRun));

        // Отладка: вывод в лог под главным окном (заменяет стандартный print); открывается first_bug
        AddCommand(new PrintCommand(PublishPrint));

        // Головоломки (этап 6): условие и ответ; открываются с событием карты code_task (по числу достижений)
        AddCommand(new PuzzleCommand(_executor, _mapData, _puzzles));
        AddCommand(new SolveCommand(_puzzles));

        // Добавляй новые команды здесь ↓ (открытость задаётся UnlockConfig / MetaProgress)
    }

    private float GetEnergySpentThisRun() => EnergySpentThisRun;

    private void PublishPrint(string text)
    {
        OnLogMessage?.Invoke(text, LogEntryType.Info);
    }

    /// <summary>
    /// Команда известна игре; в реестр (а значит, в Lua, справку, подсветку, автодополнение
    /// и EnergyCounter) она попадает, только если открыта.
    /// </summary>
    private void AddCommand(ILuaCommand command)
    {
        _allCommands.Add(command);
        if (_unlocks.IsCommandUnlocked(command.Name))
            _registry.RegisterCommand(command);
    }

    /// <summary>
    /// Опечатка в UnlockConfig молча закрыла бы команду — предупреждаем.
    /// </summary>
    private void ValidateStartingCommands()
    {
        foreach (string name in _unlocks.StartingCommands)
            if (FindCommand(name) == null)
                UnityEngine.Debug.LogWarning($"LuaSandbox: UnlockConfig lists unknown command '{name}'");
    }

    private ILuaCommand FindCommand(string name)
    {
        foreach (ILuaCommand command in _allCommands)
            if (command.Name == name) return command;
        return null;
    }

    private void CreateLuaState()
    {
        _state = LuaState.Create();
        _state.OpenStandardLibraries();
        RestrictStandardLibraries();
        _registry.RegisterAll(_state, _meter);

        // Закрытые команды — заглушки с понятной ошибкой вместо "attempt to call a nil value".
        // В реестр они не входят, энергию не тратят (сразу ошибка)
        foreach (ILuaCommand command in _allCommands)
            if (!_registry.HasCommand(command.Name))
                _state.Environment[command.Name] = CreateLockedStub(command.Name);

        // Списание за выполненные конструкции языка — вызовы расставляет EnergyInstrumenter.
        // Возвращает true, чтобы `__energy(n) and cond` сохраняло значение условия
        _state.Environment[EnergyInstrumenter.ChargeFunction] = new LuaFunction(async (context, ct) =>
        {
            double amount = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out double n) ? n : 0d;
            // Подсветка строки и точки останова; без паузы завершается сразу
            await _meter.CheckpointAsync(context.State, ct);
            _meter.Charge((float)amount, "code");
            context.Return(true);
            return 1;
        });

        // Всё, что есть в окружении до кода игрока, на паузе не показывается как его переменные
        _debugger.GlobalsBaseline = LuaFrameInspector.SnapshotNames(_state.Environment);
    }

    /// <summary>
    /// Энергия кончилась — останавливаем программу, даже если ошибку поймал pcall.
    /// </summary>
    private void HandleEnergyExhausted()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    // Глобалы стандартной библиотеки, недоступные игроку:
    // load/loadfile/dofile/require выполняют код в обход LuaConstructGuard и EnergyInstrumenter,
    // io/os могут менять файлы сохранений и закрыть игру, debug/package/collectgarbage — служебные.
    // Стандартный print писал в консоль Unity бесплатно — его место занимает PrintCommand (или её заглушка)
    private static readonly string[] BlockedGlobals =
    {
        "load", "loadfile", "dofile", "require", "package", "io", "debug", "collectgarbage", "print"
    };

    // Из os остаются только безопасные функции времени
    private static readonly string[] AllowedOsFunctions = { "clock", "time", "date" };

    private void RestrictStandardLibraries()
    {
        LuaTable env = _state.Environment;

        LuaTable safeOs = new LuaTable();
        if (env["os"].TryRead(out LuaTable os))
        {
            foreach (string name in AllowedOsFunctions)
            {
                LuaValue function = os[name];
                if (function.Type != LuaValueType.Nil)
                    safeOs[name] = function;
            }
        }

        foreach (string name in BlockedGlobals)
            env[name] = LuaValue.Nil;
        env["os"] = safeOs;
    }

    private LuaFunction CreateLockedStub(string name)
    {
        string message = $"command `{name}` is not unlocked yet";
        return new LuaFunction((context, ct) =>
        {
            // Запоминаем: если ошибка дойдёт до верха (не поймана pcall), итог запуска — "locked"
            _lockedCommandError = message;
            // Строка вызова — для места ошибки, если у исключения не окажется стека Lua
            _lineTracker.Probe(context.State);
            throw new InvalidOperationException(message);
        });
    }

    // ─── Открытия ─────────────────────────────────────────────

    /// <summary>
    /// Открытие посреди забега (достижение): новые команды подключаются сразу, если код не выполняется,
    /// иначе — когда запуск закончится. Конструкции проверяются при каждом запуске, их применять не нужно.
    /// </summary>
    private void HandleUnlocked(UnlockKind kind, string id)
    {
        if (IsRunning)
            _unlocksPending = true;
        else
            ApplyUnlocks();
    }

    private void ApplyUnlocks()
    {
        _unlocksPending = false;

        foreach (ILuaCommand command in _allCommands)
        {
            if (_registry.HasCommand(command.Name) || !_unlocks.IsCommandUnlocked(command.Name)) continue;

            // Регистрация заменяет заглушку; списание за вызов и Meter для возвращаемых функций — как у всех
            _registry.RegisterCommand(command);
            command.Register(_state, _meter);
        }

        OnUnlocksApplied?.Invoke();
    }

    private void OnEnable()
    {
        MarkFrame();
#if UNITY_EDITOR
        // Редактор на паузе не вызывает Update, но EditorApplication.update продолжает работать
        UnityEditor.EditorApplication.update += MarkFrame;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= MarkFrame;
#endif
    }

    private void Update()
    {
        MarkFrame();
    }

    private void OnDestroy()
    {
        if (_unlocks != null)
            _unlocks.OnUnlocked -= HandleUnlocked;
        if (_lineTracker != null)
            _lineTracker.OnLineChanged -= HandleExecutingLineChanged;

        StopWatchdog();
        // finally старого запуска сравнивает _cts со своим: после уничтожения он ничего не публикует
        // (RunFinished ушёл бы в шину уже новой сцены). Обнуляем до Cancel: отмена паузы на точке
        // останова может довыполнить запуск (и его finally) прямо внутри Cancel()
        CancellationTokenSource cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private void HandleExecutingLineChanged(string chunk, int line)
    {
        OnExecutingLineChanged?.Invoke(chunk, line);
    }

    // ─── Public API ───────────────────────────────────────────

    public float CalculateEnergyCost(Dictionary<string, string> windowCode)
    {
        float total = 0f;
        foreach (var kvp in windowCode)
            total += _energyCounter.CalculateTotalCost(kvp.Value);
        return total;
    }

    /// <summary>
    /// Стоимость каждой строки кода (ключ — номер строки, начиная с 1).
    /// </summary>
    public Dictionary<int, float> CalculateLineCosts(string code)
    {
        return _energyCounter.CalculateLineCosts(code);
    }

    public async UniTask<ExecutionResult> ExecuteAsync(
        string mainCode,
        Dictionary<string, string> auxiliaryWindows)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationTokenSource cts = _cts;
        CancellationToken ct = cts.Token;

        // Открытое во время прошлого запуска подключаем до нового
        if (_unlocksPending) ApplyUnlocks();

        // Для достижений: расход энергии за запуск и конструкции в коде (собирает LuaConstructGuard)
        float spentAtStart = _playerEnergy.TotalSpent;
        _spentAtRunStart = spentAtStart;
        _lockedCommandError = null;
        var usedConstructs = new HashSet<string>(StringComparer.Ordinal);
        bool started = false;
        bool succeeded = false;
        // Tag события RunFinished (GameEventBus.RunTag*), уточняется ниже по итогу
        string outcome = GameEventBus.RunTagError;

        SetRunning(true);
        StartWatchdog(cts);
        _meter.Reset();
        _lineTracker.Clear();
        _debugger.Reset();
        GameEventBus.PublishRunStarted();

        try
        {
            mainCode ??= string.Empty;
            var chunks = new List<(string Name, string Code)>();
            foreach (var kvp in auxiliaryWindows)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                chunks.Add((kvp.Key, $"function {kvp.Key}(...)\n{kvp.Value}\nend"));
            }
            chunks.Add((MainChunkName, mainCode));

            // 1. Синтаксис — по исходному коду всех окон, до выполнения чего-либо
            foreach (var chunk in chunks)
                _state.Load(chunk.Code.AsSpan(), chunk.Name, null);

            // 2. Закрытые конструкции и окна-«классы»: отказ без траты энергии
            //    (запуск дня засчитывается, как и при ошибке компиляции)
            string locked = _constructGuard.Check(mainCode, auxiliaryWindows, MainChunkName, usedConstructs);
            if (locked != null)
            {
                outcome = GameEventBus.RunTagLocked;
                return ExecutionResult.RuntimeError(locked);
            }

            // 3. Выполнение кода, размеченного списаниями энергии
            started = true;
            foreach (var chunk in chunks)
                await RunChunkAsync(chunk.Code, chunk.Name, ct);

            succeeded = true;
            outcome = GameEventBus.RunTagOk;
            return ExecutionResult.Success();
        }
        catch (OperationCanceledException)
        {
            outcome = _meter.IsExhausted ? GameEventBus.RunTagEnergy : GameEventBus.RunTagStopped;
            return OutOfEnergyOr(CancelledResult());
        }
        catch (LuaCompileException ex)
        {
            outcome = GameEventBus.RunTagError;
            return ExecutionResult.CompileError(ex.Message);
        }
        catch (Exception ex)
        {
            // LuaRuntimeException и исключения команд, дошедшие до верха
            outcome = ClassifyFailure(ex, ct);
            return OutOfEnergyOr(ct.IsCancellationRequested
                ? CancelledResult()
                : ExecutionResult.RuntimeError(WithErrorLocation(ex)));
        }
        finally
        {
            // Если за это время запустили новое выполнение — состояние принадлежит ему
            if (_cts == cts)
            {
                StopWatchdog();
                // До SetRunning(false): конец дня (DayCycle) — уже после итога запуска
                GameEventBus.PublishRunFinished(new CodeRunReport(
                    succeeded, started, _playerEnergy.TotalSpent - spentAtStart, usedConstructs, outcome));
                _lineTracker.Clear();
                _debugger.Reset();
                SetRunning(false);
                if (_unlocksPending) ApplyUnlocks();
            }
        }
    }

    /// <summary>
    /// Выполняет код, «размеченный» списаниями энергии: платим за то, что реально выполняется.
    /// Синтаксис к этому моменту уже проверен по исходному коду (ошибки указывают на код игрока,
    /// а не на вставки).
    /// </summary>
    private async UniTask RunChunkAsync(string code, string chunkName, CancellationToken ct)
    {
        await _state.DoStringAsync(_instrumenter.Instrument(code), chunkName, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    // ─── Watchdog ─────────────────────────────────────────────

    /// <summary>
    /// Если программа остановлена из-за нехватки энергии — сообщаем именно это.
    /// </summary>
    private ExecutionResult OutOfEnergyOr(ExecutionResult other)
    {
        return _meter.IsExhausted ? ExecutionResult.RuntimeError(_meter.ExhaustedMessage) : other;
    }

    /// <summary>
    /// Итог упавшего запуска (Tag RunFinished): нехватка энергии важнее всего (даже если её ошибку
    /// пересоздал pcall/error), затем остановка, затем вызов закрытой команды, иначе — ошибка кода.
    /// </summary>
    private string ClassifyFailure(Exception ex, CancellationToken ct)
    {
        if (_meter.IsExhausted) return GameEventBus.RunTagEnergy;
        if (ct.IsCancellationRequested) return GameEventBus.RunTagStopped;
        if (_lockedCommandError != null && ex.Message != null && ex.Message.Contains(_lockedCommandError))
            return GameEventBus.RunTagLocked;
        return GameEventBus.RunTagError;
    }

    // Место ошибки от Lua: `[string "Main"]:3: …` (возможно, после "Lua-CSharp: ")
    private static readonly System.Text.RegularExpressions.Regex ErrorLocationRegex =
        new System.Text.RegularExpressions.Regex(@"^(?:Lua-CSharp:\s*)?\[string\s+""[^""]*""\]:\d+:",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private const string LuaErrorPrefix = "Lua-CSharp: ";

    /// <summary>
    /// Текст ошибки с местом: исключения команд (ArgumentException и т. п.) и error(x, 0) приходят без
    /// `[string "окно"]:N:` — тогда место берётся из стека Lua в момент ошибки (LuaTraceback), а если его нет —
    /// из последней выполнявшейся строки (LuaLineTracker). Формат — как у ошибок Lua, его разбирает LuaErrorExplainer.
    /// </summary>
    private string WithErrorLocation(Exception ex)
    {
        string message = ex.Message ?? string.Empty;
        if (ErrorLocationRegex.IsMatch(message)) return message;

        if (TryGetTracebackLocation(ex as LuaRuntimeException, out string chunk, out int chunkLine))
            return AttachErrorLocation(message, chunk, chunkLine);

        // Строка трекера — строка окна; в чанке доп. окна выше неё стоит строка обёртки function Title(...)
        if (!string.IsNullOrEmpty(_lineTracker.Chunk) && _lineTracker.Line > 0)
        {
            int line = _lineTracker.Line + (_lineTracker.Chunk == MainChunkName ? 0 : 1);
            return AttachErrorLocation(message, _lineTracker.Chunk, line);
        }
        return message;
    }

    /// <summary>
    /// Окно и строка чанка, где выполнялся код игрока в момент ошибки: ближайший к вершине Lua-кадр стека.
    /// </summary>
    private static bool TryGetTracebackLocation(LuaRuntimeException ex, out string chunk, out int chunkLine)
    {
        chunk = null;
        chunkLine = 0;
        Lua.Runtime.Traceback traceback = ex?.LuaTraceback;
        if (traceback == null) return false;

        ReadOnlySpan<Lua.Runtime.CallStackFrame> frames = traceback.StackFrames;
        for (int i = frames.Length - 1; i >= 0; i--)
        {
            if (!(frames[i].Function is Lua.Runtime.LuaClosure closure)) continue;
            chunk = closure.Proto.ChunkName;

            // Окно и строка — из одного кадра, как в LuaLineTracker.Probe: номер инструкции вызова хранит
            // кадр над Lua-кадром (C#-функция команды). Lua-кадр на вершине (ошибка в самом Lua) — LastLine
            ReadOnlySpan<int> lineInfo = closure.Proto.LineInfo;
            int pc = i + 1 < frames.Length ? frames[i + 1].CallerInstructionIndex : -1;
            chunkLine = pc >= 0 && pc < lineInfo.Length ? lineInfo[pc] : traceback.LastLine;
            break;
        }
        return !string.IsNullOrEmpty(chunk) && chunkLine > 0;
    }

    /// <summary>
    /// Добавляет к тексту ошибки место в формате Lua: `[string "Main"]:3: moveTo: usage …`
    /// (после префикса "Lua-CSharp: ", если он есть). Строка — номер строки чанка: у доп. окон на 1 больше
    /// строки окна (обёртка), как в ошибках самого Lua.
    /// </summary>
    public static string AttachErrorLocation(string message, string chunk, int chunkLine)
    {
        message ??= string.Empty;
        if (string.IsNullOrEmpty(chunk) || chunkLine < 1) return message;

        string location = System.FormattableString.Invariant($"[string \"{chunk}\"]:{chunkLine}: ");
        return message.StartsWith(LuaErrorPrefix, StringComparison.Ordinal)
            ? LuaErrorPrefix + location + message.Substring(LuaErrorPrefix.Length)
            : location + message;
    }

    private ExecutionResult CancelledResult()
    {
        return _timedOut
            ? ExecutionResult.RuntimeError(
                $"script blocked the game for more than {freezeTimeoutSeconds:0.#}s " +
                "and was stopped (infinite loop?)")
            : ExecutionResult.Cancelled();
    }

    private void MarkFrame()
    {
        Interlocked.Exchange(ref _lastFrameTimestamp, Stopwatch.GetTimestamp());
    }

    private void StartWatchdog(CancellationTokenSource cts)
    {
        StopWatchdog();
        _timedOut = false;
        MarkFrame();

        long timeoutTicks = (long)(freezeTimeoutSeconds * Stopwatch.Frequency);

        // Таймер работает в пуле потоков, поэтому срабатывает даже когда
        // главный поток завис в цикле Lua без единого await
        _watchdog = new Timer(_ =>
        {
            long frozenFor = Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastFrameTimestamp);
            // На точке останова главный поток свободен, а кадров может не быть (игра свёрнута) — это не зависание
            if (frozenFor <= timeoutTicks || _cts != cts || IsPaused) return;

            _timedOut = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }, null, 250, 250);
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    private void SetRunning(bool running)
    {
        IsRunning = running;
        OnRunningChanged?.Invoke(running);
    }

    /// <summary>Только открытые команды — источник для справки, подсветки, автодополнения и цен.</summary>
    public LuaCommandRegistry Registry => _registry;
    /// <summary>Все команды игры, включая закрытые (например, чтобы показать их в справке недоступными).</summary>
    public IReadOnlyList<ILuaCommand> AllCommands => _allCommands;
    public UnlockRegistry Unlocks => _unlocks;
    public EnergyConfig EnergyConfig => _energyConfig;
}