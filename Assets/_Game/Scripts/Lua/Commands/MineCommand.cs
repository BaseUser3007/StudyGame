using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Добыть объект, которому нужен инструмент (дерево — топор, валун — кирка).
/// Добыча выпадает на землю вокруг объекта — её подбирают через harvest.
/// </summary>
public class MineCommand : PlayerCommandBase
{
    public override string Name => "mine";
    // Плата за вызов; сама добыча дополнительно тратит энергию по прочности того, что выпадет
    public override float EnergyCost => 1f;
    public override string Signature => "mine([x, y])";
    public override string Description => "Добыть дерево (нужен axe), валун или gold_ore (pickaxe) и т.п. Без аргументов — ближайший к персонажу, " +
                                          "mine(x, y) или mine(obj) — в точке рядом. Инструмент изнашивается, " +
                                          "добыча выпадает на землю — собрать её можно через harvest. " +
                                          "Тратит энергию по прочности добычи. Возвращает true или false и причину";

    private readonly ResourceHarvester _harvester;
    private readonly ObjectManager _objectManager;
    private readonly EnemyManager _enemies;

    public MineCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ResourceHarvester harvester,
        ObjectManager objectManager,
        EnemyManager enemies)
        : base(executor, mapData)
    {
        _harvester = harvester;
        _objectManager = objectManager;
        _enemies = enemies;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error;
        if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
            error = _harvester.TryMineNearest();
        else if (HarvestCommand.TryReadPoint(context, out Vector2 point))
            // Враг — не ресурс: подсказываем attack() вместо «no object at»
            error = AttackCommand.GetNotHarvestableError(context, point, _objectManager, _enemies, "mined")
                    ?? _harvester.TryMineAt(point);
        else
            error = "usage: mine(), mine(x, y) or mine(obj)";

        if (error == null)
        {
            context.Return(true);
            return new ValueTask<int>(1);
        }

        context.Return(false, error);
        return new ValueTask<int>(2);
    }
}
