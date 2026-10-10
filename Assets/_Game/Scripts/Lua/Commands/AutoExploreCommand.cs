using Lua;
using UnityEngine;

public class AutoExploreCommand : PlayerCommandBase
{
    public override string Name => "autoExplore";
    public override float EnergyCost => 25f;

    public AutoExploreCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    public override void Register(LuaState state)
    {
        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            Direction[] directions = (Direction[])System.Enum.GetValues(typeof(Direction));
            Direction random = directions[Random.Range(0, directions.Length)];
            await Executor.MoveAsync(random, 5);
            context.Return();
            return 0;
        });
    }
}