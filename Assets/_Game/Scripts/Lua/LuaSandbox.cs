using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Lua;
using Lua.Standard;
using UnityEngine;

public class LuaSandbox : MonoBehaviour
{
    public event Action<string, LogEntryType> OnLogMessage;

    private PlayerCommandExecutor _executor;
    private IMapDataProvider _mapData;
    private EnergyConfig _energyConfig;
    private ObjectManager _objectManager;

    private LuaState _state;
    private LuaCommandRegistry _registry;
    private EnergyCounter _energyCounter;
    private CancellationTokenSource _cts;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        EnergyConfig energyConfig,
        ObjectManager objectManager)
    {
        _executor = executor;
        _mapData = mapData;
        _energyConfig = energyConfig;
        _objectManager = objectManager;

        _registry = new LuaCommandRegistry();

        RegisterCommands();

        _energyCounter = new EnergyCounter(_energyConfig, _registry);

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
        _registry.RegisterCommand(new FindObjectsCommand(_executor, _mapData, _objectManager));

        // Добавляй новые команды здесь ↓
    }

    private void CreateLuaState()
    {
        _state = LuaState.Create();
        _state.OpenStandardLibraries();
        _registry.RegisterAll(_state);
    }

    private void OnDestroy()
    {
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

    public async UniTask<ExecutionResult> ExecuteAsync(
        string mainCode,
        Dictionary<string, string> auxiliaryWindows)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            foreach (var kvp in auxiliaryWindows)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                string wrapped = $"function {kvp.Key}(...)\n{kvp.Value}\nend";
                await _state.DoStringAsync(wrapped);
            }

            await _state.DoStringAsync(mainCode);
            return ExecutionResult.Success();
        }
        catch (LuaCompileException ex)
        {
            return ExecutionResult.CompileError(ex.Message);
        }
        catch (LuaRuntimeException ex)
        {
            return ExecutionResult.RuntimeError(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult.Cancelled();
        }
        catch (Exception ex)
        {
            return ExecutionResult.RuntimeError(ex.Message);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public LuaCommandRegistry Registry => _registry;
}