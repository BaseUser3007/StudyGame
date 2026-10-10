using System;
using System.Collections.Generic;
using Lua;
using UnityEngine;

public class FindObjectsCommand : PlayerCommandBase
{
    public override string Name => "findObjects";
    public override float EnergyCost => 0f;
    public override string Signature => "findObjects(type)";
    public override string Description => "Объекты типа в поле зрения: tree, stone, bush, mushroom, rabbit, stick, pebble. У результата есть метод getNear()";

    private readonly ObjectManager _objectManager;

    private static readonly Dictionary<string, Type> TypeMap
        = new Dictionary<string, Type>
    {
        { "tree",     typeof(TreeObject)     },
        { "stone",    typeof(StoneObject)    },
        { "bush",     typeof(BushObject)     },
        { "mushroom", typeof(MushroomObject) },
        { "rabbit",   typeof(RabbitObject)   },
        { "stick",    typeof(StickObject)    },
        { "pebble",   typeof(PebbleObject)   }
    };

    public FindObjectsCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager)
        : base(executor, mapData)
    {
        _objectManager = objectManager;
    }

    public override void Register(LuaState state)
    {
        state.Environment[Name] = new LuaFunction(async (context, ct) =>
        {
            string typeName = context.GetArgument<string>(0);

            if (!TypeMap.TryGetValue(typeName.ToLower(), out Type type))
            {
                Debug.LogWarning($"findObjects: unknown type '{typeName}'");
                context.Return(LuaValue.Nil);
                return 1;
            }

            // Получаем объекты в радиусе поля зрения
            List<ResourceObject> objects = GetObjectsByType(type);

            // Создаём Lua таблицу с объектами и методом getNear
            LuaTable result = BuildResultTable(state, objects);

            context.Return(result);
            return 1;
        });
    }

    // ─── Helpers ──────────────────────────────────────────────

    private List<ResourceObject> GetObjectsByType(Type type)
    {
        return _objectManager.GetObjectsByType(
            Executor.CurrentTile, Executor.VisionRadius, type);
    }

    private LuaTable BuildResultTable(LuaState state, List<ResourceObject> objects)
    {
        LuaTable table = new LuaTable();

        // Добавляем объекты по индексу
        for (int i = 0; i < objects.Count; i++)
        {
            LuaTable objTable = BuildObjectTable(objects[i]);
            table[i + 1] = objTable;
        }

        // Метод getNear — возвращает ближайший объект
        table["getNear"] = new LuaFunction(async (context, ct) =>
        {
            if (objects.Count == 0)
            {
                context.Return(LuaValue.Nil);
                return 1;
            }

            ResourceObject nearest = null;
            float minDistance = float.MaxValue;
            Vector2Int center = Executor.CurrentTile;

            foreach (ResourceObject obj in objects)
            {
                if (!obj.IsAvailable) continue;

                float distance = Vector2Int.Distance(center, obj.Tile);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = obj;
                }
            }

            if (nearest == null)
            {
                context.Return(LuaValue.Nil);
                return 1;
            }

            context.Return(BuildObjectTable(nearest));
            return 1;
        });

        return table;
    }

    private LuaTable BuildObjectTable(ResourceObject obj)
    {
        LuaTable objTable = new LuaTable();

        objTable["x"] = (double)obj.Tile.x;
        objTable["y"] = (double)obj.Tile.y;
        objTable["type"] = obj.GetType().Name.ToLower().Replace("object", "");

        return objTable;
    }
}