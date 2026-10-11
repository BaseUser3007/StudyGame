using System.Collections.Generic;
using Lua;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Массив, который возвращает findObjects: count, id, поиск, фильтрация, сортировка, пропуск собранных.
/// Персонаж стоит в (0, 0); объекты — камни в разных точках.
/// </summary>
public class ObjectListTests
{
    private readonly List<GameObject> _created = new List<GameObject>();
    private TestHelpers.LuaHost _lua;
    private List<ResourceObject> _stones;

    [SetUp]
    public void SetUp()
    {
        var executor = Create<PlayerCommandExecutor>("Player");

        // Камень, который можно «собрать», — чтобы проверить пропуск собранных
        ResourceVariant variant = ScriptableObject.CreateInstance<ResourceVariant>();
        variant.drop = TestHelpers.Material("stone");

        _stones = new List<ResourceObject>
        {
            Stone(new Vector2(3, 0), variant),   // distance 3
            Stone(new Vector2(1, 0), variant),   // distance 1 — ближайший
            Stone(new Vector2(0, 5), variant)    // distance 5 — самый дальний
        };

        _lua = new TestHelpers.LuaHost(TestHelpers.ProjectEnergyConfig(), 1000f);
        _lua.State.Environment["L"] = new LuaObjectList(executor).Build(_stones, _lua.Meter);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _created)
            Object.DestroyImmediate(go);
        _created.Clear();
    }

    [Test]
    public void Count_AndIpairs()
    {
        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("return L.count")));
        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("local n = 0 for _ in ipairs(L) do n = n + 1 end return n")));
    }

    [Test]
    public void GetNear_GetFar_GetMinMax()
    {
        Assert.AreEqual(1, TestHelpers.Number(_lua.Run("return L:getNear().x")));
        Assert.AreEqual(5, TestHelpers.Number(_lua.Run("return L:getFar().y")));
        Assert.AreEqual(0, TestHelpers.Number(_lua.Run("return L:getMin('x').x")));
        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("return L:getMax('x').x")));
    }

    [Test]
    public void Where_SortBy_Take_Chain()
    {
        Assert.AreEqual(2, TestHelpers.Number(_lua.Run("return L:where('distance', '<', 4).count")));
        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("return L:sortBy('x', true)[1].x")));
        Assert.AreEqual(1, TestHelpers.Number(_lua.Run("return L:sortBy('distance'):take(2)[1].x")));
        Assert.AreEqual(1, TestHelpers.Number(_lua.Run(
            "return L:where('distance', '<', 4):sortBy('distance'):take(1).count")));
    }

    [Test]
    public void Where_WithNilMatchesMissingField()
    {
        // У камня нет нужного инструмента — tool == nil
        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("return L:where('tool', '==', nil).count")));
    }

    [Test]
    public void Ids_IdentifyObjects_AcrossLists()
    {
        LuaValue same = _lua.Run("local a = L:getNear() local b = L:sortBy('distance')[1] return a.id == b.id");
        Assert.IsTrue(same.TryRead(out bool equal) && equal);

        Assert.AreEqual(2, TestHelpers.Number(_lua.Run("return L:indexOf(L:getNear())")));
        LuaValue contains = _lua.Run("return L:take(1):contains(L:getNear())");
        Assert.IsTrue(contains.TryRead(out bool has) && !has, "nearest is not the first element");
    }

    [Test]
    public void Sum_AddsNumericField()
    {
        Assert.AreEqual(4, TestHelpers.Number(_lua.Run("return L:sum('x')")));
    }

    [Test]
    public void CollectedObjects_AreSkipped()
    {
        _stones[1].Interact(); // ближайший собран

        Assert.AreEqual(3, TestHelpers.Number(_lua.Run("return L:getNear().x")));
        Assert.AreEqual(2, TestHelpers.Number(_lua.Run("return L:where('x', '>=', 0).count")));
    }

    [Test]
    public void Methods_CostEnergy()
    {
        _lua.Run("L:getNear() L:where('x', '>', 0) L:sum('x')");
        Assert.AreEqual(3 * LuaObjectList.MethodCost, _lua.Spent, 0.001f);
    }

    [Test]
    public void UnknownField_IsAnError()
    {
        Assert.Catch(() => _lua.Run("return L:getMin('type')"), "type is not numeric");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private T Create<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        _created.Add(go);
        return go.AddComponent<T>();
    }

    private ResourceObject Stone(Vector2 position, ResourceVariant variant)
    {
        StoneObject stone = Create<StoneObject>("Stone");
        stone.SetVariant(variant);
        stone.SetPosition(position);
        return stone;
    }
}
