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
    public override string Description => "Ударить врага: attack(enemy) — врага из findObjects/check, attack(x, y) — врага в точке, " +
                                          "attack() — ближайшего в досягаемости. Бьёт лучшим оружием из инвентаря (меч изнашивается) " +
                                          "или кулаком (5 урона); дальность удара — 1.5 тайла, сам к врагу не подходит. " +
                                          "Удар длится 0.5 с. Возвращает true, урон и погиб ли враг, или nil и причину";

    // Насколько далеко от указанной точки может стоять враг (как в CombatService.TryAttackAt)
    private const float PickTolerance = 0.75f;

    private readonly EnemyManager _enemies;
    private readonly CombatService _combat;

    public AttackCommand(
        PlayerCommandExecutor executor,
        IMapDataProvider mapData,
        EnemyManager enemies,
        CombatService combat)
        : base(executor, mapData)
    {
        _enemies = enemies;
        _combat = combat;
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
    /// Для harvest/mine: цель — враг (таблица врага или точка, где нет ресурса, но стоит враг)?
    /// Возвращает причину «use attack()» или null, если это не враг. action — "harvested" / "mined".
    /// </summary>
    public static string GetNotHarvestableError(
        LuaFunctionExecutionContext context, Vector2 point, ObjectManager objects, EnemyManager enemies,
        string action)
    {
        if (context.GetArgument(0).TryRead(out LuaTable table) && LuaObjectList.TryGetEnemyId(table, out _))
            return $"{ReadType(table)} can't be {action} — use attack()";

        if (enemies == null) return null;

        // Ресурс в точке важнее: harvest(x, y) по камню рядом с гоблином работает как раньше
        ResourceObject obj = objects != null ? objects.GetNearestObjectAt(point, 0.5f) : null;
        if (obj != null && obj.Variant != null) return null;

        Enemy enemy = enemies.GetNearestEnemy(point, PickTolerance);
        return enemy != null ? $"{enemy.TypeId} can't be {action} — use attack()" : null;
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

        if (HarvestCommand.TryReadPoint(context, out Vector2 point))
        {
            target = _enemies?.GetNearestEnemy(point, PickTolerance);
            if (target == null) error = $"no enemy at ({point.x:0.##}, {point.y:0.##})";
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
