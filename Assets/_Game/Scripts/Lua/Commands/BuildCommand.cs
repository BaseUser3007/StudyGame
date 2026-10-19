using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Ставит постройку из инвентаря на землю рядом с персонажем.
/// build("workbench") — на первое свободное место вокруг, build("workbench", x, y) — в указанную точку.
/// </summary>
public class BuildCommand : PlayerCommandBase
{
    public override string Name => "build";
    public override float EnergyCost => 3f;
    public override string Signature => "build(name, [x, y])";
    public override string Description =>
        "Place a structure from the inventory (e.g. build(\"workbench\")). " +
        "Without coordinates — on a free spot next to the player, with coordinates — at that point " +
        "(within reach). The ground under it must be land with no other objects. " +
        "Returns true, x, y or false and a reason";

    private readonly PlayerInventory _inventory;
    private readonly StructureManager _structures;
    private readonly float _reach;

    public BuildCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        PlayerInventory inventory,
        StructureManager structures,
        float reach)
        : base(executor, mapData)
    {
        _inventory = inventory;
        _structures = structures;
        _reach = reach;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error = TryBuild(context, out Vector2 position);

        if (error == null)
        {
            context.Return(true, (double)position.x, (double)position.y);
            return new ValueTask<int>(3);
        }

        context.Return(false, error);
        return new ValueTask<int>(2);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Возвращает null при успехе, иначе причину неудачи.
    /// </summary>
    private string TryBuild(LuaFunctionExecutionContext context, out Vector2 position)
    {
        position = default;

        if (context.ArgumentCount == 0 || !context.GetArgument(0).TryRead(out string itemName))
            return "usage: build(\"name\") or build(\"name\", x, y)";

        ResourceItem found = _inventory.FindItem(itemName);
        if (found == null)
            return $"no {itemName} in inventory";
        if (!(found is StructureItem item))
            return $"{itemName} can't be built";

        Vector2Int playerTile = Executor.CurrentTile;
        string error;

        if (context.ArgumentCount >= 3
            && context.GetArgument(1).TryRead(out double x)
            && context.GetArgument(2).TryRead(out double y))
        {
            position = new Vector2((float)x, (float)y);
            if (Vector2.Distance(playerTile, position) > _reach)
                return "too far: build next to the player";

            error = _structures.TryPlace(item, position, out _);
        }
        else
        {
            error = TryPlaceAround(item, playerTile, out position);
        }

        if (error != null) return error;

        _inventory.Remove(item, 1);
        return null;
    }

    /// <summary>
    /// Пробует соседние тайлы — сначала по сторонам, потом по диагоналям.
    /// </summary>
    private string TryPlaceAround(StructureItem item, Vector2Int playerTile, out Vector2 position)
    {
        string lastError = "no free place nearby";

        foreach (Vector2Int offset in Offsets)
        {
            position = playerTile + offset;
            if (Vector2.Distance(playerTile, position) > _reach) continue;

            string error = _structures.TryPlace(item, position, out _);
            if (error == null) return null;
            lastError = error == "place is occupied" ? "no free place nearby" : error;
        }

        position = default;
        return lastError;
    }

    private static readonly List<Vector2Int> Offsets = new List<Vector2Int>
    {
        new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1),
        new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(1, 1), new Vector2Int(-1, 1)
    };
}
