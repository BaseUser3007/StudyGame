using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// Состояние игрока и дня для кода: сколько энергии и здоровья осталось, какой максимум, сколько запусков в запасе.
/// Позволяет коду вовремя остановиться и не закончить день с нулём (это штраф к энергии на завтра).
/// </summary>
public class GetStatusCommand : LuaCommandBase
{
    public override string Name => "getStatus";
    public override float EnergyCost => 1f;
    public override string Signature => "getStatus()";
    public override string Description => "Состояние: таблица { energy, maxEnergy, health, maxHealth, day, runsLeft, threatDay, daysToThreat, threat }. " +
                                          "threatDay — день, с которого гоблины патрулируют карту, daysToThreat — сколько дней до него " +
                                          "(0 — угроза уже идёт), threat — идёт ли угроза. " +
                                          "Закончить день с энергией меньше 1 — штраф к максимуму на завтра, " +
                                          "у кровати (build(\"bed\")) — бонус";

    private readonly PlayerEnergy _energy;
    private readonly PlayerHealth _health;
    private readonly DayCycle _dayCycle;

    public GetStatusCommand(PlayerEnergy energy, PlayerHealth health, DayCycle dayCycle)
    {
        _energy = energy;
        _health = health;
        _dayCycle = dayCycle;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        LuaTable status = new LuaTable();
        // Плата за этот вызов уже списана — energy показывает остаток после неё
        status["energy"] = (double)_energy.Current;
        status["maxEnergy"] = (double)_energy.Max;
        status["health"] = (double)_health.Current;
        status["maxHealth"] = (double)_health.Max;
        status["day"] = (double)_dayCycle.Day;
        // Текущий запуск засчитается, когда закончится, — показываем, сколько останется после него
        status["runsLeft"] = (double)(_dayCycle.RunsLeft - 1 < 0 ? 0 : _dayCycle.RunsLeft - 1);
        // Мировая угроза: с threatDay на карте патрули (GameDesign §10.1)
        status["threatDay"] = (double)_dayCycle.ThreatDay;
        status["daysToThreat"] = (double)_dayCycle.DaysUntilThreat;
        status["threat"] = _dayCycle.IsThreatActive;

        context.Return(status);
        return new ValueTask<int>(1);
    }
}
