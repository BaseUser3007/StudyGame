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
    public override string Description =>
        "Mine a tree (needs an axe), a boulder or gold_ore (pickaxe), etc. Without arguments — the nearest one to the player, " +
        "mine(x, y) or mine(obj) — at a nearby point. The tool wears out, " +
        "the loot falls on the ground — pick it up with harvest. " +
        "Spends energy by the loot durability. Returns true or false and a reason";

    private readonly ResourceHarvester _harvester;
    private readonly ObjectManager _objectManager;
    private readonly EnemyManager _enemies;
    private readonly PuzzleManager _puzzles;

    public MineCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ResourceHarvester harvester,
        ObjectManager objectManager,
        EnemyManager enemies,
        PuzzleManager puzzles = null)
        : base(executor, mapData)
    {
        _harvester = harvester;
        _objectManager = objectManager;
        _enemies = enemies;
        _puzzles = puzzles;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error;
        if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
            error = _harvester.TryMineNearest();
        else if (HarvestCommand.TryReadPoint(context, out Vector2 point))
        {
            // Подвижный объект ищется по id; иначе враг на точке — не ресурс: подсказываем attack() вместо «no object at»
            int movingId = HarvestCommand.ReadMovingObjectId(context, _objectManager);
            error = (movingId != 0 ? null
                        : AttackCommand.GetNotHarvestableError(context, point, _objectManager, _enemies, "mined", _puzzles))
                    ?? _harvester.TryMineAt(point, movingId);
        }
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
