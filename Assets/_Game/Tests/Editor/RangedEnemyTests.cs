using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Враг-лучник (T13): решения RangedEnemyPlanner (подход / стоять и стрелять / отход с гистерезисом, поводок,
/// «загнан»), характеристики по умолчанию (старые враги — ближний бой), стратегия RangedEnemyBehaviour на
/// настоящем Enemy (замах, перезарядка, фора, "retreat", поводок) и полёт стрел EnemyProjectiles (урон при прилёте,
/// промах, если игрок ушёл с точки). Ближний враг с тем же миром по-прежнему бьёт сам и не стреляет.
/// Карта-заглушка: точка карты = мировая точка, вся суша.
/// </summary>
public class RangedEnemyTests
{
    private const float Keep = 2.5f;
    private const float Range = 4f;
    private const float Leash = 8f;
    private const float Eps = 1e-3f;

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    // ─── RangedEnemyPlanner ───────────────────────────────────

    [Test]
    public void Plan_PlayerBeyondRange_ApproachesToPreferredDistance()
    {
        Vector2 self = new Vector2(10f, 10f);
        Vector2 player = new Vector2(16f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, player, self, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Approach, plan.Kind);
        Assert.AreEqual(player, plan.Target);
        Assert.AreEqual(Range * RangedEnemyPlanner.ApproachFactor, plan.StopDistance, Eps);
        Assert.IsFalse(plan.CanShoot);
    }

    [Test]
    public void Plan_PlayerBetweenKeepAndRange_HoldsAndShoots()
    {
        Vector2 self = new Vector2(10f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, new Vector2(13f, 10f), self, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Hold, plan.Kind);
        Assert.IsTrue(plan.CanShoot);
        Assert.IsFalse(plan.Cornered);
    }

    [Test]
    public void Plan_PlayerTooClose_RetreatsAwayToKeepDistance()
    {
        Vector2 self = new Vector2(10f, 10f);
        Vector2 player = new Vector2(11f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, player, self, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Retreat, plan.Kind);
        Assert.IsFalse(plan.CanShoot, "no shots while retreating");
        Assert.Less(plan.Target.x, self.x, "moves away from the player");
        Assert.AreEqual(Keep, Vector2.Distance(plan.Target, player), Eps);
    }

    [Test]
    public void Plan_Hysteresis_KeepsRetreatingSlightlyBeyondKeepDistance()
    {
        Vector2 self = new Vector2(10f, 10f);
        Vector2 player = new Vector2(10f + Keep + 0.2f, 10f);

        Assert.AreEqual(RangedMoveKind.Hold,
            RangedEnemyPlanner.Plan(self, player, self, Keep, Range, Leash, wasRetreating: false).Kind);
        Assert.AreEqual(RangedMoveKind.Retreat,
            RangedEnemyPlanner.Plan(self, player, self, Keep, Range, Leash, wasRetreating: true).Kind);
    }

    [Test]
    public void RetreatDistance_NeverBeyondRange()
    {
        Assert.AreEqual(Keep, RangedEnemyPlanner.RetreatDistance(Keep, Range, false), Eps);
        Assert.AreEqual(Keep + RangedEnemyPlanner.RetreatHysteresis,
            RangedEnemyPlanner.RetreatDistance(Keep, Range, true), Eps);
        Assert.AreEqual(2.6f, RangedEnemyPlanner.RetreatDistance(Keep, 2.6f, true), Eps);
    }

    [Test]
    public void PreferredDistance_ClampedBetweenKeepAndRange()
    {
        Assert.AreEqual(3.4f, RangedEnemyPlanner.PreferredDistance(2.5f, 4f), Eps);
        Assert.AreEqual(3.8f, RangedEnemyPlanner.PreferredDistance(3.8f, 4f), Eps, "not closer than keep");
        Assert.AreEqual(2f, RangedEnemyPlanner.PreferredDistance(2.5f, 2f), Eps, "not farther than range");
    }

    [Test]
    public void Plan_RetreatAtLeashEdge_SlidesAlongLeashInsteadOfLeaving()
    {
        Vector2 home = new Vector2(10f, 10f);
        Vector2 self = new Vector2(10f + Leash - 0.01f, 10f);
        Vector2 player = new Vector2(self.x - 1f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, player, home, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Retreat, plan.Kind);
        Assert.LessOrEqual(Vector2.Distance(plan.Target, home), Leash + Eps, "stays on the leash");
        Assert.Greater(Vector2.Distance(plan.Target, player), Vector2.Distance(self, player) + Eps,
            "still gets farther from the player");
    }

    [Test]
    public void Plan_NoRoomToRetreat_HoldsCorneredAndShoots()
    {
        Vector2 self = new Vector2(10f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, new Vector2(11f, 10f), self, Keep, Range, leash: 0f, false);

        Assert.AreEqual(RangedMoveKind.Hold, plan.Kind);
        Assert.IsTrue(plan.Cornered);
        Assert.IsTrue(plan.CanShoot, "shoots point-blank");
    }

    [Test]
    public void Plan_PlayerBeyondLeash_WaitsOnLeashEdge()
    {
        Vector2 home = new Vector2(10f, 10f);
        Vector2 player = new Vector2(30f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(home, player, home, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Approach, plan.Kind);
        Assert.AreEqual(new Vector2(10f + Leash, 10f).x, plan.Target.x, Eps);
        Assert.AreEqual(10f, plan.Target.y, Eps);
    }

    [Test]
    public void Plan_PlayerOnTopOfArcher_RetreatsTowardHome()
    {
        Vector2 home = new Vector2(10f, 10f);
        Vector2 self = new Vector2(12f, 10f);

        RangedPlan plan = RangedEnemyPlanner.Plan(self, self, home, Keep, Range, Leash, false);

        Assert.AreEqual(RangedMoveKind.Retreat, plan.Kind);
        Assert.Less(plan.Target.x, self.x);
    }

    [Test]
    public void IsProjectileHit_ByDistanceToImpactPoint()
    {
        Assert.IsTrue(RangedEnemyPlanner.IsProjectileHit(Vector2.zero, new Vector2(0.5f, 0f), 0.6f));
        Assert.IsFalse(RangedEnemyPlanner.IsProjectileHit(Vector2.zero, new Vector2(1.5f, 0f), 0.6f));
    }

    // ─── EnemyStats ───────────────────────────────────────────

    [Test]
    public void Stats_DefaultDefinition_IsMeleeWithRangedDefaults()
    {
        EnemyDefinition melee = Track(ScriptableObject.CreateInstance<EnemyDefinition>());

        EnemyStats stats = EnemyStats.Resolve(melee, null);

        Assert.IsFalse(stats.IsRanged, "old enemies stay melee");
        Assert.AreEqual(1f, stats.AttackRadius, Eps);
        Assert.AreEqual(EnemyStats.DefaultKeepDistance, stats.KeepDistance, Eps);
        Assert.AreEqual(EnemyStats.DefaultProjectileFlightTime, stats.ProjectileFlightTime, Eps);
    }

    [Test]
    public void Stats_Archer_UsesAttackRadiusAsRange()
    {
        EnemyStats stats = EnemyStats.Resolve(CreateArcherDefinition(), null);

        Assert.IsTrue(stats.IsRanged);
        Assert.AreEqual(Range, stats.AttackRadius, Eps);
        Assert.AreEqual(Keep, stats.KeepDistance, Eps);
        Assert.AreEqual(2f, stats.AttackCooldown, Eps);
        Assert.AreEqual(6f, stats.Damage, Eps);
    }

    // ─── RangedEnemyBehaviour на Enemy ────────────────────────

    [Test]
    public void Archer_InRange_WindupThenShootsThenCooldown()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(3f, 0f) };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);

        archer.Tick(0.1f); // вошёл в бой: замах 0.4
        Assert.AreEqual(0, world.Shots.Count, "windup first");
        Assert.AreEqual("chase", archer.StateName);

        archer.Tick(0.5f);
        Assert.AreEqual(1, world.Shots.Count, "shot after windup");
        Assert.AreEqual(world.PlayerPosition, world.Shots[0]);
        Assert.AreEqual(0, world.Damage.Count, "no melee hit, damage comes with the arrow");

        archer.Tick(1f);
        Assert.AreEqual(1, world.Shots.Count, "cooldown 2 s");
        archer.Tick(1.1f);
        Assert.AreEqual(2, world.Shots.Count);
    }

    [Test]
    public void Archer_RunStartGrace_DoesNotShoot()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(3f, 0f), CanAttack = false };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);

        for (int i = 0; i < 10; i++) archer.Tick(0.5f);

        Assert.AreEqual(0, world.Shots.Count);
    }

    [Test]
    public void Archer_PlayerTooClose_RetreatsWithRetreatState()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(1f, 0f) };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);

        archer.Tick(0.1f);

        Assert.AreEqual(EnemyState.Retreat, archer.State);
        Assert.AreEqual("retreat", archer.StateName);
        Assert.Less(archer.Position.x, 0f, "steps away from the player");
        Assert.AreEqual(0, world.Shots.Count);
    }

    [Test]
    public void Archer_PressedByPlayer_NeverLeavesLeash()
    {
        EnemyDefinition definition = CreateArcherDefinition();
        definition.leashRadius = 2f;
        var world = new RecordingWorld { PlayerPosition = new Vector2(2f, 0f) };
        Enemy archer = CreateEnemy(definition, Vector2.zero, world);

        // Игрок медленно наседает — лучник отходит, но не дальше поводка, и в итоге стреляет в упор
        for (int i = 0; i < 200; i++)
        {
            Vector2 toArcher = archer.Position - world.PlayerPosition;
            if (toArcher.magnitude > 0.8f)
                world.PlayerPosition += toArcher.normalized * 0.02f;
            archer.Tick(0.05f);
            Assert.LessOrEqual(Vector2.Distance(archer.Position, archer.Home), 2f + 0.06f, $"tick {i}");
        }

        Assert.Greater(world.Shots.Count, 0, "cornered archer shoots");
    }

    [Test]
    public void Goblin_SameWorld_StillMeleeOnly()
    {
        EnemyDefinition goblin = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
        goblin.enemyId = "goblin";
        var world = new RecordingWorld { PlayerPosition = new Vector2(0.5f, 0f) };
        Enemy enemy = CreateEnemy(goblin, Vector2.zero, world);

        enemy.Tick(0.1f);
        enemy.Tick(0.5f);

        Assert.IsFalse(enemy.IsRanged);
        Assert.AreEqual(0, world.Shots.Count);
        Assert.AreEqual(1, world.Damage.Count, "melee hit after windup");
        Assert.AreEqual("chase", enemy.StateName);
    }

    // ─── EnemyProjectiles ─────────────────────────────────────

    [Test]
    public void Projectile_DamagesOnArrival_NotBefore()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(3f, 0f) };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);
        var damage = new List<float>();
        EnemyProjectiles projectiles = CreateProjectiles(world, damage);

        projectiles.Launch(archer, world.PlayerPosition);
        projectiles.Tick(0.2f);
        Assert.AreEqual(0, damage.Count, "still flying");
        Assert.AreEqual(1, projectiles.Count);

        projectiles.Tick(0.15f);
        Assert.AreEqual(1, damage.Count);
        Assert.AreEqual(6f, damage[0], Eps);
        Assert.AreEqual(0, projectiles.Count);
    }

    [Test]
    public void Projectile_PlayerLeftTheSpot_Misses()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(3f, 0f) };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);
        var damage = new List<float>();
        EnemyProjectiles projectiles = CreateProjectiles(world, damage);

        projectiles.Launch(archer, world.PlayerPosition);
        world.PlayerPosition = new Vector2(3f, 1.5f);
        projectiles.Tick(0.5f);

        Assert.AreEqual(0, damage.Count);
        Assert.AreEqual(0, projectiles.Count);
    }

    [Test]
    public void Projectile_NoTime_HangsInTheAir()
    {
        var world = new RecordingWorld { PlayerPosition = new Vector2(3f, 0f) };
        Enemy archer = CreateEnemy(CreateArcherDefinition(), Vector2.zero, world);
        var damage = new List<float>();
        EnemyProjectiles projectiles = CreateProjectiles(world, damage);

        projectiles.Launch(archer, world.PlayerPosition);
        projectiles.Tick(0f);

        Assert.AreEqual(1, projectiles.Count, "world time stopped — arrow waits for the next run");
        Assert.AreEqual(0, damage.Count);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private EnemyDefinition CreateArcherDefinition()
    {
        EnemyDefinition archer = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
        archer.name = "archer";
        archer.enemyId = "archer";
        archer.maxHealth = 25;
        archer.damage = 6f;
        archer.ranged = true;
        archer.attackRadius = Range;
        archer.aggroRadius = 5f;
        archer.loseChaseDistance = 7f;
        archer.attackCooldown = 2f;
        archer.keepDistance = Keep;
        return archer;
    }

    /// <summary>
    /// Враг без коллайдера (движение не делает Cast). В EditMode Awake не вызывается — зовём отражением.
    /// </summary>
    private Enemy CreateEnemy(EnemyDefinition definition, Vector2 position, IEnemyWorld world)
    {
        GameObject go = Track(new GameObject(definition.TypeId));
        Enemy enemy = go.AddComponent<Enemy>();
        MethodInfo awake = typeof(Enemy).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(awake, "Enemy.Awake not found");
        awake.Invoke(enemy, null);

        enemy.Setup(1, definition, EnemyStats.Resolve(definition, null), position, patrol: false, world);
        return enemy;
    }

    private static EnemyProjectiles CreateProjectiles(RecordingWorld world, List<float> damage)
    {
        return new EnemyProjectiles(new StubMap(), null, _ => true, () => world.PlayerPosition, () => true,
            (attacker, amount) => damage.Add(amount), () => 1f);
    }

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
    }

    /// <summary>Мир врага: игрок в PlayerPosition, вся суша; выстрелы и удары записываются.</summary>
    private sealed class RecordingWorld : IEnemyWorld
    {
        public readonly List<Vector2> Shots = new List<Vector2>();
        public readonly List<float> Damage = new List<float>();

        public Vector2 PlayerPosition { get; set; }
        public bool IsPlayerAlive => true;
        public bool CanAttack { get; set; } = true;
        public void DamagePlayer(Enemy attacker, float damage) => Damage.Add(damage);
        public void ShootProjectile(Enemy attacker, Vector2 target) => Shots.Add(target);
        public bool IsWalkable(Vector2 mapPosition) => true;
        public Vector3 MapToWorld(Vector2 mapPosition) => new Vector3(mapPosition.x, mapPosition.y, 0f);
        public float WorldScale => 1f;
        public Vector2 PickPatrolPoint(Vector2 from, float radius) => from;
    }

    private sealed class StubMap : IMapDataProvider
    {
        public int MapWidth => 64;
        public int MapHeight => 64;
        public int Seed => 1;
        public bool IsWalkable(int x, int y) => true;
        public bool IsInBounds(int x, int y) => true;
        public TileType GetTileType(int x, int y) => TileType.Grass;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }
}
