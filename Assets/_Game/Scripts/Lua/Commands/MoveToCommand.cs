using Lua;
using System.Threading.Tasks;
using UnityEngine;

public class MoveToCommand : PlayerCommandBase
{
    public override string Name => "moveTo";
    public override float EnergyCost => 2f;

    public MoveToCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    public override void Register(LuaState state)
    {
        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            int x = context.GetArgument<int>(0);
            int y = context.GetArgument<int>(1);

            int stepsTaken = await Executor.MoveToAsync(new Vector2Int(x, y));
            context.Return(stepsTaken);
            return 1;
        });
    }
}