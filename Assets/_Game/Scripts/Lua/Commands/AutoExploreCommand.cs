using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// Готовая команда разведки (план — <see cref="ExplorePlanner"/>).
/// autoExplore() — один шаг: до 6 тайлов туда, где откроется больше всего никогда не виденной суши
/// (если вокруг всё открыто — к ближайшей границе тумана). Возвращает число открытых тайлов суши;
/// если идти некуда — 0 и причину.
/// autoExplore(type) — шаги разведки (не больше <see cref="MaxStepsPerCall"/>), пока в поле зрения
/// не появится объект/враг/головоломка типа; возвращает таблицу ближайшего (как getNear) или nil и причину.
/// Первый шаг входит в цену вызова, каждый следующий списывает ещё <see cref="EnergyCost"/> через Meter —
/// как и всё остальное: не хватает энергии — EnergyMeter останавливает программу.
/// </summary>
public class AutoExploreCommand : PlayerCommandBase
{
    /// <summary>Сколько шагов разведки autoExplore(type) делает за один вызов.</summary>
    public const int MaxStepsPerCall = 3;

    /// <summary>Цена вызова и каждого следующего шага (константа — её подставляет и русское описание).</summary>
    // Готовая команда: аналог из базовых — check + move(dir, 6) ≈ 2–3 энергии; наценка за удобство (GameDesign §9, решение 3)
    public const float CallCost = 10f;

    public override string Name => "autoExplore";
    public override float EnergyCost => CallCost;
    public override bool IsReadyMade => true;
    public override string Signature => "autoExplore([type])";
    public override string Description =>
        "Ready-made scouting command: walks up to 6 tiles toward where the most unseen land opens up " +
        "(if everything around is revealed — to the nearest fog edge). Returns the number of land tiles revealed. " +
        $"With a type (stick, tree, goblin, obelisk…) — scouts until such an object comes into view (up to {MaxStepsPerCall} steps) " +
        "and returns the nearest one, like findObjects(type):getNear(), or nil and a reason. " +
        $"Each step after the first costs {CallCost:0} more energy. Costs more than check + move";

    private readonly IFogProvider _fog;
    private readonly ObjectManager _objectManager;
    private readonly EnemyManager _enemies;
    private readonly PuzzleManager _puzzles;
    private readonly LuaObjectList _objectList;

    public AutoExploreCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        IFogProvider fog,
        ObjectManager objectManager,
        EnemyManager enemies = null,
        PuzzleManager puzzles = null)
        : base(executor, mapData)
    {
        _fog = fog;
        _objectManager = objectManager;
        // Врагов и головоломок может не быть (тесты без сцены, лагерь) — тогда их «не видно»
        _enemies = enemies;
        _puzzles = puzzles;
        _objectList = new LuaObjectList(executor);
    }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        bool hasArgument = context.ArgumentCount > 0 && context.GetArgument(0).Type != LuaValueType.Nil;
        if (!hasArgument)
            return await ExploreOnceAsync(context, ct);

        if (!context.GetArgument(0).TryRead(out string typeName))
        {
            context.Return(LuaValue.Nil, "usage: autoExplore() or autoExplore(\"type\")");
            return 2;
        }

        if (!TryParseTarget(typeName, out Target target))
        {
            context.Return(LuaValue.Nil, $"unknown type: {typeName}");
            return 2;
        }

        return await ExploreForAsync(context, target, ct);
    }

    // ─── Разведка ─────────────────────────────────────────────

    private async ValueTask<int> ExploreOnceAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        if (!ExplorePlanner.TryPlan(MapData, _fog, Executor.CurrentTile, Executor.VisionRadius, out ExplorePlanner.Plan plan))
        {
            context.Return(0d, "nothing left to explore nearby");
            return 2;
        }

        int revealed = await StepAsync(plan, ct);
        context.Return((double)revealed);
        return 1;
    }

    private async ValueTask<int> ExploreForAsync(LuaFunctionExecutionContext context, Target target, CancellationToken ct)
    {
        // Уже видно — шагать не нужно
        LuaTable found = FindNearestVisible(target);

        for (int step = 0; found == null && step < MaxStepsPerCall; step++)
        {
            if (!ExplorePlanner.TryPlan(MapData, _fog, Executor.CurrentTile, Executor.VisionRadius, out ExplorePlanner.Plan plan))
                break;

            // Первый шаг оплачен вызовом, следующие — отдельно (бросит OutOfEnergyException, если не хватает)
            if (step > 0)
                Meter?.Charge(EnergyCost, $"{Name}() step {step + 1}");

            await StepAsync(plan, ct);
            found = FindNearestVisible(target);
        }

        if (found != null)
        {
            context.Return(found);
            return 1;
        }

        context.Return(LuaValue.Nil, "not found");
        return 2;
    }

    /// <summary>
    /// Идёт к цели плана и возвращает, сколько тайлов суши на самом деле открылось.
    /// </summary>
    private async ValueTask<int> StepAsync(ExplorePlanner.Plan plan, CancellationToken ct)
    {
        Vector2Int start = Executor.CurrentTile;
        // Квадрат с запасом: цель не дальше StepTiles, обзор — VisionRadius, +1 на скольжение вдоль объектов
        int radius = ExplorePlanner.StepTiles + Executor.VisionRadius + 1;
        int before = ExplorePlanner.CountUnexploredLand(MapData, _fog, start, radius);

        await Executor.MoveToAsync(plan.Target, ct);

        int after = ExplorePlanner.CountUnexploredLand(MapData, _fog, start, radius);
        return Mathf.Max(0, before - after);
    }

    // ─── Поиск цели ───────────────────────────────────────────

    private enum TargetKind { Object, Enemy, Puzzle }

    private readonly struct Target
    {
        public readonly TargetKind Kind;
        public readonly Type ObjectType;
        public readonly string Name;

        public Target(TargetKind kind, Type objectType, string name)
        {
            Kind = kind;
            ObjectType = objectType;
            Name = name;
        }
    }

    /// <summary>
    /// Тип из Lua — как в findObjects: ресурс, головоломка или враг (enemy, известный или встреченный тип).
    /// </summary>
    private bool TryParseTarget(string typeName, out Target target)
    {
        target = default;
        if (string.IsNullOrEmpty(typeName)) return false;

        if (FindObjectsCommand.TryGetType(typeName, out Type type))
            target = new Target(TargetKind.Object, type, null);
        else if (LuaPuzzles.TryParseType(typeName, out string puzzleTypeId))
            target = new Target(TargetKind.Puzzle, null, puzzleTypeId);
        else if (FindObjectsCommand.IsEnemyType(typeName) || EnemyTypeExists(typeName))
            target = new Target(TargetKind.Enemy, null, typeName);
        else
            return false;

        return true;
    }

    /// <summary>
    /// Ближайший видимый объект типа — таблица, как у findObjects(type):getNear(); null — не видно.
    /// </summary>
    private LuaTable FindNearestVisible(Target target)
    {
        List<ResourceObject> objects = null;
        List<Enemy> enemies = null;
        List<PuzzleStructure> puzzles = null;

        switch (target.Kind)
        {
            case TargetKind.Object:
                objects = _objectManager != null
                    ? _objectManager.GetObjectsByType(Executor.CurrentTile, Executor.VisionRadius, target.ObjectType)
                    : new List<ResourceObject>();
                break;
            case TargetKind.Puzzle:
                puzzles = LuaPuzzles.GetVisible(_puzzles, _fog, Executor.Position, target.Name);
                break;
            case TargetKind.Enemy:
                enemies = GetVisibleEnemies(target.Name);
                break;
        }

        if ((objects?.Count ?? 0) + (enemies?.Count ?? 0) + (puzzles?.Count ?? 0) == 0)
            return null;

        // Таблицы строит тот же LuaObjectList, что и findObjects; методы списка не вызываются — ничего не списывается
        LuaTable list = _objectList.Build(objects, enemies, puzzles, Meter);
        LuaTable nearest = null;
        double best = double.MaxValue;
        int count = list["count"].TryRead(out double n) ? (int)n : 0;
        for (int i = 1; i <= count; i++)
        {
            if (!list[i].TryRead(out LuaTable item)) continue;
            double distance = item["distance"].TryRead(out double d) ? d : double.MaxValue;
            if (nearest != null && distance >= best) continue;
            best = distance;
            nearest = item;
        }

        return nearest;
    }

    /// <summary>Живые враги на видимых тайлах; typeName — "enemy" (все) или TypeId.</summary>
    private List<Enemy> GetVisibleEnemies(string typeName)
    {
        var result = new List<Enemy>();
        if (_enemies == null) return result;

        bool any = string.Equals(typeName, FindObjectsCommand.EnemyTypeName, StringComparison.OrdinalIgnoreCase);
        foreach (Enemy enemy in _enemies.GetEnemiesInRadius(Executor.Position, float.MaxValue))
        {
            if (!enemy.IsVisible || !enemy.IsAlive) continue;
            if (!any && !string.Equals(enemy.TypeId, typeName, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(enemy);
        }
        return result;
    }

    private bool EnemyTypeExists(string typeName)
    {
        if (_enemies == null) return false;
        foreach (Enemy enemy in _enemies.Enemies)
            if (string.Equals(enemy.TypeId, typeName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
