using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Что сейчас делает враг (для Lua и отладки).
/// </summary>
public enum EnemyState
{
    /// <summary>Стоит (статический враг дома, патруль ждёт в точке маршрута).</summary>
    Idle,
    /// <summary>Патруль идёт к следующей точке маршрута.</summary>
    Patrol,
    /// <summary>Преследует игрока или атакует его.</summary>
    Chase,
    /// <summary>Возвращается на пост (бросил погоню).</summary>
    Return
}

/// <summary>
/// Враг на карте. Создаёт, хранит и возвращает в пул EnemyManager; он же двигает время — Tick
/// вызывается только пока выполняется код игрока (между запусками мир стоит).
/// Статический враг стоит на посту; игрок в радиусе агрессии — погоня (не дальше поводка от поста),
/// в радиусе атаки — замах, удар, перезарядка. Погоня сбрасывается, если игрок дальше loseChaseDistance
/// или враг дальше leashRadius от поста; тогда враг возвращается. Патруль ходит между случайными
/// точками суши не дальше patrolPointRadius от места появления (стоит patrolWait в каждой) и так же преследует. Движение — по прямой без поиска пути: вода не пускает,
/// о коллайдеры враг останавливается и скользит вдоль них (Collider2D.Cast, как у игрока).
/// Под туманом рендереры выключены, но враг действует.
/// </summary>
[DisallowMultipleComponent]
public class Enemy : MonoBehaviour
{
    /// <summary>Здоровье кончилось. Вызывается один раз; дальше враг недоступен (IsAlive = false).</summary>
    public event Action<Enemy> OnKilled;

    // Зазор до коллайдера при остановке (в тайлах), как у игрока
    private const float CollisionSkin = 0.06f;
    private const float ArriveDistance = 0.05f;
    // Столько секунд без продвижения — точка маршрута недостижима / домой не пройти
    private const float StuckTimeout = 2f;
    // Подходим чуть ближе радиуса атаки, чтобы игрок не выходил из него от дрожания
    private const float AttackApproach = 0.85f;
    private const int SlideIterations = 2;

    public int Id { get; private set; }
    public EnemyDefinition Definition { get; private set; }
    public EnemyStats Stats { get; private set; }
    /// <summary>Тип для Lua и достижений ("goblin").</summary>
    public string TypeId => Definition != null ? Definition.TypeId : "enemy";
    /// <summary>Патруль (появился после угрозы) или статический враг лагеря.</summary>
    public bool IsPatrol { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth => Stats.MaxHealth;
    public float Damage => Stats.Damage;
    public float AggroRadius => Stats.AggroRadius;
    public float AttackRadius => Stats.AttackRadius;
    public float AttackCooldown => Stats.AttackCooldown;
    public bool IsAlive { get; private set; }
    public EnemyState State { get; private set; }
    /// <summary>
    /// Состояние для Lua: "idle", "patrol" или "chase". Возвращение на пост — "patrol" (идёт спокойно, не гонится).
    /// </summary>
    public string StateName => State switch
    {
        EnemyState.Chase => "chase",
        EnemyState.Patrol => "patrol",
        EnemyState.Return => "patrol",
        _ => "idle"
    };
    /// <summary>Враг уже был на видимом тайле в этом забеге (EnemyFound опубликован).</summary>
    public bool WasSeen { get; set; }
    /// <summary>Тайл врага сейчас не под туманом (рендереры включены).</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>Позиция на карте в тайлах (не привязана к центру тайла).</summary>
    public Vector2 Position { get; private set; }
    public Vector2Int Tile => MapPosition.ToTile(Position);
    /// <summary>Точка, которую охраняет статический враг (у патруля — точка появления).</summary>
    public Vector2 Home { get; private set; }

    /// <summary>Ключ пула — префаб, из которого создан враг.</summary>
    internal Enemy PrefabKey { get; set; }

    private IEnemyWorld _world;
    private Collider2D _body;
    private SpriteRenderer[] _renderers;
    private DepthSortedSprite _depthSorting;
    private readonly List<RaycastHit2D> _hits = new List<RaycastHit2D>();

    private float _attackTimer;
    private bool _inAttackRange;
    // Получил удар — преследует дольше (до aggroRadius × loseAggroMultiplier), даже если игрок бил издалека
    private bool _provoked;
    private bool _hasPatrolTarget;
    private Vector2 _patrolTarget;
    private float _waitTimer;
    private float _stuckTime;

    // ─── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        if (!TryGetComponent(out _depthSorting))
            _depthSorting = gameObject.AddComponent<DepthSortedSprite>();
        _depthSorting.Dynamic = true;

        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        _body = FindBodyCollider();
        if (_body == null)
            Debug.LogWarning($"Enemy: на {name} нет Collider2D (не триггер) — враг проходит сквозь объекты", this);

        // Физика не должна толкать врага — двигаем сами
        if (TryGetComponent(out Rigidbody2D body))
            body.bodyType = RigidbodyType2D.Kinematic;
    }

    /// <summary>
    /// Выдача из пула: transform уже стоит в точке (EnemyManager), здесь — логическое состояние.
    /// </summary>
    public void Setup(int id, EnemyDefinition definition, EnemyStats stats, Vector2 position, bool patrol,
        IEnemyWorld world)
    {
        Id = id;
        Definition = definition;
        Stats = stats;
        IsPatrol = patrol;
        Health = stats.MaxHealth;
        IsAlive = true;
        State = EnemyState.Idle;
        Home = position;
        _world = world;

        _attackTimer = 0f;
        _inAttackRange = false;
        _provoked = false;
        _hasPatrolTarget = false;
        _waitTimer = 0f;
        _stuckTime = 0f;
        WasSeen = false;

        SetPosition(position);
        SetVisible(true);
        _depthSorting.Refresh();
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Нанести урон. Возвращает true, если враг от этого погиб (OnKilled уже вызван).
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0) return false;

        Health = Mathf.Max(0, Health - amount);
        _provoked = true;

        if (Health > 0) return false;

        IsAlive = false;
        OnKilled?.Invoke(this);
        return true;
    }

    public void SetVisible(bool visible)
    {
        if (IsVisible == visible) return;
        IsVisible = visible;

        foreach (SpriteRenderer sr in _renderers)
            if (sr != null)
                sr.enabled = visible;
    }

    /// <summary>
    /// Шаг игрового времени. Вызывает EnemyManager только пока выполняется код игрока.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsAlive || _world == null || deltaTime <= 0f) return;

        _attackTimer = Mathf.Max(0f, _attackTimer - deltaTime);

        Vector2 player = _world.PlayerPosition;
        float toPlayer = Vector2.Distance(Position, player);

        if (ShouldChase(player, toPlayer))
        {
            State = EnemyState.Chase;
            ChaseAndAttack(player, toPlayer, deltaTime);
            return;
        }

        _provoked = false;
        _inAttackRange = false;

        if (IsPatrol) Patrol(deltaTime);
        else ReturnHome(deltaTime);
    }

    // ─── Behaviour ────────────────────────────────────────────

    private bool ShouldChase(Vector2 player, float toPlayer)
    {
        if (!_world.IsPlayerAlive) return false;

        // Погоня начинается в радиусе агрессии (или от удара игрока)
        bool engaged = State == EnemyState.Chase || _provoked;
        if (!engaged) return toPlayer <= Stats.AggroRadius;

        // Сброс погони: игрок убежал или враг слишком далеко от поста
        if (toPlayer > Stats.LoseChaseDistance) return false;
        if (Vector2.Distance(Position, Home) > Stats.LeashRadius + ArriveDistance) return false;
        return true;
    }

    private void ChaseAndAttack(Vector2 player, float toPlayer, float deltaTime)
    {
        if (toPlayer <= Stats.AttackRadius)
        {
            // Только вошёл в радиус — короткий замах, игрок успевает среагировать кодом
            if (!_inAttackRange)
            {
                _inAttackRange = true;
                _attackTimer = Mathf.Max(_attackTimer, Stats.AttackWindup);
            }

            Face(player - Position);
            // В начале запуска — «фора»: враг не бьёт, но замах уже идёт
            if (_attackTimer <= 0f && _world.CanAttack)
            {
                _world.DamagePlayer(this, Stats.Damage);
                _attackTimer = Stats.AttackCooldown;
            }
            return;
        }

        _inAttackRange = false;

        Vector2 target = player;
        float stopDistance = Stats.AttackRadius * AttackApproach;

        // Враг не отходит от поста дальше поводка — ждёт игрока на границе
        if (Vector2.Distance(Home, target) > Stats.LeashRadius)
        {
            target = Home + (target - Home).normalized * Stats.LeashRadius;
            stopDistance = ArriveDistance;
        }

        MoveTowards(target, stopDistance, Stats.ChaseSpeed, deltaTime);
    }

    private void ReturnHome(float deltaTime)
    {
        if (Vector2.Distance(Position, Home) <= ArriveDistance)
        {
            State = EnemyState.Idle;
            return;
        }

        State = EnemyState.Return;
        MoveTowards(Home, ArriveDistance, Stats.PatrolSpeed, deltaTime);

        // Домой не пройти (загородили постройкой) — остаётся охранять там, где стоит
        if (_stuckTime >= StuckTimeout)
        {
            Home = Position;
            _stuckTime = 0f;
            State = EnemyState.Idle;
        }
    }

    private void Patrol(float deltaTime)
    {
        if (!_hasPatrolTarget)
        {
            _waitTimer -= deltaTime;
            if (_waitTimer > 0f)
            {
                State = EnemyState.Idle;
                return;
            }

            // Маршрут — вокруг места появления, патруль не уходит бродить по всей карте
            _patrolTarget = _world.PickPatrolPoint(Home, Stats.PatrolPointRadius);
            _hasPatrolTarget = true;
            _stuckTime = 0f;
        }

        State = EnemyState.Patrol;
        bool arrived = MoveTowards(_patrolTarget, ArriveDistance, Stats.PatrolSpeed, deltaTime);

        if (arrived || _stuckTime >= StuckTimeout)
        {
            _hasPatrolTarget = false;
            _waitTimer = Stats.PatrolWait;
            _stuckTime = 0f;
        }
    }

    // ─── Movement ─────────────────────────────────────────────

    /// <summary>
    /// Шаг к цели с постоянной скоростью; возвращает true, если цель достигнута (ближе stopDistance).
    /// Без продвижения копится _stuckTime.
    /// </summary>
    private bool MoveTowards(Vector2 target, float stopDistance, float speed, float deltaTime)
    {
        Vector2 toTarget = target - Position;
        float distance = toTarget.magnitude;
        if (distance <= stopDistance)
        {
            _stuckTime = 0f;
            return true;
        }

        float step = Mathf.Min(speed * deltaTime, distance - stopDistance);
        Vector2 move = toTarget / distance * step;
        Face(move);

        Vector2 before = Position;
        SetPosition(MoveWithSlide(Position, move, toTarget));

        float moved = Vector2.Distance(before, Position);
        _stuckTime = moved < step * 0.25f ? _stuckTime + deltaTime : 0f;

        return Vector2.Distance(Position, target) <= stopDistance + 0.001f;
    }

    /// <summary>
    /// Сдвиг с учётом коллайдеров и воды: упёршись, остаток идёт вдоль поверхности.
    /// </summary>
    private Vector2 MoveWithSlide(Vector2 position, Vector2 move, Vector2 toTarget)
    {
        for (int i = 0; i < SlideIterations && move.sqrMagnitude > 1e-8f; i++)
        {
            float length = move.magnitude;
            Vector2 direction = move / length;

            if (!TryCast(position, direction, length, out float hitDistance, out Vector2 normal))
            {
                position = ClampToLand(position, position + move);
                break;
            }

            float travel = Mathf.Max(0f, hitDistance - CollisionSkin);
            position = ClampToLand(position, position + direction * travel);

            float remaining = length - travel;
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            float along = Vector2.Dot(direction * remaining, tangent);

            // Упёрлись в лоб — обходим с той стороны, куда смещена цель
            if (Mathf.Abs(along) < remaining * 0.05f)
                along = remaining * (Vector2.Dot(toTarget, tangent) >= 0f ? 1f : -1f);

            move = tangent * along;
        }

        return position;
    }

    private Vector2 ClampToLand(Vector2 from, Vector2 to)
    {
        if (_world.IsWalkable(to)) return to;

        Vector2 alongX = new Vector2(to.x, from.y);
        if (_world.IsWalkable(alongX)) return alongX;

        Vector2 alongY = new Vector2(from.x, to.y);
        if (_world.IsWalkable(alongY)) return alongY;

        return from;
    }

    /// <summary>
    /// «Протаскивает» коллайдер врага вдоль direction (в тайлах). Препятствия сбоку и позади не мешают.
    /// </summary>
    private bool TryCast(Vector2 position, Vector2 direction, float distance, out float hitDistance, out Vector2 normal)
    {
        hitDistance = 0f;
        normal = Vector2.zero;
        if (_body == null) return false;

        // Cast считает от текущей позиции коллайдера — синхронизируем её с transform
        SetPosition(position);
        Physics2D.SyncTransforms();

        float scale = Mathf.Max(_world.WorldScale, 0.0001f);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        int count = _body.Cast(direction, filter, _hits, (distance + CollisionSkin) * scale, true);

        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _hits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Vector2.Dot(direction, hit.normal) >= 0f) continue;

            float d = hit.distance / scale;
            if (!found || d < hitDistance)
            {
                hitDistance = d;
                normal = hit.normal;
                found = true;
            }
        }

        return found;
    }

    private void SetPosition(Vector2 position)
    {
        Position = position;
        Vector3 world = _world.MapToWorld(position);
        transform.position = new Vector3(world.x, world.y, transform.position.z);
    }

    /// <summary>Спрайты Tiny Swords смотрят вправо — поворачиваем по направлению движения.</summary>
    private void Face(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) < 0.01f) return;
        bool flip = direction.x < 0f;

        foreach (SpriteRenderer sr in _renderers)
            if (sr != null)
                sr.flipX = flip;
    }

    private Collider2D FindBodyCollider()
    {
        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
            if (!collider.isTrigger)
                return collider;
        return null;
    }
}
