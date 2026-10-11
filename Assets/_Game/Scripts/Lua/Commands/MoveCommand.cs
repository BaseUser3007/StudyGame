using Lua;
using System.Threading.Tasks;
using UnityEngine;

public class MoveCommand : PlayerCommandBase
{
    public override string Name => "move";
    public override float EnergyCost => 1f;
    public override string Signature => "move(direction, steps)";
    public override string Description => "Идти в направлении: north, south, east, west, northeast, northwest, southeast, southwest. Вода останавливает, объекты обходятся; если застрял — стоп через пару секунд. Возвращает число пройденных тайлов";

    public MoveCommand(PlayerCommandExecutor executor, IMapDataProvider mapData)
        : base(executor, mapData) { }

    public override void Register(LuaState state)
    {
        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            string direction = context.GetArgument<string>(0);
            int steps = context.GetArgument<int>(1);

            if (!TryParseDirection(direction, out Direction dir))
            {
                Debug.LogWarning($"move: unknown direction '{direction}'");
                context.Return(0);
                return 1;
            }

            int stepsTaken = await Executor.MoveAsync(dir, steps, ct);
            context.Return(stepsTaken);
            return 1;
        });
    }

    private bool TryParseDirection(string direction, out Direction result)
    {
        switch (direction.ToLower())
        {
            case "north": result = Direction.North; return true;
            case "south": result = Direction.South; return true;
            case "east": result = Direction.East; return true;
            case "west": result = Direction.West; return true;
            case "northeast": result = Direction.NorthEast; return true;
            case "northwest": result = Direction.NorthWest; return true;
            case "southeast": result = Direction.SouthEast; return true;
            case "southwest": result = Direction.SouthWest; return true;
            default: result = Direction.North; return false;
        }
    }
}