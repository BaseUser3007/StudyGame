using System;
using System.Collections.Generic;
using Lua;
using UnityEngine;

public class FindObjectsCommand : PlayerCommandBase
{
    public override string Name => "findObjects";
    public override float EnergyCost => 0f;

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
        Vector2Int center = Executor.CurrentTile;
        int radius = 4; // поле зрения 8x8

        List<ResourceObject> all = _objectManager.GetObjectsByType(center, radius, type);
        return all;
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

                Vector2Int objTile = new Vector2Int(
                    Mathf.RoundToInt(obj.transform.position.x),
                    Mathf.RoundToInt(obj.transform.position.y));

                float distance = Vector2Int.Distance(center, objTile);

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

        Vector2Int tile = new Vector2Int(
            Mathf.RoundToInt(obj.transform.position.x),
            Mathf.RoundToInt(obj.transform.position.y));

        objTable["x"] = (double)tile.x;
        objTable["y"] = (double)tile.y;
        objTable["type"] = obj.GetType().Name.ToLower().Replace("object", "");

        return objTable;
    }
}