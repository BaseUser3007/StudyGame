using System.Threading;
using System.Threading.Tasks;
using Lua;
using UnityEngine;

/// <summary>
/// attack([obj | x, y]) — ударить врага лучшим оружием из инвентаря или кулаком (CombatService).
/// Удар длится CombatService.StrikeDuration: урон сразу, затем команда ждёт (враги в это время действуют).
/// Энергия — только цена вызова; цена боя — здоровье. Открывается достижением (enemy_spotted).
/// </summary>
public class AttackCommand : PlayerCommandBase
{
    public override string Name => "attack";
    // Действие в мире, как moveTo (GameDesign §10.1)
    public override float EnergyCost => 2f;
    public override string Signature => "attack([obj | x, y])";
    public override string Description =>
        "Hit an enemy: attack(enemy) — an enemy from findObjects/check, attack(x, y) — the enemy at a point, " +
        "attack() — the nearest one in reach. Uses the best weapon in the inventory (a sword wears out) " +
        "or a fist (5 damage); reach is 1.5 tiles, does not walk up to the enemy. " +
        "A hit takes 0.5 s. Returns true, the damage and whether the enemy died, or nil and a reason";

    // Насколько далеко от указанной точки может стоять враг (как в CombatService.TryAttackAt)
    private const float PickTolerance = 0.75f;

    private readonly EnemyManager _enemies;
    private readonly CombatService _combat;
    private readonly PuzzleManager _puzzles;
    private readonly RunPortal _portal;

    public AttackCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        EnemyManager enemies,
        CombatService combat,
        PuzzleManager puzzles = null,
        RunPortal portal = null)
        : base(executor, mapData)
    {
        _enemies = enemies;
        _combat = combat;
        _puzzles = puzzles;
        _portal = portal;
    }

    protected override async ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        Enemy target = FindTarget(context, out string error);

        if (target != null)
        {
            var (success, result, attackError) = await _combat.AttackAsync(target, ct);
            if (success)
            {
                context.Return(true, (double)result.Damage, result.Killed);
                return 3;
            }

            error = attackError;
        }

        context.Return(LuaValue.Nil, error ?? "no enemy");
        return 2;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Для harvest/mine: цель — враг (таблица врага или точка, где нет ресурса, но стоит враг) или головоломка
    /// (таблица головоломки, стена лабиринта или обелиск в точке без ресурса) или портал (его таблица или точка на нём)?
    /// Возвращает понятную причину или null, если цель обычная. action — "harvested" / "mined".
    /// </summary>
    public static string GetNotHarvestableError(
        LuaFunctionExecutionContext context, Vector2 point, ObjectManager objects, EnemyManager enemies,
        string action, PuzzleManager puzzles = null, RunPortal portal = null)
    {
        if (context.GetArgument(0).TryRead(out LuaTable table) && LuaObjectList.TryGetEnemyId(table, out _))
            return $"{ReadType(table)} can't be {action} — use attack()";

        string puzzleError = LuaPuzzles.GetNotTargetError(context, point, puzzles, action, checkPoint: false);
        if (puzzleError != null) return puzzleError;

        string portalError = LuaPortal.GetNotTargetError(context, point, portal, action, checkPoint: false);
        if (portalError != null) return portalError;

        // Ресурс в точке важнее: harvest(x, y) по камню рядом с гоблином работает как раньше
        ResourceObject obj = objects != null ? objects.GetNearestObjectAt(point, 0.5f) : null;
        if (obj != null && obj.Variant != null) return null;

        Enemy enemy = enemies?.GetNearestEnemy(point, PickTolerance);
        if (enemy != null) return $"{enemy.TypeId} can't be {action} — use attack()";

        return LuaPuzzles.GetNotTargetError(context, point, puzzles, action, checkPoint: true)
               ?? LuaPortal.GetNotTargetError(context, point, portal, action, checkPoint: true);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private Enemy FindTarget(LuaFunctionExecutionContext context, out string error)
    {
        error = null;
        Enemy target;

        if (context.ArgumentCount == 0 || context.GetArgument(0).Type == LuaValueType.Nil)
        {
            target = _enemies?.GetNearestEnemy(Executor.Position, _combat.AttackRadius);
            if (target == null) error = "no enemy within reach";
            return target;
        }

        // attack(enemy) — по id, а не по точке: враг мог уйти с того места, где его нашли
        if (context.GetArgument(0).TryRead(out LuaTable table) && LuaObjectList.TryGetEnemyId(table, out int enemyId))
        {
            target = _enemies?.GetEnemyById(enemyId);
            if (target == null) error = "enemy is gone";
            return target;
        }

        // attack(obelisk) / attack(maze) — головоломку не бьют
        error = LuaPuzzles.GetNotTargetError(context, default, _puzzles, "attacked", checkPoint: false)
                ?? LuaPortal.GetNotTargetError(context, default, _portal, "attacked", checkPoint: false);
        if (error != null) return null;

        if (HarvestCommand.TryReadPoint(context, out Vector2 point))
        {
            target = _enemies?.GetNearestEnemy(point, PickTolerance);
            if (target == null)
                error = LuaPuzzles.GetNotTargetError(context, point, _puzzles, "attacked", checkPoint: true)
                        ?? LuaPortal.GetNotTargetError(context, point, _portal, "attacked", checkPoint: true)
                        ?? System.FormattableString.Invariant($"no enemy at ({point.x:0.##}, {point.y:0.##})");
            return target;
        }

        error = "usage: attack(), attack(x, y) or attack(enemy)";
        return null;
    }

    private static string ReadType(LuaTable table)
    {
        return table["type"].TryRead(out string type) && !string.IsNullOrEmpty(type) ? type : "enemy";
    }
}
