using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Дневной цикл: запуски, восстановление энергии, штраф за пустой запас, бонус за ночи у кровати.
/// Значения — из PlayerConfig по умолчанию: база 100, минимум 40, бонус 5 за ночь до 25, штраф 15%, 3 запуска.
/// </summary>
public class DayCycleTests
{
    private class FakeBed : ISleepingPlaceProvider
    {
        public bool Near;
        public bool IsSleepingPlaceNearPlayer() => Near;
    }

    private PlayerConfig _config;
    private PlayerEnergy _energy;
    private FakeBed _bed;
    private DayCycle _day;

    [SetUp]
    public void SetUp()
    {
        _config = ScriptableObject.CreateInstance<PlayerConfig>();
        _energy = new PlayerEnergy(_config.baseEnergyLimit);
        _bed = new FakeBed();
        _day = new DayCycle(_config, _energy, _bed);
    }

    [Test]
    public void ThreeRuns_EndTheDay_AndRefillEnergy()
    {
        _energy.TrySpend(30f);

        _day.RegisterRunFinished();
        _day.RegisterRunFinished();
        Assert.AreEqual(1, _day.Day);
        Assert.AreEqual(1, _day.RunsLeft);

        _day.RegisterRunFinished();
        Assert.AreEqual(2, _day.Day);
        Assert.AreEqual(0, _day.RunsUsed);
        Assert.AreEqual(100f, _energy.Max);
        Assert.AreEqual(100f, _energy.Current, "energy refilled for the new day");
    }

    [Test]
    public void EndingDayWithoutEnergy_PenalizesNextDayOnly()
    {
        _energy.TrySpend(99.5f);
        _day.TryEndDay();
        Assert.AreEqual(85f, _energy.Max, 0.001f);
        Assert.AreEqual(85f, _energy.Current, 0.001f);

        // Следующий день закончился с запасом — штраф не копится и снимается
        _day.TryEndDay();
        Assert.AreEqual(100f, _energy.Max, 0.001f);
    }

    [Test]
    public void EndingDayWithOneEnergy_IsNotPenalized()
    {
        _energy.TrySpend(99f);
        _day.TryEndDay();
        Assert.AreEqual(100f, _energy.Max, 0.001f);
    }

    [Test]
    public void SleepingInBed_GivesGrowingBonus_UpToCap()
    {
        _bed.Near = true;

        float[] expected = { 105f, 110f, 115f, 120f, 125f, 125f };
        foreach (float max in expected)
        {
            _day.TryEndDay();
            Assert.AreEqual(max, _energy.Max, 0.001f);
        }
    }

    [Test]
    public void MissedNight_ResetsBedStreak()
    {
        _bed.Near = true;
        _day.TryEndDay();
        _day.TryEndDay();
        Assert.AreEqual(110f, _energy.Max, 0.001f);

        _bed.Near = false;
        _day.TryEndDay();
        Assert.AreEqual(100f, _energy.Max, 0.001f);

        _bed.Near = true;
        _day.TryEndDay();
        Assert.AreEqual(105f, _energy.Max, 0.001f);
    }

    [Test]
    public void PenaltyAndBonus_Combine()
    {
        _bed.Near = true;
        _energy.TrySpend(100f);
        _day.TryEndDay();
        Assert.AreEqual(90f, _energy.Max, 0.001f, "100 + 5 − 15");
    }

    [Test]
    public void MaxEnergy_NeverDropsBelowMinimum()
    {
        _config.energyPenaltyMultiplier = 0.9f;
        _energy.TrySpend(100f);
        _day.TryEndDay();
        Assert.AreEqual(_config.minEnergyLimit, _energy.Max, 0.001f);
    }
}
