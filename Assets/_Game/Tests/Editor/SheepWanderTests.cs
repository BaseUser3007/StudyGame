using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Блуждание овцы: выбор следующей точки, проверка пути, паузы и seed (SheepWanderPlanner).
/// </summary>
public class SheepWanderTests
{
    private static readonly Vector2 Home = new Vector2(10f, 10f);
    private const float Radius = 2f;
    private const float MinStep = 0.5f;

    // ─── TryChooseTarget ──────────────────────────────────────

    [Test]
    public void TryChooseTarget_AllAllowed_PointWithinRadiusAndFarEnough()
    {
        var random = new System.Random(1);
        for (int i = 0; i < 200; i++)
        {
            bool found = SheepWanderPlanner.TryChooseTarget(
                Home, Home, Radius, MinStep, random, _ => true, out Vector2 target);

            Assert.IsTrue(found);
            Assert.LessOrEqual(Vector2.Distance(Home, target), Radius + 1e-4f);
            Assert.GreaterOrEqual(Vector2.Distance(Home, target), MinStep);
        }
    }

    [Test]
    public void TryChooseTarget_SameSeed_SameSequence()
    {
        List<Vector2> first = Sequence(42);
        List<Vector2> second = Sequence(42);

        CollectionAssert.AreEqual(first, second);
        CollectionAssert.AreNotEqual(first, Sequence(43));
    }

    [Test]
    public void TryChooseTarget_NothingAllowed_ReturnsFalseAndCurrent()
    {
        Vector2 current = new Vector2(10.3f, 9.8f);

        bool found = SheepWanderPlanner.TryChooseTarget(
            Home, current, Radius, MinStep, new System.Random(5), _ => false, out Vector2 target);

        Assert.IsFalse(found);
        Assert.AreEqual(current, target);
    }

    [Test]
    public void TryChooseTarget_WaterOnRight_NeverTargetsOrCrossesIt()
    {
        // Вода — всё правее x = 10.5 (как тайлы с x ≥ 11)
        Func<Vector2, bool> land = p => p.x < 10.5f;

        var random = new System.Random(7);
        for (int i = 0; i < 200; i++)
        {
            if (!SheepWanderPlanner.TryChooseTarget(Home, Home, Radius, MinStep, random, land, out Vector2 target))
                continue;

            Assert.Less(target.x, 10.5f);
        }
    }

    [Test]
    public void TryChooseTarget_PathThroughForbiddenStrip_Rejected()
    {
        // Запретная полоса (стена) шире шага проверки пути — между текущей точкой и левой частью круга:
        // туда цель не выбирается, хотя сама точка за полосой разрешена
        Func<Vector2, bool> allowed = p => !(p.x > 9.3f && p.x < 9.7f);

        var random = new System.Random(11);
        for (int i = 0; i < 200; i++)
        {
            if (!SheepWanderPlanner.TryChooseTarget(Home, Home, Radius, MinStep, random, allowed, out Vector2 target))
                continue;

            Assert.GreaterOrEqual(target.x, 9.7f, $"цель {target} за стеной");
        }
    }

    // ─── IsPathAllowed ────────────────────────────────────────

    [Test]
    public void IsPathAllowed_ChecksEndButNotStart()
    {
        Vector2 from = new Vector2(0f, 0f);
        Vector2 to = new Vector2(1f, 0f);

        Assert.IsTrue(SheepWanderPlanner.IsPathAllowed(from, to, p => p != from));
        Assert.IsFalse(SheepWanderPlanner.IsPathAllowed(from, to, p => p.x < 0.9f));
    }

    // ─── NextWait ─────────────────────────────────────────────

    [Test]
    public void NextWait_InRange()
    {
        var random = new System.Random(3);
        for (int i = 0; i < 200; i++)
        {
            float wait = SheepWanderPlanner.NextWait(random, 2f, 4f);
            Assert.GreaterOrEqual(wait, 2f);
            Assert.Less(wait, 4f);
        }
    }

    // ─── MakeSeed ─────────────────────────────────────────────

    [Test]
    public void MakeSeed_DeterministicAndDependsOnMapAndHome()
    {
        int a = SheepWanderPlanner.MakeSeed(123, new Vector2(4.25f, 7.5f));

        Assert.AreEqual(a, SheepWanderPlanner.MakeSeed(123, new Vector2(4.25f, 7.5f)));
        Assert.AreNotEqual(a, SheepWanderPlanner.MakeSeed(124, new Vector2(4.25f, 7.5f)));
        Assert.AreNotEqual(a, SheepWanderPlanner.MakeSeed(123, new Vector2(4.75f, 7.5f)));
        Assert.AreNotEqual(a, SheepWanderPlanner.MakeSeed(123, new Vector2(4.25f, 8.5f)));
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static List<Vector2> Sequence(int seed)
    {
        var random = new System.Random(seed);
        var result = new List<Vector2>();
        Vector2 current = Home;
        for (int i = 0; i < 10; i++)
        {
            SheepWanderPlanner.TryChooseTarget(Home, current, Radius, MinStep, random, _ => true, out current);
            result.Add(current);
        }
        return result;
    }
}
