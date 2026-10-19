using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;

/// <summary>
/// findObjects([type]) — ресурсы и враги в поле зрения в виде LuaObjectList.
/// Враги (тип "enemy" — все, или по EnemyDefinition.enemyId, например "goblin") попадают в список, только
/// если стоят на видимом тайле (Enemy.IsVisible); погибшие пропускаются. Головоломки ("obelisk"/"code_task",
/// "maze", "puzzle" — все) — если хоть один тайл их области без тумана; решённые остаются (solved = true).
/// Без аргумента — всё сразу.
/// </summary>
public class FindObjectsCommand : PlayerCommandBase
{
    /// <summary>Тип-«все враги» для findObjects и obtain/harvest-проверок.</summary>
    public const string EnemyTypeName = LuaObjectList.KindEnemy;

    public override string Name => "findObjects";
    public override float EnergyCost => 1f;
    public override string Signature => "findObjects([type])";
    public override string Description => "Массив объектов типа в поле зрения: tree, stone, boulder, gold_ore, berry_bush, mushroom, sheep, stick, pebble, log, flint (выброшенные); " +
                                          "враги — enemy (все) или goblin; головоломки — obelisk, maze или puzzle (все). " +
                                          "Без аргумента — всё, что видно, вместе с врагами и головоломками. " +
                                          $"У объекта есть поля {LuaObjectList.Fields} (kind — \"object\", \"enemy\" или \"puzzle\"; " +
                                          "у лабиринта ещё entranceX, entranceY — тайл перед входом). " +
                                          "У массива есть count и методы для поиска, фильтрации и сортировки " +
                                          "(getNear, getFar, getMin, getMax, where, sortBy, take, find …) — см. раздел в справке";

    private readonly ObjectManager _objectManager;
    private readonly EnemyManager _enemies;
    private readonly IFogProvider _fog;
    private readonly PuzzleManager _puzzles;
    private readonly LuaObjectList _objectList;

    private static readonly Dictionary<string, Type> TypeMap
        = new Dictionary<string, Type>
    {
        { "tree",     typeof(TreeObject)     },
        { "stone",    typeof(StoneObject)    },
        { "berry_bush", typeof(BerryBushObject) },
        { "mushroom", typeof(MushroomObject) },
        { "sheep",    typeof(SheepObject)    },
        { "stick",    typeof(StickObject)    },
        { "pebble",   typeof(PebbleObject)   },
        { "boulder",  typeof(BoulderObject)  },
        { "gold_ore", typeof(GoldOreObject)  },
        { "gold",     typeof(GoldObject)     },
        { "log",      typeof(LogObject)      },
        { "flint",    typeof(FlintObject)    },
        { "meat",     typeof(MeatObject)     }
    };

    // Старые имена типов: сниппеты в библиотеке кода, написанные до переименования, продолжают работать.
    // В список известных типов (ошибки, справка) не попадают
    private static readonly Dictionary<string, string> LegacyTypeNames
        = new Dictionary<string, string>
    {
        { "bush",   "berry_bush" },
        { "rabbit", "sheep"      }
    };

    // Типы врагов, известные заранее (для подсказки в ошибке); остальные определяются по EnemyDefinition.enemyId
    private static readonly string[] KnownEnemyTypes = { "goblin", "goblin_guard" };

    public FindObjectsCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        ObjectManager objectManager,
        EnemyManager enemies,
        IFogProvider fog = null,
        PuzzleManager puzzles = null)
        : base(executor, mapData)
    {
        _objectManager = objectManager;
        _enemies = enemies;
        // Головоломок может не быть (тесты без сцены); без тумана видны все
        _fog = fog;
        _puzzles = puzzles;
        _objectList = new LuaObjectList(executor);
    }

    /// <summary>
    /// Класс объектов по имени типа из Lua ("stone" → StoneObject). Используется и другими командами.
    /// </summary>
    public static bool TryGetType(string typeName, out Type type)
    {
        type = null;
        if (string.IsNullOrEmpty(typeName)) return false;

        string lower = typeName.ToLower();
        if (LegacyTypeNames.TryGetValue(lower, out string current)) lower = current;
        return TypeMap.TryGetValue(lower, out type);
    }

    /// <summary>
    /// Имя — тип врага: "enemy" или один из известных (goblin).
    /// </summary>
    public static bool IsEnemyType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        string lower = typeName.ToLower();
        return lower == EnemyTypeName || Array.IndexOf(KnownEnemyTypes, lower) >= 0;
    }

    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        bool hasArgument = context.ArgumentCount > 0 && context.GetArgument(0).Type != LuaValueType.Nil;
        string typeName = hasArgument && context.GetArgument(0).TryRead(out string name) ? name : null;

        List<ResourceObject> objects = null;
        List<Enemy> enemies = null;
        List<PuzzleStructure> puzzles = null;

        if (!hasArgument)
        {
            // Всё в поле зрения: ресурсы любого типа, видимые враги и головоломки
            objects = _objectManager.GetObjectsInRadius<ResourceObject>(Executor.CurrentTile, Executor.VisionRadius);
            enemies = GetVisibleEnemies(null);
            puzzles = LuaPuzzles.GetVisible(_puzzles, _fog, Executor.Position, null);
        }
        else if (TryGetType(typeName, out Type type))
        {
            objects = _objectManager.GetObjectsByType(Executor.CurrentTile, Executor.VisionRadius, type);
        }
        else if (LuaPuzzles.TryParseType(typeName, out string puzzleTypeId))
        {
            puzzles = LuaPuzzles.GetVisible(_puzzles, _fog, Executor.Position, puzzleTypeId);
        }
        else
        {
            enemies = GetVisibleEnemies(typeName);
            // Имя не ресурс и не известный/встреченный тип врага — скорее всего опечатка
            if (enemies.Count == 0 && !IsEnemyType(typeName) && !EnemyTypeExists(typeName))
                throw new ArgumentException(
                    $"findObjects: unknown type '{typeName}' " +
                    $"(known: {string.Join(", ", TypeMap.Keys)}, {EnemyTypeName}, {string.Join(", ", KnownEnemyTypes)}, " +
                    $"{LuaPuzzles.KnownTypeNames})");
        }

        context.Return(_objectList.Build(objects, enemies, puzzles, Meter));
        return new ValueTask<int>(1);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Живые враги на видимых тайлах; typeName — "enemy"/null (все) или TypeId, ближние — первыми.
    /// </summary>
    private List<Enemy> GetVisibleEnemies(string typeName)
    {
        var result = new List<Enemy>();
        if (_enemies == null) return result;

        bool any = string.IsNullOrEmpty(typeName)
                   || string.Equals(typeName, EnemyTypeName, StringComparison.OrdinalIgnoreCase);

        foreach (Enemy enemy in _enemies.GetEnemiesInRadius(Executor.Position, float.MaxValue))
        {
            if (!enemy.IsVisible) continue;
            if (!any && !string.Equals(enemy.TypeId, typeName, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(enemy);
        }

        return result;
    }

    // Тип врага есть на карте (в том числе в тумане) — не опечатка, просто никого не видно
    private bool EnemyTypeExists(string typeName)
    {
        if (_enemies == null) return false;
        foreach (Enemy enemy in _enemies.Enemies)
            if (string.Equals(enemy.TypeId, typeName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
