using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// solve(obj, answer) — ответ на задачу обелиска (GameDesign §10.4). Попытки не ограничены, каждая стоит
/// цену вызова. Проверку, награду, событие PuzzleSolved и уведомление делает PuzzleManager.TrySolveCodeTask
/// (энергию он не списывает — только эта команда). Открывается вместе с событием карты code_task
/// (по числу достижений, UnlockConfig.mapEventThresholds).
/// </summary>
public class SolveCommand : LuaCommandBase
{
    public override string Name => "solve";
    // Дороже puzzle(): подбор ответа перебором должен стоить заметно (GameDesign §10.4)
    public override float EnergyCost => 2f;
    public override string Signature => "solve(obj, answer)";
    public override string Description =>
        "Answer to an obelisk task from findObjects/check: solve(obj, answer), the player within 1.5 tiles of the obelisk. " +
        "The answer must be computed by code in the same run (puzzle(obj).input changes every run). " +
        "Correct — a reward until the end of the run and true; otherwise nil and a reason (\"wrong answer\"); one attempt per obelisk per code run";

    private const string UsageMessage = "usage: solve(obj, answer)";

    private readonly PuzzleManager _puzzles;

    public SolveCommand(PuzzleManager puzzles)
    {
        _puzzles = puzzles;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        PuzzleStructure puzzle = LuaPuzzles.Resolve(context, _puzzles, UsageMessage, out string error);

        if (puzzle is PlatesPuzzle)
        {
            // Плиты решают ногами — ответа у них нет
            error = LuaPuzzles.PlatesSolveError;
        }
        else if (puzzle != null)
        {
            if (context.ArgumentCount < 2 || !context.GetArgument(1).TryRead(out double answer))
                error = "answer must be a number";
            else
                error = PuzzleManager.Describe(_puzzles.TrySolveCodeTask(puzzle, answer));
        }

        if (error != null)
        {
            context.Return(LuaValue.Nil, error);
            return new ValueTask<int>(2);
        }

        context.Return(true);
        return new ValueTask<int>(1);
    }
}
