using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вид врага: префаб, характеристики и добыча (GameDesign §10.1, значения по умолчанию — гоблин).
/// Нулевые радиусы/скорости/тайминги берутся из EnemyConfig, см. EnemyStats.Resolve.
/// </summary>
[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "CodeRogue/Enemies/Enemy")]
public class EnemyDefinition : ScriptableObject
{
    [Header("Base")]
    [Tooltip("Тип врага для Lua и достижений (EnemyFound/EnemyKilled), например \"goblin\". Пусто — имя ассета")]
    public string enemyId = "goblin";
    public string displayName;
    [Tooltip("Префаб с компонентом Enemy, Collider2D (не триггер) у ног и спрайтом с опорной точкой у основания")]
    public Enemy prefab;

    [Header("Stats")]
    [Min(1)] public int maxHealth = 40;
    [Tooltip("Урон по здоровью игрока за один удар")]
    [Min(0f)] public float damage = 10f;

    [Header("Behaviour (0 — значение из EnemyConfig)")]
    [Tooltip("Игрок ближе этого (в тайлах) — враг начинает погоню")]
    [Min(0f)] public float aggroRadius;
    [Tooltip("Игрок ближе этого (центр–центр) — враг бьёт (у стрелка — дальность выстрела)")]
    [Min(0f)] public float attackRadius;
    [Tooltip("Замах перед первым ударом, когда игрок вошёл в радиус атаки (секунды)")]
    [Min(0f)] public float attackWindup;
    [Tooltip("Секунды между ударами")]
    [Min(0f)] public float attackCooldown;
    [Tooltip("Скорость погони, тайлов в секунду")]
    [Min(0f)] public float chaseSpeed;
    [Tooltip("Скорость патруля и возвращения на пост, тайлов в секунду")]
    [Min(0f)] public float patrolSpeed;
    [Tooltip("Погоня сбрасывается, если игрок дальше этого от врага (в тайлах)")]
    [Min(0f)] public float loseChaseDistance;
    [Tooltip("Враг не отходит от поста (дом лагеря / точка появления патруля) дальше этого (в тайлах)")]
    [Min(0f)] public float leashRadius;

    [Header("Ranged (лучник; выключено — ближний бой как раньше)")]
    [Tooltip("Дальний бой: стреляет снарядом с дистанции attackRadius, отходит от игрока ближе keepDistance " +
             "(RangedEnemyBehaviour)")]
    public bool ranged;
    [Tooltip("Игрок ближе этого (в тайлах) — стрелок отходит (не дальше leashRadius от дома). 0 — 2.5")]
    [Min(0f)] public float keepDistance;
    [Tooltip("Видимый снаряд (префаб с компонентом Arrow). Пусто — снаряд без картинки, урон так же при прилёте")]
    public Arrow projectilePrefab;
    [Tooltip("Время полёта снаряда (секунды игрового времени). 0 — 0.3")]
    [Min(0f)] public float projectileFlightTime;

    [Header("Loot")]
    [Tooltip("Что выпадает на землю при смерти (через World Prefab предмета; без него — сразу в инвентарь)")]
    public List<LootEntry> loot = new List<LootEntry>();

    /// <summary>Тип для Lua и событий: enemyId в нижнем регистре, иначе имя ассета.</summary>
    public string TypeId => string.IsNullOrWhiteSpace(enemyId) ? name.ToLowerInvariant() : enemyId.Trim().ToLowerInvariant();

    public string DisplayName => string.IsNullOrEmpty(displayName) ? TypeId : displayName;

    /// <summary>Непустые строки добычи.</summary>
    public IEnumerable<LootEntry> Loot
    {
        get
        {
            foreach (LootEntry entry in loot)
                if (entry.item != null && entry.amount > 0)
                    yield return entry;
        }
    }
}

/// <summary>
/// Итоговые характеристики врага: значения EnemyDefinition, нулевые — из EnemyConfig
/// (а без конфига — значения гоблина из GameDesign §10.1).
/// </summary>
public readonly struct EnemyStats
{
    public readonly int MaxHealth;
    public readonly float Damage;
    public readonly float AggroRadius;
    public readonly float AttackRadius;
    public readonly float AttackWindup;
    public readonly float AttackCooldown;
    public readonly float ChaseSpeed;
    public readonly float PatrolSpeed;
    public readonly float LoseChaseDistance;
    public readonly float LeashRadius;
    public readonly float PatrolPointRadius;
    public readonly float PatrolWait;
    /// <summary>Дальний бой (EnemyDefinition.ranged): AttackRadius — дальность выстрела.</summary>
    public readonly bool IsRanged;
    /// <summary>Стрелок отходит, если игрок ближе этого (в тайлах).</summary>
    public readonly float KeepDistance;
    /// <summary>Время полёта снаряда (секунды игрового времени).</summary>
    public readonly float ProjectileFlightTime;

    public const float DefaultKeepDistance = 2.5f;
    public const float DefaultProjectileFlightTime = 0.3f;

    private EnemyStats(EnemyDefinition definition, EnemyConfig config)
    {
        bool c = config != null;
        MaxHealth = Mathf.Max(1, definition.maxHealth);
        Damage = Mathf.Max(0f, definition.damage);
        AggroRadius = Pick(definition.aggroRadius, c ? config.defaultAggroRadius : 3f);
        AttackRadius = Pick(definition.attackRadius, c ? config.defaultAttackRadius : 1f);
        AttackWindup = definition.attackWindup > 0f ? definition.attackWindup : c ? Mathf.Max(0f, config.defaultAttackWindup) : 0.4f;
        AttackCooldown = Pick(definition.attackCooldown, c ? config.defaultAttackCooldown : 1.5f);
        ChaseSpeed = Pick(definition.chaseSpeed, c ? config.defaultChaseSpeed : 3f);
        PatrolSpeed = Pick(definition.patrolSpeed, c ? config.defaultPatrolSpeed : 2f);
        LoseChaseDistance = Mathf.Max(AggroRadius, Pick(definition.loseChaseDistance, c ? config.defaultLoseChaseDistance : 6f));
        LeashRadius = Pick(definition.leashRadius, c ? config.defaultLeashRadius : 8f);
        PatrolPointRadius = c ? Mathf.Max(1f, config.patrolPointRadius) : 8f;
        PatrolWait = c ? Mathf.Max(0f, config.patrolWaitSeconds) : 1f;
        IsRanged = definition.ranged;
        // Дистанция отхода меньше дальности выстрела — иначе стрелок отходит за дальность, подходит и снова отходит
        KeepDistance = Pick(definition.keepDistance, DefaultKeepDistance);
        if (IsRanged) KeepDistance = Mathf.Min(KeepDistance, AttackRadius * 0.8f);
        ProjectileFlightTime = definition.projectileFlightTime > 0f
            ? definition.projectileFlightTime
            : DefaultProjectileFlightTime;
    }

    public static EnemyStats Resolve(EnemyDefinition definition, EnemyConfig config) => new EnemyStats(definition, config);

    private static float Pick(float own, float fallback) => own > 0f ? own : Mathf.Max(0.01f, fallback);
}
