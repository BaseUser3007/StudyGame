using System;
using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// Состояние игрока и дня для кода: сколько энергии и здоровья осталось, какой максимум, сколько запусков в запасе,
/// сколько энергии потрачено в текущем запуске (spent — замер для достижения efficient_logger).
/// Позволяет коду вовремя остановиться и не закончить день с нулём (это штраф к энергии на завтра).
/// </summary>
public class GetStatusCommand : LuaCommandBase
{
    public override string Name => "getStatus";
    public override float EnergyCost => 1f;
    public override string Signature => "getStatus()";
    public override string Description =>
        "Status: a table { energy, maxEnergy, spent, health, maxHealth, day, runsLeft, threatDay, daysToThreat, threat }. " +
        "spent — energy spent since the current code run started (this call included), " +
        "threatDay — the day goblins start patrolling the map, daysToThreat — days left until then " +
        "(0 — the threat is on), threat — whether the threat is on. " +
        "Ending a day with less than 1 energy lowers tomorrow's maximum, " +
        "sleeping by a bed (build(\"bed\")) gives a bonus";

    private readonly PlayerEnergy _energy;
    private readonly PlayerHealth _health;
    private readonly DayCycle _dayCycle;
    private readonly Func<float> _spentThisRun;

    /// <param name="spentThisRun">Потрачено с начала текущего запуска (LuaSandbox.EnergySpentThisRun).</param>
    public GetStatusCommand(PlayerEnergy energy, PlayerHealth health, DayCycle dayCycle, Func<float> spentThisRun)
    {
        _energy = energy;
        _health = health;
        _dayCycle = dayCycle;
        _spentThisRun = spentThisRun;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        LuaTable status = new LuaTable();
        // Плата за этот вызов уже списана — energy показывает остаток после неё, spent её включает
        status["energy"] = (double)_energy.Current;
        status["maxEnergy"] = (double)_energy.Max;
        status["spent"] = (double)(_spentThisRun?.Invoke() ?? 0f);

        // Здоровья и дня нет только в тестах песочницы без сцены — тогда этих полей нет
        if (_health != null)
        {
            status["health"] = (double)_health.Current;
            status["maxHealth"] = (double)_health.Max;
        }

        if (_dayCycle != null)
        {
            status["day"] = (double)_dayCycle.Day;
            // Текущий запуск засчитается, когда закончится, — показываем, сколько останется после него
            status["runsLeft"] = (double)(_dayCycle.RunsLeft - 1 < 0 ? 0 : _dayCycle.RunsLeft - 1);
            // Мировая угроза: с threatDay на карте патрули (GameDesign §10.1)
            status["threatDay"] = (double)_dayCycle.ThreatDay;
            status["daysToThreat"] = (double)_dayCycle.DaysUntilThreat;
            status["threat"] = _dayCycle.IsThreatActive;
        }

        context.Return(status);
        return new ValueTask<int>(1);
    }
}
