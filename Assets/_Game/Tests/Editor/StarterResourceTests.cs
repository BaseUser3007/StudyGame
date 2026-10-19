using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Стартовый набор у спавна: подсчёт недостающих объектов и точки в кольце (StarterResourcePlanner).
/// </summary>
public class StarterResourceTests
{
    private static readonly Vector2 Center = new Vector2(10f, 10f);

    // ─── GetMissing ───────────────────────────────────────────

    [Test]
    public void GetMissing_NothingNearby_ReturnsFullCountsInOrder()
    {
        var required = new List<(string key, int count)> { ("stick", 4), ("stone", 4), ("tree", 2) };

        var missing = StarterResourcePlanner.GetMissing(
            required, new List<(string key, Vector2 position)>(), Center, 4f);

        CollectionAssert.AreEqual(
            new List<(string, int)> { ("stick", 4), ("stone", 4), ("tree", 2) }, missing);
    }

    [Test]
    public void GetMissing_CountsOnlySameTypeWithinMaxRadius()
    {
        var required = new List<(string key, int count)> { ("stick", 4), ("tree", 2) };
        var existing = new List<(string key, Vector2 position)>
        {
            ("stick", Center + new Vector2(3.5f, 0f)),   // в обзоре
            ("stick", Center + new Vector2(0f, -4f)),    // ровно на границе — считается
            ("stick", Center + new Vector2(6.8f, 0f)),   // далеко — нет
            ("stone", Center + new Vector2(3f, 0f)),     // другой тип — нет
            ("tree", Center + new Vector2(2.9f, 2.9f))   // ≈4.1 — нет
        };

        var missing = StarterResourcePlanner.GetMissing(required, existing, Center, 4f);

        CollectionAssert.AreEqual(new List<(string, int)> { ("stick", 2), ("tree", 2) }, missing);
    }

    [Test]
    public void GetMissing_EnoughAlready_TypeIsOmitted()
    {
        var required = new List<(string key, int count)> { ("stone", 1), ("tree", 1) };
        var existing = new List<(string key, Vector2 position)>
        {
            ("stone", Center + Vector2.one), ("stone", Center - Vector2.one)
        };

        var missing = StarterResourcePlanner.GetMissing(required, existing, Center, 4f);

        CollectionAssert.AreEqual(new List<(string, int)> { ("tree", 1) }, missing);
    }

    [Test]
    public void GetMissing_MergesDuplicates_SkipsEmptyAndZero()
    {
        var required = new List<(string key, int count)>
        {
            ("stick", 2), (null, 5), ("stone", 0), ("stick", 1), ("tree", -1)
        };

        var missing = StarterResourcePlanner.GetMissing(
            required, new List<(string key, Vector2 position)>(), Center, 4f);

        CollectionAssert.AreEqual(new List<(string, int)> { ("stick", 3) }, missing);
    }

    // ─── Кольцо ───────────────────────────────────────────────

    [Test]
    public void RandomPointInRing_StaysInsideRing()
    {
        var random = new System.Random(42);

        for (int i = 0; i < 1000; i++)
        {
            Vector2 point = StarterResourcePlanner.RandomPointInRing(random, Center, 3f, 4f);
            float distance = Vector2.Distance(Center, point);
            Assert.GreaterOrEqual(distance, 3f - 1e-4f);
            Assert.LessOrEqual(distance, 4f + 1e-4f);
        }
    }

    [Test]
    public void RandomPointInRing_SameSeed_SamePoints()
    {
        var a = new System.Random(7);
        var b = new System.Random(7);

        for (int i = 0; i < 20; i++)
            Assert.AreEqual(
                StarterResourcePlanner.RandomPointInRing(a, Center, 3f, 4f),
                StarterResourcePlanner.RandomPointInRing(b, Center, 3f, 4f));
    }

    [Test]
    public void IsInRing_BoundsInclusive()
    {
        Assert.IsTrue(StarterResourcePlanner.IsInRing(Center, Center + new Vector2(3f, 0f), 3f, 4f));
        Assert.IsTrue(StarterResourcePlanner.IsInRing(Center, Center + new Vector2(0f, 4f), 3f, 4f));
        Assert.IsFalse(StarterResourcePlanner.IsInRing(Center, Center + new Vector2(2.9f, 0f), 3f, 4f));
        Assert.IsFalse(StarterResourcePlanner.IsInRing(Center, Center + new Vector2(4.1f, 0f), 3f, 4f));
    }
}
