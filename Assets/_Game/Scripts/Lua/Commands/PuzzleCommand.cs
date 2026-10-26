using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// puzzle([obj | x, y]) — условие головоломки рядом (GameDesign §10.4): { kind, text, input, limit, solved }.
/// Вход обелиска генерируется заново в каждом запуске кода — ответ надо вычислить в том же запуске.
/// Нужно стоять не дальше PuzzleManager.InteractionRadius (1,5 тайла) от края головоломки.
/// Открывается достижением hub_builder.
/// </summary>
public class PuzzleCommand : PlayerCommandBase
{
    public override string Name => "puzzle";
    // Чтение условия — как check (GameDesign §10.4)
    public override float EnergyCost => 1f;
    public override string Signature => "puzzle([obj | x, y])";
    public override string Description =>
        "Conditions of a nearby puzzle: puzzle(obj) — an obelisk, maze or plates from findObjects/check, puzzle(x, y) — at a point, " +
        "puzzle() — the nearest one in reach (1.5 tiles). Returns a table { kind, text, input, limit, solved } " +
        "(kind — \"sum\", \"max\", \"count\", \"maze\" or \"plates\"; input — an array of numbers, new on every code run) " +
        "or nil and a reason. The answer is given with solve(obj, answer)";

    private const string UsageMessage = "usage: puzzle(), puzzle(x, y) or puzzle(obj)";

    private readonly PuzzleManager _puzzles;

    public PuzzleCommand(PlayerCommandExecutor executor, IMapDataProvider mapData, PuzzleManager puzzles)
        : base(executor, mapData)
    {
        _puzzles = puzzles;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string error;
        PuzzleStructure puzzle = context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil
            ? FindNearestInReach(out error)
            : LuaPuzzles.Resolve(context, _puzzles, UsageMessage, out error);

        if (puzzle != null && !_puzzles.IsInReach(puzzle))
            error = PuzzleManager.Describe(PuzzleSolveResult.TooFar);

        if (error != null)
        {
            context.Return(LuaValue.Nil, error);
            return new ValueTask<int>(2);
        }

        context.Return(LuaPuzzles.BuildTaskTable(puzzle));
        return new ValueTask<int>(1);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private PuzzleStructure FindNearestInReach(out string error)
    {
        error = null;
        if (_puzzles != null)
        {
            var inReach = new List<PuzzleStructure>();
            foreach (PuzzleStructure puzzle in _puzzles.Puzzles)
                if (puzzle != null && _puzzles.IsInReach(puzzle))
                    inReach.Add(puzzle);

            if (Executor != null)
            {
                Vector2 player = Executor.Position;
                inReach.Sort((a, b) => a.DistanceTo(player).CompareTo(b.DistanceTo(player)));
            }

            if (inReach.Count > 0) return inReach[0];
        }

        error = "no puzzle within reach";
        return null;
    }
}
