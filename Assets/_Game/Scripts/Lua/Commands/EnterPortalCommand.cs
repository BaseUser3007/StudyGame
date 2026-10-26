using System;
using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// enterPortal() — уйти из забега через портал в лагерь. Единственный выход из забега (кнопки Exit Run нет),
/// поэтому команда в стартовом наборе. Персонаж должен стоять не дальше reach (PlayerConfig.structureInteractionRadius)
/// от края портала. Успех: RunController (IRunExitService) запоминает выход, песочница сразу заканчивает запуск —
/// код после вызова не выполняется, итог запуска «ok» (не ошибка), затем экран итогов: материалы — на склад лагеря.
/// Энергия за вызов не возвращается.
/// </summary>
public class EnterPortalCommand : PlayerCommandBase
{
    public override string Name => "enterPortal";
    // Действие в мире, но разовое и обязательное для выхода — дёшево
    public override float EnergyCost => 1f;
    public override string Signature => "enterPortal()";
    // Без числа досягаемости: reach берётся из PlayerConfig, а описание переводится по точному тексту (Loc.T)
    public override string Description =>
        "Leave the run through the portal and return to the camp: the player must stand next to the portal " +
        "(moveTo(findObjects(\"portal\")[1]) walks up to it). The materials in the inventory go to the camp stock, " +
        "the code after the call is not executed. Returns nil and a reason if you can't leave (\"portal is too far\")";

    /// <summary>Ответ, когда персонаж дальше досягаемости (сравнивают в коде — не переводится).</summary>
    public const string TooFarError = "portal is too far";
    public const string NoPortalError = "there is no portal here";
    public const string CantLeaveError = "can't leave the run now";

    private readonly RunPortal _portal;
    private readonly float _reach;
    private readonly Func<bool> _exitThroughPortal;

    /// <summary>
    /// portal — портал забега (null в лагере); reach — досягаемость от края портала;
    /// exitThroughPortal — просит выход и заканчивает запуск (LuaSandbox), false — выйти сейчас нельзя.
    /// После принятого выхода команда бросает RunHaltedException (значений не возвращает).
    /// </summary>
    public EnterPortalCommand(
        PlayerCommandExecutor executor, IMapDataProvider mapData, RunPortal portal, float reach,
        Func<bool> exitThroughPortal)
        : base(executor, mapData)
    {
        _portal = portal;
        _reach = reach;
        _exitThroughPortal = exitThroughPortal;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error = _portal == null || Executor == null ? NoPortalError
            : !_portal.IsNear(Executor.Position, _reach) ? TooFarError
            : _exitThroughPortal == null || !_exitThroughPortal() ? CantLeaveError
            : null;

        if (error != null)
        {
            context.Return(LuaValue.Nil, error);
            return new ValueTask<int>(2);
        }

        // Выход принят, песочница остановила запуск (EnergyMeter.Halt): дальше код не идёт —
        // даже если вызов обёрнут в pcall, следующее же списание энергии снова бросит RunHaltedException
        throw new RunHaltedException();
    }
}
