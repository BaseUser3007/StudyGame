using UnityEngine;

/// <summary>
/// Что делает стрелок в бою на этом тике.
/// </summary>
public enum RangedMoveKind
{
    /// <summary>Стоит и целится (стреляет, если игрок в радиусе атаки).</summary>
    Hold,
    /// <summary>Подходит к игроку на дистанцию выстрела.</summary>
    Approach,
    /// <summary>Отходит от игрока, который подошёл ближе keepDistance.</summary>
    Retreat
}

/// <summary>
/// Решение стрелка на тик: куда идти (Target, остановиться в StopDistance от неё) и можно ли стрелять.
/// Cornered — хотел отойти, но некуда (поводок): стоит и стреляет в упор.
/// </summary>
public readonly struct RangedPlan
{
    public readonly RangedMoveKind Kind;
    public readonly Vector2 Target;
    public readonly float StopDistance;
    public readonly bool CanShoot;
    public readonly bool Cornered;

    public RangedPlan(RangedMoveKind kind, Vector2 target, float stopDistance, bool canShoot, bool cornered = false)
    {
        Kind = kind;
        Target = target;
        StopDistance = stopDistance;
        CanShoot = canShoot;
        Cornered = cornered;
    }
}

/// <summary>
/// Чистая логика дальнего боя (без сцены, покрыта EditMode-тестами RangedEnemyPlannerTests):
///   • игрок дальше attackRange — подойти на PreferredDistance (не дальше поводка от дома, как ближний враг);
///   • игрок ближе keepDistance — отойти от него (с гистерезисом RetreatHysteresis, чтобы не дёргаться на границе),
///     цель отхода — внутри круга поводка; прямо назад не выйти — пробуем в стороны (вдоль границы поводка);
///     совсем некуда — «загнан»: стоит и стреляет;
///   • между ними — стоит и стреляет.
/// Коллизии и вода здесь не учитываются — их обрабатывает движение врага (Collider2D.Cast + скольжение).
/// </summary>
public static class RangedEnemyPlanner
{
    /// <summary>Отходя, стрелок останавливается дальше keepDistance на столько (в тайлах).</summary>
    public const float RetreatHysteresis = 0.5f;
    /// <summary>Подходит на эту долю дальности выстрела, чтобы игрок не выходил из неё от дрожания.</summary>
    public const float ApproachFactor = 0.85f;
    /// <summary>Отход, который отдаляет от игрока меньше этого (в тайлах), не имеет смысла — стрелок загнан.</summary>
    public const float MinRetreatGain = 0.05f;
    public const float ArriveDistance = 0.05f;

    // Повороты направления отхода (градусы), если прямо назад не пускает поводок
    private static readonly float[] SideAngles = { 45f, -45f, 90f, -90f };

    /// <summary>
    /// Дистанция, на которую стрелок подходит: ApproachFactor × range, но не ближе keepDistance и не дальше range.
    /// </summary>
    public static float PreferredDistance(float keepDistance, float range)
    {
        range = Mathf.Max(0f, range);
        return Mathf.Clamp(range * ApproachFactor, Mathf.Min(Mathf.Max(0f, keepDistance), range), range);
    }

    /// <summary>
    /// Докуда отходить: keepDistance, а если уже отходит — ещё RetreatHysteresis дальше (но не дальше range,
    /// иначе стрелок отошёл бы за дальность выстрела).
    /// </summary>
    public static float RetreatDistance(float keepDistance, float range, bool wasRetreating)
    {
        keepDistance = Mathf.Max(0f, keepDistance);
        if (!wasRetreating) return keepDistance;
        return Mathf.Max(keepDistance, Mathf.Min(keepDistance + RetreatHysteresis, range));
    }

    /// <summary>
    /// Решение на тик. self/player/home — позиции на карте (в тайлах), leash — поводок от home.
    /// </summary>
    public static RangedPlan Plan(Vector2 self, Vector2 player, Vector2 home, float keepDistance, float range,
        float leash, bool wasRetreating)
    {
        float distance = Vector2.Distance(self, player);
        bool inRange = distance <= range;
        float retreatUntil = RetreatDistance(keepDistance, range, wasRetreating);

        if (distance < retreatUntil)
        {
            if (TryPickRetreat(self, player, home, retreatUntil, leash, out Vector2 target))
                return new RangedPlan(RangedMoveKind.Retreat, target, ArriveDistance, false);

            return new RangedPlan(RangedMoveKind.Hold, self, 0f, inRange, cornered: true);
        }

        if (inRange)
            return new RangedPlan(RangedMoveKind.Hold, self, 0f, true);

        // Не отходит от дома дальше поводка — ждёт игрока на границе (как ближний враг)
        if (Vector2.Distance(home, player) > leash)
        {
            Vector2 edge = home + SafeDirection(player - home, Vector2.right) * leash;
            return new RangedPlan(RangedMoveKind.Approach, edge, ArriveDistance, false);
        }

        return new RangedPlan(RangedMoveKind.Approach, player, PreferredDistance(keepDistance, range), false);
    }

    /// <summary>
    /// Точка отхода: на расстоянии retreatUntil от игрока, внутри круга поводка. Прямо от игрока, иначе —
    /// с поворотом на ±45°/±90°. Подходит точка, которая отдаляет от игрока хотя бы на MinRetreatGain.
    /// </summary>
    public static bool TryPickRetreat(Vector2 self, Vector2 player, Vector2 home, float retreatUntil, float leash,
        out Vector2 target)
    {
        float distance = Vector2.Distance(self, player);
        // Игрок ровно на месте стрелка — отходим к дому (там свободнее), иначе вправо
        Vector2 away = SafeDirection(self - player, SafeDirection(home - self, Vector2.right));
        float step = Mathf.Max(retreatUntil - distance, ArriveDistance * 2f);

        if (TryRetreatPoint(self, player, home, away * step, distance, leash, out target))
            return true;

        float bestGain = MinRetreatGain;
        bool found = false;
        target = self;
        foreach (float angle in SideAngles)
        {
            Vector2 move = Rotate(away, angle) * step;
            if (!TryRetreatPoint(self, player, home, move, distance, leash, out Vector2 candidate)) continue;

            float gain = Vector2.Distance(candidate, player) - distance;
            if (!found || gain > bestGain)
            {
                bestGain = gain;
                target = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Сдвиг, прижатый к кругу поводка; годится, если отдаляет от игрока хотя бы на MinRetreatGain.</summary>
    private static bool TryRetreatPoint(Vector2 self, Vector2 player, Vector2 home, Vector2 move, float distance,
        float leash, out Vector2 point)
    {
        point = ClampToLeash(self + move, home, leash);
        return Vector2.Distance(point, player) - distance >= MinRetreatGain;
    }

    /// <summary>Точка не дальше leash от home (снаружи — на границе круга).</summary>
    public static Vector2 ClampToLeash(Vector2 point, Vector2 home, float leash)
    {
        Vector2 offset = point - home;
        float length = offset.magnitude;
        if (length <= leash || length < 1e-6f) return point;
        return home + offset / length * Mathf.Max(0f, leash);
    }

    /// <summary>
    /// Стрела попала: игрок в момент прилёта не дальше hitRadius от точки, куда она летела.
    /// </summary>
    public static bool IsProjectileHit(Vector2 impactPoint, Vector2 playerPosition, float hitRadius)
    {
        return Vector2.Distance(impactPoint, playerPosition) <= hitRadius;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static Vector2 SafeDirection(Vector2 vector, Vector2 fallback)
    {
        float length = vector.magnitude;
        return length > 1e-4f ? vector / length : fallback;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
