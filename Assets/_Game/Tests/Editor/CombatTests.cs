using System.Collections.Generic;
using System.Reflection;
using Lua;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Бой (этап 3, GameDesign §10.1): урон кулаком 5 / мечом 15, радиус удара 1,5, «enemy is too far»;
/// выбор оружия (сильнее, при равенстве — изношеннее), износ за удар и поломка; Enemy.TakeDamage →
/// OnKilled один раз; удар по мёртвому — ошибка без износа; id врагов в Lua не пересекаются с объектами.
/// Персонаж стоит в (0, 0) на карте-заглушке, где тайл (x, y) — мировая точка (x, y).
/// </summary>
public class CombatTests
{
    private readonly List<Object> _created = new List<Object>();
    private PlayerCommandExecutor _executor;
    private PlayerInventory _inventory;
    private CombatService _combat;
    private EnemyDefinition _goblin;
    private EnemyManager _manager;

    [SetUp]
    public void SetUp()
    {
        _executor = CreateExecutor(Vector2Int.zero);
        _inventory = new PlayerInventory(5);
        // Удар по конкретному врагу не обращается к EnemyManager — для TryAttack хватает null
        _combat = new CombatService(_executor, _inventory, null);

        _goblin = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
        _goblin.name = "goblin";
        _goblin.enemyId = "goblin";
        _goblin.maxHealth = 40;
    }

    [TearDown]
    public void TearDown()
    {
        if (_manager != null)
        {
            // Контейнер "Enemies" создаётся в конструкторе EnemyManager — убираем его из сцены
            var container = GetPrivate<Transform>(_manager, "_container");
            _manager.Dispose();
            if (container != null) Object.DestroyImmediate(container.gameObject);
            _manager = null;
        }

        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    // ─── Урон и радиус ────────────────────────────────────────

    [Test]
    public void TryAttack_NoWeapon_FistDealsFiveDamage()
    {
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult result, out string error), error);

        Assert.IsNull(error);
        Assert.AreEqual(CombatService.FistDamage, result.Damage);
        Assert.AreEqual(5, result.Damage);
        Assert.IsNull(result.Weapon, "fist");
        Assert.IsFalse(result.Killed);
        Assert.AreEqual(35, enemy.Health);
    }

    [Test]
    public void TryAttack_WithSword_DealsFifteenDamage()
    {
        WeaponItem sword = Track(TestHelpers.Weapon("sword"));
        _inventory.Add(sword, 1);
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult result, out string error), error);

        Assert.AreEqual(15, result.Damage);
        Assert.AreSame(sword, result.Weapon);
        Assert.AreEqual(25, enemy.Health);
        Assert.AreEqual(15, _combat.AttackDamage);
    }

    [Test]
    public void AttackRadius_FistAndDefaultSword_AreOneAndAHalf()
    {
        Assert.AreEqual(1.5f, CombatService.FistRadius, 0.0001f);
        Assert.AreEqual(1.5f, _combat.AttackRadius, 0.0001f, "fist");

        WeaponItem sword = Track(ScriptableObject.CreateInstance<WeaponItem>());
        Assert.AreEqual(15, sword.Damage, "default sword damage");
        Assert.AreEqual(1.5f, sword.AttackRadius, 0.0001f, "default sword radius");
    }

    [Test]
    public void TryAttack_EnemyExactlyAtRadius_Hits()
    {
        Enemy enemy = CreateEnemy(1, new Vector2(1.5f, 0f));

        Assert.IsTrue(_combat.TryAttack(enemy, out _, out string error), error);
        Assert.AreEqual(35, enemy.Health);
    }

    [Test]
    public void TryAttack_EnemyBeyondRadius_TooFar_NothingChanges()
    {
        WeaponItem sword = Track(TestHelpers.Weapon("sword", maxDurability: 3));
        _inventory.Add(sword, 1);
        Enemy enemy = CreateEnemy(1, new Vector2(2f, 0f));

        Assert.IsFalse(_combat.TryAttack(enemy, out AttackResult result, out string error));

        // Числа в тексте форматируются текущей культурой (1.5 / 1,5) — проверяем только начало
        StringAssert.StartsWith("enemy is too far: 2 > 1", error);
        Assert.AreEqual(40, enemy.Health, "no damage");
        Assert.AreEqual(3, _inventory.GetSlot(0).Durability, "no wear on a miss");
        Assert.IsNull(result.Target);
    }

    [Test]
    public void TryAttack_LongWeapon_UsesWeaponRadius()
    {
        _inventory.Add(Track(TestHelpers.Weapon("spear", damage: 10, radius: 3f)), 1);
        Enemy enemy = CreateEnemy(1, new Vector2(2.5f, 0f));

        Assert.AreEqual(3f, _combat.AttackRadius, 0.0001f);
        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult result, out string error), error);
        Assert.AreEqual(10, result.Damage);
    }

    // ─── Выбор оружия и износ ─────────────────────────────────

    [Test]
    public void FindWeaponSlot_PicksHighestDamage()
    {
        WeaponItem weak = Track(TestHelpers.Weapon("club", damage: 8));
        WeaponItem strong = Track(TestHelpers.Weapon("sword", damage: 15));
        _inventory.Add(weak, 1);
        _inventory.Add(strong, 1);
        _inventory.Add(Track(TestHelpers.Tool("axe", ToolType.Axe)), 1);

        Assert.AreEqual(1, _inventory.FindWeaponSlot());
        Assert.AreSame(strong, _combat.CurrentWeapon);
    }

    [Test]
    public void FindWeaponSlot_EqualDamage_PicksMostWorn()
    {
        WeaponItem sword = Track(TestHelpers.Weapon("sword", maxDurability: 5));
        _inventory.Add(sword, 2);
        Assert.AreSame(sword, _inventory.GetSlot(0).Item, "weapons never stack");
        Assert.AreSame(sword, _inventory.GetSlot(1).Item);

        _inventory.WearTool(1, 2);

        Assert.AreEqual(1, _inventory.FindWeaponSlot(), "the more worn sword is used first");
    }

    [Test]
    public void FindWeaponSlot_NoWeapon_MinusOne_ToolsAreNotWeapons()
    {
        _inventory.Add(Track(TestHelpers.Tool("axe", ToolType.Axe)), 1);
        _inventory.Add(Track(TestHelpers.Material("stone")), 3);

        Assert.AreEqual(-1, _inventory.FindWeaponSlot());
        Assert.IsNull(_combat.CurrentWeapon);
        Assert.AreEqual(CombatService.FistDamage, _combat.AttackDamage);
    }

    [Test]
    public void TryAttack_WearsWeaponByWearPerUse()
    {
        _inventory.Add(Track(TestHelpers.Weapon("sword", maxDurability: 10, wear: 3)), 1);
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult result, out string error), error);

        Assert.IsFalse(result.WeaponBroken);
        Assert.AreEqual(7, _inventory.GetSlot(0).Durability);
    }

    [Test]
    public void TryAttack_SwordAtLastDurability_BreaksAndDisappears_NextHitIsFist()
    {
        WeaponItem sword = Track(TestHelpers.Weapon("sword", maxDurability: 1));
        _inventory.Add(sword, 1);
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult first, out string error), error);
        Assert.AreEqual(15, first.Damage, "the breaking hit still deals sword damage");
        Assert.IsTrue(first.WeaponBroken);
        Assert.AreEqual(0, _inventory.GetCount(sword), "broken sword is removed");
        Assert.IsTrue(_inventory.GetSlot(0).IsEmpty);

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult second, out error), error);
        Assert.AreEqual(CombatService.FistDamage, second.Damage);
        Assert.IsNull(second.Weapon);
        Assert.AreEqual(40 - 15 - 5, enemy.Health);
    }

    // ─── Смерть врага ─────────────────────────────────────────

    [Test]
    public void TakeDamage_ToZero_RaisesOnKilledOnce()
    {
        Enemy enemy = CreateEnemy(1, Vector2.zero);
        int killed = 0;
        enemy.OnKilled += _ => killed++;

        Assert.IsFalse(enemy.TakeDamage(39));
        Assert.AreEqual(0, killed);
        Assert.IsTrue(enemy.IsAlive);

        Assert.IsTrue(enemy.TakeDamage(5), "the killing hit returns true");
        Assert.AreEqual(1, killed);
        Assert.AreEqual(0, enemy.Health, "health does not go below zero");
        Assert.IsFalse(enemy.IsAlive);

        Assert.IsFalse(enemy.TakeDamage(10), "a dead enemy can't be killed again");
        Assert.AreEqual(1, killed);
        Assert.AreEqual(0, enemy.Health);
    }

    [Test]
    public void TakeDamage_ZeroOrNegative_Ignored()
    {
        Enemy enemy = CreateEnemy(1, Vector2.zero);

        Assert.IsFalse(enemy.TakeDamage(0));
        Assert.IsFalse(enemy.TakeDamage(-5));
        Assert.AreEqual(40, enemy.Health);
        Assert.IsTrue(enemy.IsAlive);
    }

    [Test]
    public void TryAttack_KillingHit_ReportsKilled()
    {
        _goblin.maxHealth = 15;
        _inventory.Add(Track(TestHelpers.Weapon("sword")), 1);
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));
        int killed = 0;
        enemy.OnKilled += _ => killed++;

        Assert.IsTrue(_combat.TryAttack(enemy, out AttackResult result, out string error), error);

        Assert.IsTrue(result.Killed);
        Assert.AreSame(enemy, result.Target);
        Assert.AreEqual(1, killed);
    }

    [Test]
    public void TryAttack_DeadEnemy_EnemyIsGone_NoWear()
    {
        _inventory.Add(Track(TestHelpers.Weapon("sword", maxDurability: 3)), 1);
        Enemy enemy = CreateEnemy(1, new Vector2(1f, 0f));
        enemy.TakeDamage(1000);
        int killed = 0;
        enemy.OnKilled += _ => killed++;

        Assert.IsFalse(_combat.TryAttack(enemy, out _, out string error));

        Assert.AreEqual("enemy is gone", error);
        Assert.AreEqual(3, _inventory.GetSlot(0).Durability, "a refused hit doesn't wear the weapon");
        Assert.AreEqual(0, killed);
        Assert.AreEqual("enemy is gone", _combat.CheckAttack(null));
    }

    // ─── Ближайший враг (через EnemyManager) ──────────────────

    [Test]
    public void TryAttackNearest_PicksClosestWithinRadius()
    {
        _manager = CreateManager();
        Enemy far = Register(CreateEnemy(1, new Vector2(1.4f, 0f)));
        Enemy near = Register(CreateEnemy(2, new Vector2(0f, 1f)));
        Register(CreateEnemy(3, new Vector2(0.5f, 0f))).TakeDamage(1000); // мёртвые не выбираются

        Assert.IsTrue(_combat.TryAttackNearest(out AttackResult result, out string error), error);

        Assert.AreSame(near, result.Target);
        Assert.AreEqual(40, far.Health);
    }

    [Test]
    public void TryAttackNearest_NobodyInReach_Error()
    {
        _manager = CreateManager();
        Register(CreateEnemy(1, new Vector2(3f, 0f)));

        Assert.IsFalse(_combat.TryAttackNearest(out _, out string error));
        Assert.AreEqual("no enemy within reach", error);
    }

    // ─── id врагов в Lua ──────────────────────────────────────

    [Test]
    public void EnemyLuaId_IsOffsetPlusId_DecodedBack_AndDistinctFromObjects()
    {
        Enemy enemy = CreateEnemy(7, new Vector2(1f, 0f));
        StoneObject stone = Create<StoneObject>("Stone");
        ResourceVariant variant = Track(ScriptableObject.CreateInstance<ResourceVariant>());
        variant.drop = Track(TestHelpers.Material("stone"));
        stone.SetVariant(variant);
        stone.SetPosition(new Vector2(2f, 0f));

        LuaTable list = new LuaObjectList(_executor).Build(
            new List<ResourceObject> { stone }, new List<Enemy> { enemy }, meter: null);
        LuaTable stoneTable = list[1].Read<LuaTable>();
        LuaTable enemyTable = list[2].Read<LuaTable>();

        Assert.AreEqual(LuaObjectList.KindEnemy, enemyTable["kind"].Read<string>());
        Assert.AreEqual((double)(LuaObjectList.EnemyIdOffset + 7), enemyTable["id"].Read<double>());
        Assert.IsTrue(LuaObjectList.TryGetEnemyId(enemyTable, out int enemyId));
        Assert.AreEqual(7, enemyId);

        // Объекты, созданные во время игры, имеют отрицательный GetInstanceID — ниже диапазона врагов
        double stoneId = stoneTable["id"].Read<double>();
        Assert.Less(stoneId, LuaObjectList.EnemyIdOffset);
        Assert.AreNotEqual(stoneId, enemyTable["id"].Read<double>());
        Assert.IsFalse(LuaObjectList.TryGetEnemyId(stoneTable, out _), "an object is not an enemy");
        // Головоломки — ещё выше, тоже не пересекаются
        Assert.Greater(LuaObjectList.PuzzleIdOffset, LuaObjectList.EnemyIdOffset + 1000000);
    }

    [Test]
    public void TryGetEnemyId_WrongKindOrMissingId_False()
    {
        var noKind = new LuaTable();
        noKind["id"] = (double)(LuaObjectList.EnemyIdOffset + 3);
        Assert.IsFalse(LuaObjectList.TryGetEnemyId(noKind, out _));

        var puzzle = new LuaTable();
        puzzle["kind"] = "puzzle";
        puzzle["id"] = (double)(LuaObjectList.EnemyIdOffset + 3);
        Assert.IsFalse(LuaObjectList.TryGetEnemyId(puzzle, out _));

        var noId = new LuaTable();
        noId["kind"] = LuaObjectList.KindEnemy;
        Assert.IsFalse(LuaObjectList.TryGetEnemyId(noId, out _));

        Assert.IsFalse(LuaObjectList.TryGetEnemyId(null, out _));
    }

    // ─── Заготовки ────────────────────────────────────────────

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
    }

    private T Create<T>(string name) where T : Component
    {
        var go = Track(new GameObject(name));
        return go.AddComponent<T>();
    }

    private PlayerCommandExecutor CreateExecutor(Vector2Int tile)
    {
        PlayerConfig config = Track(ScriptableObject.CreateInstance<PlayerConfig>());
        // Свой коллайдер — чтобы Initialize не предупреждал о его отсутствии
        GameObject go = Track(new GameObject("Player"));
        go.AddComponent<BoxCollider2D>();
        PlayerCommandExecutor executor = go.AddComponent<PlayerCommandExecutor>();
        executor.Initialize(StubMap.Instance, config, tile);
        executor.transform.position = StubMap.Instance.TileIndexToWorld(tile.x, tile.y);
        return executor;
    }

    /// <summary>
    /// Враг гоблин в точке карты. В EditMode Awake у MonoBehaviour без [ExecuteAlways] не вызывается,
    /// а Setup опирается на то, что он прошёл (DepthSortedSprite) — вызываем его отражением.
    /// </summary>
    private Enemy CreateEnemy(int id, Vector2 position)
    {
        // Коллайдер — до Enemy, чтобы Awake нашёл тело и не предупреждал
        GameObject go = Track(new GameObject("Goblin"));
        go.AddComponent<BoxCollider2D>();
        Enemy enemy = go.AddComponent<Enemy>();
        MethodInfo awake = typeof(Enemy).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(awake, "Enemy.Awake not found");
        awake.Invoke(enemy, null);

        enemy.Setup(id, _goblin, EnemyStats.Resolve(_goblin, null), position, patrol: false, StubWorld.Instance);
        return enemy;
    }

    /// <summary>EnemyManager без генерации: врагов кладём в его списки сами (Spawn требует префаб и Awake).</summary>
    private EnemyManager CreateManager()
    {
        var manager = new EnemyManager(StubMap.Instance, null, _executor, null, _inventory, null,
            config: null, dayCycle: null, spawnEnabled: false);
        _combat = new CombatService(_executor, _inventory, manager);
        return manager;
    }

    private Enemy Register(Enemy enemy)
    {
        GetPrivate<List<Enemy>>(_manager, "_enemies").Add(enemy);
        GetPrivate<Dictionary<int, Enemy>>(_manager, "_byId")[enemy.Id] = enemy;
        return enemy;
    }

    private static T GetPrivate<T>(object target, string field) where T : class
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
        return info.GetValue(target) as T;
    }

    /// <summary>Карта 64×64 из травы; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class StubMap : IMapDataProvider
    {
        public static readonly StubMap Instance = new StubMap();

        public int MapWidth => 64;
        public int MapHeight => 64;
        public int Seed => 1;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y);
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => TileType.Grass;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }

    /// <summary>Мир врага: игрок в (0, 0), время не идёт (Tick не вызывается), ударить игрока нельзя.</summary>
    private sealed class StubWorld : IEnemyWorld
    {
        public static readonly StubWorld Instance = new StubWorld();

        public Vector2 PlayerPosition => Vector2.zero;
        public bool IsPlayerAlive => true;
        public bool CanAttack => false;
        public void DamagePlayer(Enemy attacker, float damage) { }
        public void ShootProjectile(Enemy attacker, Vector2 target) { }
        public bool IsWalkable(Vector2 mapPosition) => true;
        public Vector3 MapToWorld(Vector2 mapPosition) => new Vector3(mapPosition.x, mapPosition.y, 0f);
        public float WorldScale => 1f;
        public Vector2 PickPatrolPoint(Vector2 from, float radius) => from;
    }
}
