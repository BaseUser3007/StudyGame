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

    [Tooltip("Если главный поток заблокирован Lua-кодом дольше этого времени, выполнение прерывается")]
    [SerializeField] private float freezeTimeoutSeconds = 3f;

    private PlayerCommandExecutor _executor;
    private IMapDataProvider _mapData;
    private EnergyConfig _energyConfig;
    private ObjectManager _objectManager;
    private PlayerEnergy _playerEnergy;
    private PlayerInventory _inventory;
    private RecipeBook _recipeBook;
    private StructureManager _structures;
    private float _buildReach;
    private DayCycle _dayCycle;

    private LuaState _state;
    private LuaCommandRegistry _registry;
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
        EnergyConfig energyConfig,
        ObjectManager objectManager,
        PlayerEnergy playerEnergy,
        PlayerInventory inventory,
        RecipeBook recipeBook,
        StructureManager structures,
        float buildReach,
        DayCycle dayCycle)
    {
        _executor = executor;
        _mapData = mapData;
        _energyConfig = energyConfig;
        _objectManager = objectManager;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
        _recipeBook = recipeBook;
        _structures = structures;
        _buildReach = buildReach;
        _dayCycle = dayCycle;

        _registry = new LuaCommandRegistry();

        RegisterCommands();

        _energyCounter = new EnergyCounter(_energyConfig, _registry);
        _instrumenter = new EnergyInstrumenter(_energyConfig);
        _meter = new EnergyMeter(_playerEnergy);
        _meter.OnExhausted += HandleEnergyExhausted;

        CreateLuaState();
    }

    private void RegisterCommands()
    {
        // Движение
        _registry.RegisterCommand(new MoveCommand(_executor, _mapData));
        _registry.RegisterCommand(new MoveToCommand(_executor, _mapData));
        _registry.RegisterCommand(new GetPositionCommand(_executor, _mapData));

        // Авто команды
        _registry.RegisterCommand(new AutoExploreCommand(_executor, _mapData));

        // Объекты
        var harvester = new ResourceHarvester(_executor, _mapData, _objectManager, _playerEnergy, _inventory);
        _registry.RegisterCommand(new FindObjectsCommand(_executor, _mapData, _objectManager));
        _registry.RegisterCommand(new HarvestCommand(_executor, _mapData, harvester));
        _registry.RegisterCommand(new MineCommand(_executor, _mapData, harvester));

        // Готовые команды: удобнее, но дороже самописного аналога
        _registry.RegisterCommand(new ObtainCommand(_executor, _mapData, _objectManager, harvester));

        // Крафт
        _registry.RegisterCommand(new CraftCommand(_recipeBook, _playerEnergy, _inventory, _structures));

        // Постройки
        _registry.RegisterCommand(new BuildCommand(_executor, _mapData, _inventory, _structures, _buildReach));

        // Инвентарь
        _registry.RegisterCommand(new DropCommand(_executor, _mapData, _inventory, _objectManager));
        _registry.RegisterCommand(new GetInventoryCommand(_inventory));

        // Состояние дня и энергии
        _registry.RegisterCommand(new GetStatusCommand(_playerEnergy, _dayCycle));

        // Добавляй новые команды здесь ↓
    }

    private void CreateLuaState()
    {
        _state = LuaState.Create();
        _state.OpenStandardLibraries();
        _registry.RegisterAll(_state, _meter);

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
        StopWatchdog();
        _cts?.Cancel();
        _cts?.Dispose();
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

        SetRunning(true);
        StartWatchdog(cts);
        _meter.Reset();

        try
        {
            foreach (var kvp in auxiliaryWindows)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                string wrapped = $"function {kvp.Key}(...)\n{kvp.Value}\nend";
                await RunChunkAsync(wrapped, kvp.Key, ct);
            }

            await RunChunkAsync(mainCode, "Main", ct);
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
                SetRunning(false);
            }
        }
    }

    /// <summary>
    /// Синтаксис проверяется по исходному коду (ошибки указывают на код игрока, а не на вставки),
    /// затем выполняется код, «размеченный» списаниями энергии: платим за то, что реально выполняется.
    /// </summary>
    private async UniTask RunChunkAsync(string code, string chunkName, CancellationToken ct)
    {
        _state.Load(code.AsSpan(), chunkName, null);
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

    public LuaCommandRegistry Registry => _registry;
    public EnergyConfig EnergyConfig => _energyConfig;
}