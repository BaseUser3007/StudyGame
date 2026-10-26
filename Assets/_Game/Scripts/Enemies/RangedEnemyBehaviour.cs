using UnityEngine;

/// <summary>
/// Дальний бой врага (лучник, EnemyDefinition.ranged): вместо «догнать и ударить» — держать дистанцию и стрелять.
/// Enemy создаёт стратегию в Setup и отдаёт ей тик, пока враг в бою (вход в бой и сброс погони — общие с ближним
/// врагом: радиус агрессии, loseChaseDistance, поводок). Решение «куда идти» — RangedEnemyPlanner (чистая логика),
/// здесь — состояние между тиками, перезарядка и выстрел через IEnemyWorld.ShootProjectile.
///   • игрок дальше AttackRadius — подход (State = Chase);
///   • в радиусе — замах AttackWindup при входе, выстрел, перезарядка AttackCooldown (State = Chase);
///   • игрок ближе KeepDistance — отход (State = Retreat, "retreat" для Lua) с той же коллизией, что у всех врагов,
///     не дальше поводка от дома; на отходе не стреляет. Упёрся (коллайдер, вода) или загнан к границе поводка —
///     стоит и стреляет в упор.
/// Фора в начале запуска (IEnemyWorld.CanAttack) и «время только во время кода» — как у ближнего врага.
/// </summary>
public sealed class RangedEnemyBehaviour
{
    // Столько секунд без продвижения на отходе — упёрся: какое-то время стоит и стреляет
    private const float RetreatStuckTime = 0.4f;
    private const float StandGroundTime = 1f;

    private readonly Enemy _enemy;
    private bool _retreating;
    private bool _aiming;
    private float _standGround;

    public RangedEnemyBehaviour(Enemy enemy)
    {
        _enemy = enemy;
    }

    /// <summary>Отходит от игрока (для Lua — "retreat").</summary>
    public bool IsRetreating => _retreating;

    /// <summary>Выдача из пула или выход из боя: забыть отход и прицеливание (перезарядка — общая, в Enemy).</summary>
    public void Reset()
    {
        _retreating = false;
        _aiming = false;
        _standGround = 0f;
    }

    /// <summary>
    /// Тик в бою. Перезарядку (Enemy.AttackTimer) Enemy уже уменьшил на deltaTime.
    /// </summary>
    public void Tick(Vector2 player, float deltaTime)
    {
        EnemyStats stats = _enemy.Stats;
        _standGround = Mathf.Max(0f, _standGround - deltaTime);

        RangedPlan plan = RangedEnemyPlanner.Plan(
            _enemy.Position, player, _enemy.Home, stats.KeepDistance, stats.AttackRadius, stats.LeashRadius,
            _retreating);

        // Упёрся при отходе — постоит и постреляет, потом попробует снова
        if (plan.Kind == RangedMoveKind.Retreat && _standGround > 0f)
            plan = new RangedPlan(RangedMoveKind.Hold, _enemy.Position, 0f,
                Vector2.Distance(_enemy.Position, player) <= stats.AttackRadius, cornered: true);

        switch (plan.Kind)
        {
            case RangedMoveKind.Retreat:
                Retreat(plan, stats, deltaTime);
                break;
            case RangedMoveKind.Approach:
                _retreating = false;
                _aiming = false;
                _enemy.SetState(EnemyState.Chase);
                _enemy.MoveTowards(plan.Target, plan.StopDistance, stats.ChaseSpeed, deltaTime);
                break;
            default:
                Hold(plan, player, stats);
                break;
        }
    }

    // ─── Behaviour ────────────────────────────────────────────

    private void Retreat(RangedPlan plan, EnemyStats stats, float deltaTime)
    {
        if (!_retreating) _enemy.ResetStuckTime();
        _retreating = true;
        _aiming = false;
        _enemy.SetState(EnemyState.Retreat);
        _enemy.MoveTowards(plan.Target, plan.StopDistance, stats.ChaseSpeed, deltaTime);

        if (_enemy.StuckTime >= RetreatStuckTime)
        {
            _standGround = StandGroundTime;
            _enemy.ResetStuckTime();
        }
    }

    private void Hold(RangedPlan plan, Vector2 player, EnemyStats stats)
    {
        _retreating = false;
        _enemy.SetState(EnemyState.Chase);
        _enemy.Face(player - _enemy.Position);

        if (!plan.CanShoot)
        {
            _aiming = false;
            return;
        }

        // Только встал на позицию — замах, игрок успевает среагировать кодом
        if (!_aiming)
        {
            _aiming = true;
            _enemy.AttackTimer = Mathf.Max(_enemy.AttackTimer, stats.AttackWindup);
        }

        if (_enemy.AttackTimer > 0f || !_enemy.World.CanAttack) return;

        _enemy.World.ShootProjectile(_enemy, player);
        _enemy.AttackTimer = stats.AttackCooldown;
        _enemy.PlayAttackAnimation();
    }
}
