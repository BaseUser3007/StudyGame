using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// check(x, y) / check(obj) / check("direction", [distance]) — что находится в тайле.
/// Тайл должен быть виден: под туманом (в том числе полупрозрачным, уже исследованным) не проверить.
/// Головоломки: стена лабиринта — type/structure "wall" и walkable = false (сам тайл проходим, стоит
/// препятствие с коллайдером), обелиск — "obelisk", центр лабиринта — "chest", плита — "plate" (+ поля plate — номер,
/// pressed — нажата), стартовая платформа (T25) — "start_platform"; поле puzzle — таблица головоломки, если тайл
/// в её области или на её платформе.
/// Портал забега: type/structure "portal", walkable = false (коллайдер сплошной, кроме запасного случая
/// RunPortal.IsSolid = false), поле portal — его таблица (как элемент findObjects).
/// </summary>
public class CheckCommand : PlayerCommandBase
{
    public override string Name => "check";
    public override float EnergyCost => 1f;
    public override string Signature => "check(x, y) or check(direction, [distance])";
    public override string Description =>
        "What is in a tile: check(x, y), check(obj) or check(\"north\", 2) — relative to the player " +
        "(directions as in move, distance in tiles, 1 by default, diagonally — steps along both axes). " +
        "Only tiles without fog. Returns a table { x, y, type, terrain, walkable, biome, structure, player, objects, enemies, puzzle, portal } " +
        "or nil and a reason. type is the main thing in the tile: a structure (maze wall — \"wall\", obelisk — \"obelisk\", maze center — \"chest\", pressure plate — \"plate\", puzzle start platform — \"start_platform\", run portal — \"portal\"), " +
        "else an enemy (its type, e.g. \"goblin\"), else the first object, else \"water\" or \"empty\"; " +
        "walkable = false on a maze wall and on the portal; objects — the resources in the tile, enemies — the enemies in it (with the same methods as findObjects); " +
        "puzzle — the puzzle whose area or start platform contains the tile (like a findObjects item), or nil; portal — the portal if it stands on the tile, or nil; " +
        "on a pressure plate also plate — its number, and pressed — whether it is pressed";

    private const string UsageMessage = "usage: check(x, y), check(obj) or check(direction, [distance])";

    private readonly IFogProvider _fog;
    private readonly ObjectManager _objectManager;
    private readonly StructureManager _structures;
    private readonly EnemyManager _enemies;
    private readonly PuzzleManager _puzzles;
    private readonly RunPortal _portal;
    private readonly LuaObjectList _objectList;

    public CheckCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        IFogProvider fog,
        ObjectManager objectManager,
        StructureManager structures,
        EnemyManager enemies,
        PuzzleManager puzzles = null,
        RunPortal portal = null)
        : base(executor, mapData)
    {
        _fog = fog;
        _objectManager = objectManager;
        _structures = structures;
        _enemies = enemies;
        _puzzles = puzzles;
        // Портала нет в лагере и в тестах без сцены
        _portal = portal;
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
        Structure structure = _structures != null ? _structures.GetStructureAt(tile) : null;
        List<ResourceObject> objects = _objectManager != null ? _objectManager.GetObjectsAtTile(tile) : new List<ResourceObject>();
        // Стена лабиринта: тайл карты проходим, но на нём препятствие с коллайдером
        PuzzleStructure puzzle = _puzzles?.GetPuzzleAt(tile);
        bool mazeWall = _puzzles != null && _puzzles.IsMazeWall(tile);
        // Стартовая платформа (T25): встал — головоломка начинается; поле puzzle — её головоломка
        PuzzleStructure startOf = LuaPuzzles.GetByStartTile(_puzzles, tile);
        if (startOf != null) puzzle = startOf;
        // Портал: тайлы карты под ним проходимы, но коллайдер портала сплошной
        bool onPortal = _portal != null && _portal.ContainsTile(tile);
        LuaTable objectList = _objectList.Build(objects, Meter);
        // Тайл виден (проверено выше) — враги в нём видны тоже
        List<Enemy> enemies = _enemies != null ? _enemies.GetEnemiesAtTile(tile) : new List<Enemy>();
        LuaTable enemyList = _objectList.Build(null, enemies, Meter);

        var table = new LuaTable();
        table["x"] = (double)tile.x;
        table["y"] = (double)tile.y;
        table["terrain"] = water ? "water" : "grass";
        table["walkable"] = MapData.IsWalkable(tile.x, tile.y) && !mazeWall && !(onPortal && _portal.IsSolid);
        table["player"] = Executor.CurrentTile == tile;
        table["objects"] = objectList;
        table["enemies"] = enemyList;

        Biome biome = MapData.GetBiome(tile.x, tile.y);
        if (biome != null)
            table["biome"] = biome.name;

        if (puzzle != null)
            table["puzzle"] = _objectList.BuildPuzzleTable(puzzle);
        if (onPortal)
            table["portal"] = _objectList.BuildPortalTable(_portal);

        // Номер плиты виден только здесь: check(x, y).plate
        if (puzzle is PlatesPuzzle plates && plates.TryGetPlate(tile, out int plateNumber, out bool platePressed))
        {
            table["plate"] = (double)plateNumber;
            table["pressed"] = platePressed;
        }

        string structureName = (startOf != null && !mazeWall ? LuaPuzzles.StartPlatformName : null)
            ?? GetPuzzleStructureName(puzzle, tile, mazeWall)
            ?? (onPortal ? LuaPortal.TypeName : null)
            ?? (structure != null ? GetStructureName(structure) : null);
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

    /// <summary>
    /// Часть головоломки на тайле: "wall" (стена лабиринта), "chest" (центр лабиринта), "obelisk", "plate"; иначе null
    /// (проходы лабиринта — обычные тайлы).
    /// </summary>
    private static string GetPuzzleStructureName(PuzzleStructure puzzle, Vector2Int tile, bool mazeWall)
    {
        if (mazeWall) return LuaPuzzles.WallName;
        if (puzzle is MazePuzzle maze && maze.Layout != null && maze.CenterTile == tile) return LuaPuzzles.ChestName;
        if (puzzle is CodeTaskPuzzle) return LuaPuzzles.ObeliskTypeName;
        if (puzzle is PlatesPuzzle plates && plates.IsPlate(tile)) return LuaPuzzles.PlateName;
        return null;
    }

    private static string GetStructureName(Structure structure)
    {
        StructureItem item = structure.Item;
        if (item == null) return structure.name.ToLower();
        return string.IsNullOrEmpty(item.itemId) ? item.DisplayName.ToLower() : item.itemId;
    }
}
