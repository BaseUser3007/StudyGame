using System;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Готовая команда: дойти до ближайшего видимого ресурса и собрать его.
/// Делает то же, что связка findObjects + moveTo + harvest, но стоит дороже —
/// удобство за энергию. Добыча тратит столько же, сколько harvest (через ResourceHarvester).
/// </summary>
public class ObtainCommand : PlayerCommandBase
{
    public override string Name => "obtain";
    // Самописный аналог (local + findObjects + moveTo + harvest) стоит 1 + 1 + 2 + 1 = 5 за проход
    public override float EnergyCost => 8f;
    public override string Signature => "obtain([type])";
    public override bool IsReadyMade => true;
    public override string Description => "Готовая команда: дойти до ближайшего видимого ресурса (можно указать тип: stone, stick…) " +
                                          "и подобрать его, как harvest (останавливается, как только объект в досягаемости; деревья и валуны не трогает — для них mine). Дороже, чем findObjects + moveTo + harvest. " +
                                          "Возвращает true или false и причину";

    private readonly ObjectManager _objectManager;
    private readonly ResourceHarvester _harvester;

    public ObtainCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager,
        ResourceHarvester harvester)
        : base(executor, mapData)
    {
        _objectManager = objectManager;
        _harvester = harvester;
    }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error = null;
        Type type = null;

        if (context.ArgumentCount > 0 && context.GetArgument(0).Type != LuaValueType.Nil)
        {
            if (!context.GetArgument(0).TryRead(out string typeName))
                error = "usage: obtain() or obtain(\"type\")";
            else if (!FindObjectsCommand.TryGetType(typeName, out type))
                error = $"unknown type: {typeName}";
        }

        if (error == null)
        {
            ResourceObject target = FindTarget(type, out error);
            if (target != null)
            {
                // Идём, пока объект не окажется в досягаемости — не упираясь в его коллайдер
                await Executor.MoveToAsync(target.Tile, ct,
                    () => Vector2.Distance(Executor.CurrentTile, target.Position) <= target.InteractionRadius);
                error = _harvester.TryHarvest(target);
            }
        }

        if (error == null)
        {
            context.Return(true);
            return 1;
        }

        context.Return(false, error);
        return 2;
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Ближайший видимый объект, который можно добыть прямо сейчас.
    /// Если подходящих нет, error объясняет почему.
    /// </summary>
    private ResourceObject FindTarget(Type type, out string error)
    {
        Vector2Int playerTile = Executor.CurrentTile;
        ResourceObject nearest = null;
        float minDistance = float.MaxValue;
        string firstProblem = null;

        foreach (ResourceObject obj in _objectManager.GetObjectsInRadius<ResourceObject>(playerTile, Executor.VisionRadius))
        {
            if (type != null && obj.GetType() != type) continue;

            // Объекты, которые сейчас не добыть (нужен инструмент, нет места), пропускаем
            string problem = _harvester.CheckHarvestable(obj);
            if (problem != null)
            {
                firstProblem ??= problem;
                continue;
            }

            float distance = Vector2.Distance(playerTile, obj.Position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = obj;
            }
        }

        error = nearest != null ? null
            : firstProblem ?? (type != null ? "nothing of this type in sight" : "nothing to obtain in sight");
        return nearest;
    }
}
