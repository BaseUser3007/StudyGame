using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// check(x, y) / check(obj) / check("direction", [distance]) — что находится в тайле.
/// Тайл должен быть виден: под туманом (в том числе полупрозрачным, уже исследованным) не проверить.
/// </summary>
public class CheckCommand : PlayerCommandBase
{
    public override string Name => "check";
    public override float EnergyCost => 1f;
    public override string Signature => "check(x, y) or check(direction, [distance])";
    public override string Description =>
        "Что находится в тайле: check(x, y), check(obj) или check(\"north\", 2) — относительно персонажа " +
        "(направления как у move, дистанция в тайлах, по умолчанию 1, по диагонали — шаги по обеим осям). " +
        "Только тайлы без тумана. Возвращает таблицу { x, y, type, terrain, walkable, biome, structure, player, objects, enemies } " +
        "или nil и причину. type — главное в тайле: постройка, иначе враг (его тип, например \"goblin\"), иначе первый объект, иначе \"water\" или \"empty\"; " +
        "objects — массив ресурсов тайла, enemies — массив врагов в нём (с теми же методами, что у findObjects)";

    private const string UsageMessage = "usage: check(x, y), check(obj) or check(direction, [distance])";

    private readonly IFogProvider _fog;
    private readonly ObjectManager _objectManager;
    private readonly StructureManager _structures;
    private readonly EnemyManager _enemies;
    private readonly LuaObjectList _objectList;

    public CheckCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        IFogProvider fog,
        ObjectManager objectManager,
        StructureManager structures,
        EnemyManager enemies)
        : base(executor, mapData)
    {
        _fog = fog;
        _objectManager = objectManager;
        _structures = structures;
        _enemies = enemies;
        _objectList = new LuaObjectList(executor);
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        Vector2Int tile = ReadTile(context);

        string error = !MapData.IsInBounds(tile.x, tile.y) ? "out of map"
            : !_fog.IsVisible(tile.x, tile.y) ? "tile is covered by fog"
            : null;

        if (error != null)
        {
            context.Return(LuaValue.Nil, error);
            return new ValueTask<int>(2);
        }

        context.Return(BuildTileTable(tile));
        return new ValueTask<int>(1);
    }

    // ─── Arguments ────────────────────────────────────────────

    private Vector2Int ReadTile(LuaFunctionExecutionContext context)
    {
        if (context.ArgumentCount == 0)
            throw new ArgumentException($"check: {UsageMessage}");

        // check("north", 2) — относительно персонажа
        if (context.GetArgument(0).TryRead(out string directionName))
        {
            if (!MoveCommand.TryParseDirection(directionName, out Direction direction))
                throw new ArgumentException(
                    $"check: unknown direction '{directionName}' " +
                    "(north, south, east, west, northeast, northwest, southeast, southwest)");

            int distance = ReadDistance(context);
            return Executor.CurrentTile + PlayerCommandExecutor.DirectionToVector(direction) * distance;
        }

        // check(x, y) или check(obj) — точка карты, берётся содержащий её тайл
        if (HarvestCommand.TryReadPoint(context, out Vector2 point))
            return MapPosition.ToTile(point);

        throw new ArgumentException($"check: {UsageMessage}");
    }

    private static int ReadDistance(LuaFunctionExecutionContext context)
    {
        if (context.ArgumentCount < 2 || context.GetArgument(1).Type == LuaValueType.Nil)
            return 1;

        if (!context.GetArgument(1).TryRead(out double value) || value < 0 || value != Math.Floor(value))
            throw new ArgumentException("check: distance must be a whole number >= 0");

        return (int)value;
    }

    // ─── Result ───────────────────────────────────────────────

    private LuaTable BuildTileTable(Vector2Int tile)
    {
        bool water = MapData.GetTileType(tile.x, tile.y) == TileType.Water;
        Structure structure = _structures.GetStructureAt(tile);
        List<ResourceObject> objects = _objectManager.GetObjectsAtTile(tile);
        LuaTable objectList = _objectList.Build(objects, Meter);
        // Тайл виден (проверено выше) — враги в нём видны тоже
        List<Enemy> enemies = _enemies != null ? _enemies.GetEnemiesAtTile(tile) : new List<Enemy>();
        LuaTable enemyList = _objectList.Build(null, enemies, Meter);

        var table = new LuaTable();
        table["x"] = (double)tile.x;
        table["y"] = (double)tile.y;
        table["terrain"] = water ? "water" : "grass";
        table["walkable"] = MapData.IsWalkable(tile.x, tile.y);
        table["player"] = Executor.CurrentTile == tile;
        table["objects"] = objectList;
        table["enemies"] = enemyList;

        Biome biome = MapData.GetBiome(tile.x, tile.y);
        if (biome != null)
            table["biome"] = biome.name;

        string structureName = structure != null ? GetStructureName(structure) : null;
        if (structureName != null)
            table["structure"] = structureName;

        // Главное в тайле — для коротких проверок: if check("north").type == "tree" then …
        // Враг важнее ресурса: if check("north").type == "goblin" then attack(…)
        table["type"] = structureName
            ?? (enemies.Count > 0 ? enemies[0].TypeId : null)
            ?? (objects.Count > 0 ? LuaObjectList.GetTypeName(objects[0]) : null)
            ?? (water ? "water" : "empty");

        return table;
    }

    private static string GetStructureName(Structure structure)
    {
        StructureItem item = structure.Item;
        if (item == null) return structure.name.ToLower();
        return string.IsNullOrEmpty(item.itemId) ? item.DisplayName.ToLower() : item.itemId;
    }
}
