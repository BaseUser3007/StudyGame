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
    public event Action<string, LogEntryType> OnLogMessage;
    public event Action<bool> OnRunningChanged;

    /// <summary>
    /// Открытия применены: в реестре появились команды и/или открылась конструкция языка.
    /// UI перечитывает справку, подсветку, автодополнение и цены строк. Во время выполнения кода
    /// новые команды подключаются после его окончания — тогда же приходит событие.
    /// </summary>
    public event Action OnUnlocksApplied;

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
    private CancellationTokenSource _cts;

    // Watchdog: метка последнего кадра главного потока (Stopwatch ticks)
    private long _lastFrameTimestamp;
    private Timer _watchdog;
    private volatile bool _timedOut;

    public bool IsRunning { get; private set; }

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
        AddCommand(new AutoExploreCommand(_executor, _mapData));

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
        AddCommand(new GetStatusCommand(_playerEnergy, _playerHealth, _dayCycle));

        // Головоломки (этап 6): условие и ответ; открываются достижением hub_builder
        AddCommand(new PuzzleCommand(_executor, _mapData, _puzzles));
        AddCommand(new SolveCommand(_puzzles));

        // Добавляй новые команды здесь ↓ (открытость задаётся UnlockConfig / MetaProgress)
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
        _state.Environment[EnergyInstrumenter.ChargeFunction] = new LuaFunction((context, ct) =>
        {
            double amount = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out double n) ? n : 0d;
            _meter.Charge((float)amount, "code");
            context.Return(true);
            return new System.Threading.Tasks.ValueTask<int>(1);
        });
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
    // io/os могут менять файлы сохранений и закрыть игру, debug/package/collectgarbage — служебные
    private static readonly string[] BlockedGlobals =
    {
        "load", "loadfile", "dofile", "require", "package", "io", "debug", "collectgarbage"
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

    private static LuaFunction CreateLockedStub(string name)
    {
        return new LuaFunction((context, ct) =>
            throw new InvalidOperationException($"command `{name}` is not unlocked yet"));
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

        StopWatchdog();
        _cts?.Cancel();
        _cts?.Dispose();
        // finally старого запуска сравнивает _cts со своим: после уничтожения он ничего не публикует
        // (RunFinished ушёл бы в шину уже новой сцены)
        _cts = null;
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
        var usedConstructs = new HashSet<string>(StringComparer.Ordinal);
        bool started = false;
        bool succeeded = false;

        SetRunning(true);
        StartWatchdog(cts);
        _meter.Reset();
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
                return ExecutionResult.RuntimeError(locked);

            // 3. Выполнение кода, размеченного списаниями энергии
            started = true;
            foreach (var chunk in chunks)
                await RunChunkAsync(chunk.Code, chunk.Name, ct);

            succeeded = true;
            return ExecutionResult.Success();
        }
        catch (OperationCanceledException)
        {
            return OutOfEnergyOr(CancelledResult());
        }
        catch (LuaCompileException ex)
        {
            return ExecutionResult.CompileError(ex.Message);
        }
        catch (LuaRuntimeException ex)
        {
            return OutOfEnergyOr(ct.IsCancellationRequested
                ? CancelledResult()
                : ExecutionResult.RuntimeError(ex.Message));
        }
        catch (Exception ex)
        {
            return OutOfEnergyOr(ct.IsCancellationRequested
                ? CancelledResult()
                : ExecutionResult.RuntimeError(ex.Message));
        }
        finally
        {
            // Если за это время запустили новое выполнение — состояние принадлежит ему
            if (_cts == cts)
            {
                StopWatchdog();
                // До SetRunning(false): конец дня (DayCycle) — уже после итога запуска
                GameEventBus.PublishRunFinished(new CodeRunReport(
                    succeeded, started, _playerEnergy.TotalSpent - spentAtStart, usedConstructs));
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
            if (frozenFor <= timeoutTicks || _cts != cts) return;

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