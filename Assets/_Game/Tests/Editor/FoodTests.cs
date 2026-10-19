using System.Collections.Generic;
using Lua;
using NUnit.Framework;

/// <summary>
/// Еда: eat() восстанавливает энергию и здоровье не выше максимума, «already full» не тратит предмет,
/// FoodEaten в GameEventBus; PlayerEnergy.Restore и PlayerHealth.Heal.
/// </summary>
public class FoodTests
{
    private TestHelpers.LuaHost _lua;
    private PlayerHealth _health;
    private PlayerInventory _inventory;
    private FoodItem _berries;
    private FoodItem _meat;
    private MaterialResource _stone;
    private readonly List<GameEvent> _events = new List<GameEvent>();

    [SetUp]
    public void SetUp()
    {
        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 100f);
        _health = new PlayerHealth(100f);
        _inventory = new PlayerInventory(5);

        _berries = Food("berries", energy: 6f, health: 0f);
        _meat = Food("cooked_meat", energy: 0f, health: 25f);
        _stone = TestHelpers.Material("stone");

        new EatCommand(_lua.Energy, _health, _inventory).Register(_lua.State, _lua.Meter);

        _events.Clear();
        GameEventBus.OnGameEvent += HandleGameEvent;
    }

    [TearDown]
    public void TearDown()
    {
        GameEventBus.OnGameEvent -= HandleGameEvent;
    }

    private void HandleGameEvent(GameEvent gameEvent) => _events.Add(gameEvent);

    private static FoodItem Food(string id, float energy, float health)
    {
        FoodItem food = TestHelpers.Item<FoodItem>(id);
        food.energyRestore = energy;
        food.healthRestore = health;
        return food;
    }

    /// <summary>Выполняет вызов и раскладывает результаты eat по глобальным a, b, c.</summary>
    private void Eat(string args) => _lua.Run($"a, b, c = eat({args})");

    private LuaValue Global(string name) => _lua.State.Environment[name];

    // ─── Энергия ──────────────────────────────────────────────

    [Test]
    public void Eat_RestoresEnergy_AndConsumesOne()
    {
        _inventory.Add(_berries, 2);
        _lua.Energy.TrySpend(20f);

        Eat("\"berries\"");

        Assert.IsTrue(Global("a").ToBoolean());
        Assert.AreEqual(6, TestHelpers.Number(Global("b")), 0.001);
        Assert.AreEqual(0, TestHelpers.Number(Global("c")), 0.001);
        // 100 − 20 − 1 (вызов) + 6
        Assert.AreEqual(85f, _lua.Energy.Current, 0.001f);
        Assert.AreEqual(1, _inventory.GetCount(_berries));
    }

    [Test]
    public void Eat_EnergyCappedAtMax()
    {
        _inventory.Add(_berries, 1);
        _lua.Energy.TrySpend(3f);

        Eat("\"berries\"");

        // 97 − 1 = 96, места только на 4
        Assert.AreEqual(4, TestHelpers.Number(Global("b")), 0.001);
        Assert.AreEqual(100f, _lua.Energy.Current, 0.001f);
        Assert.AreEqual(0, _inventory.GetCount(_berries));
    }

    [Test]
    public void Eat_AtFullEnergy_AlreadyFull_KeepsItem_ButCallIsPaid()
    {
        _inventory.Add(_berries, 1);

        Eat("\"berries\"");

        Assert.IsTrue(Global("a").Type == LuaValueType.Nil);
        Assert.AreEqual(EatCommand.AlreadyFull, Global("b").Read<string>());
        Assert.AreEqual(1, _inventory.GetCount(_berries), "food is not consumed");
        Assert.AreEqual(99f, _lua.Energy.Current, 0.001f, "the call itself costs 1");
        Assert.IsFalse(_events.Exists(e => e.Type == GameEventType.FoodEaten), "nothing was eaten");
    }

    [Test]
    public void Restore_DoesNotReduceTotalSpent()
    {
        var energy = new PlayerEnergy(10f);
        energy.TrySpend(5f);

        Assert.AreEqual(3f, energy.Restore(3f), 0.001f);
        Assert.AreEqual(2f, energy.Restore(10f), 0.001f, "capped at max");
        Assert.AreEqual(0f, energy.Restore(1f), 0.001f);
        Assert.AreEqual(10f, energy.Current, 0.001f);
        Assert.AreEqual(5f, energy.TotalSpent, 0.001f);
    }

    // ─── Здоровье ─────────────────────────────────────────────

    [Test]
    public void Eat_HealsUpToMax()
    {
        _inventory.Add(_meat, 1);
        _health.TakeDamage(10f);

        Eat("\"cooked_meat\"");

        Assert.IsTrue(Global("a").ToBoolean());
        Assert.AreEqual(10, TestHelpers.Number(Global("c")), 0.001);
        Assert.AreEqual(100f, _health.Current, 0.001f);
        Assert.AreEqual(0, _inventory.GetCount(_meat));
    }

    [Test]
    public void Eat_HealthFoodAtFullHealth_AlreadyFull()
    {
        _inventory.Add(_meat, 1);
        _lua.Energy.TrySpend(50f);

        Eat("\"cooked_meat\"");

        Assert.AreEqual(EatCommand.AlreadyFull, Global("b").Read<string>());
        Assert.AreEqual(1, _inventory.GetCount(_meat));
    }

    [Test]
    public void Heal_ReturnsHealedAmount_AndDoesNotHealTheDead()
    {
        var health = new PlayerHealth(50f);
        health.TakeDamage(20f);

        Assert.AreEqual(20f, health.Heal(100f), 0.001f);
        Assert.AreEqual(0f, health.Heal(5f), 0.001f);

        health.TakeDamage(50f);
        Assert.IsTrue(health.IsDead);
        Assert.AreEqual(0f, health.Heal(10f), 0.001f);
        Assert.AreEqual(0f, health.Current, 0.001f);
    }

    // ─── Выбор предмета ───────────────────────────────────────

    [Test]
    public void Eat_BySlot()
    {
        _inventory.Add(_stone, 1);
        _inventory.Add(_berries, 3);
        _lua.Energy.TrySpend(20f);

        Eat("2");

        Assert.IsTrue(Global("a").ToBoolean());
        Assert.AreEqual(2, _inventory.GetCount(_berries));
    }

    [Test]
    public void Eat_NotFood_Fails_AndKeepsItem()
    {
        _inventory.Add(_stone, 1);
        _lua.Energy.TrySpend(20f);

        Eat("\"stone\"");
        Assert.IsTrue(Global("a").Type == LuaValueType.Nil);
        StringAssert.Contains("not food", Global("b").Read<string>());

        Eat("1");
        StringAssert.Contains("not food", Global("b").Read<string>());

        Assert.AreEqual(1, _inventory.GetCount(_stone));
    }

    [Test]
    public void Eat_MissingOrEmptySlot_Fails()
    {
        _lua.Energy.TrySpend(20f);

        Eat("\"berries\"");
        StringAssert.Contains("no berries", Global("b").Read<string>());

        Eat("1");
        StringAssert.Contains("empty", Global("b").Read<string>());

        Eat("99");
        StringAssert.Contains("slot must be", Global("b").Read<string>());
    }

    // ─── События ──────────────────────────────────────────────

    [Test]
    public void Eat_PublishesFoodEaten_WithItemKey()
    {
        _inventory.Add(_berries, 1);
        _lua.Energy.TrySpend(20f);

        Eat("\"BERRIES\"");

        GameEvent eaten = _events.Find(e => e.Type == GameEventType.FoodEaten);
        Assert.AreEqual(GameEventType.FoodEaten, eaten.Type);
        Assert.AreEqual("berries", eaten.Id);
        Assert.AreEqual(1, eaten.Amount);
    }
}
