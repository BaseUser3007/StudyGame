using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;

public class FindObjectsCommand : PlayerCommandBase
{
    public override string Name => "findObjects";
    public override float EnergyCost => 1f;
    public override string Signature => "findObjects(type)";
    public override string Description => "Массив объектов типа в поле зрения: tree, stone, boulder, bush, mushroom, rabbit, stick, pebble, log, flint (выброшенные). " +
                                          $"У объекта есть поля {LuaObjectList.Fields}. " +
                                          "У массива есть count и методы для поиска, фильтрации и сортировки " +
                                          "(getNear, getFar, getMin, getMax, where, sortBy, take, find …) — см. раздел в справке";

    private readonly ObjectManager _objectManager;
    private readonly LuaObjectList _objectList;

    private static readonly Dictionary<string, Type> TypeMap
        = new Dictionary<string, Type>
    {
        { "tree",     typeof(TreeObject)     },
        { "stone",    typeof(StoneObject)    },
        { "bush",     typeof(BushObject)     },
        { "mushroom", typeof(MushroomObject) },
        { "rabbit",   typeof(RabbitObject)   },
        { "stick",    typeof(StickObject)    },
        { "pebble",   typeof(PebbleObject)   },
        { "boulder",  typeof(BoulderObject)  },
        { "log",      typeof(LogObject)      },
        { "flint",    typeof(FlintObject)    }
    };

    public FindObjectsCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager)
        : base(executor, mapData)
    {
        _objectManager = objectManager;
        _objectList = new LuaObjectList(executor);
    }

    /// <summary>
    /// Класс объектов по имени типа из Lua ("stone" → StoneObject). Используется и другими командами.
    /// </summary>
    public static bool TryGetType(string typeName, out Type type)
    {
        type = null;
        return !string.IsNullOrEmpty(typeName) && TypeMap.TryGetValue(typeName.ToLower(), out type);
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        string typeName = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out string name) ? name : null;

        if (!TryGetType(typeName, out Type type))
            throw new ArgumentException(
                $"findObjects: unknown type '{typeName}' (known: {string.Join(", ", TypeMap.Keys)})");

        // Объекты в радиусе поля зрения
        List<ResourceObject> objects = _objectManager.GetObjectsByType(
            Executor.CurrentTile, Executor.VisionRadius, type);

        context.Return(_objectList.Build(objects, Meter));
        return new ValueTask<int>(1);
    }
}
