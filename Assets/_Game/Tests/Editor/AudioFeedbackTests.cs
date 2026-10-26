using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Звуки на действия (T09): ограничение частоты, процедурные клипы, громкость/интервалы AudioConfig,
/// настройка «звук выключен» в MetaProgress.
/// </summary>
public class AudioFeedbackTests
{
    // ─── SoundRateLimiter ─────────────────────────────────────

    [Test]
    public void RateLimiter_SameSoundWithinInterval_IsSkipped()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();

        // Интервал 0.25 — точно представим в float/double, граница проверяется без погрешности
        Assert.IsTrue(limiter.TryPlay(1, 10.0, 0.25f));
        Assert.IsFalse(limiter.TryPlay(1, 10.125, 0.25f), "too soon after the first play");
        Assert.IsTrue(limiter.TryPlay(1, 10.25, 0.25f), "exactly one interval later");
    }

    [Test]
    public void RateLimiter_SkippedPlay_DoesNotPushTheWindow()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();

        Assert.IsTrue(limiter.TryPlay(1, 0.0, 0.25f));
        Assert.IsFalse(limiter.TryPlay(1, 0.2, 0.25f));
        // Отсчёт — от последнего сыгранного (0.0), а не от пропущенного (0.2)
        Assert.IsTrue(limiter.TryPlay(1, 0.25, 0.25f));
    }

    [Test]
    public void RateLimiter_DifferentSounds_AreIndependent()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();

        Assert.IsTrue(limiter.TryPlay(1, 5.0, 1f));
        Assert.IsTrue(limiter.TryPlay(2, 5.0, 1f));
        Assert.IsFalse(limiter.TryPlay(1, 5.5, 1f));
    }

    [Test]
    public void RateLimiter_TwentyHarvestsInOneFrameBurst_PlayOnce()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();
        int played = 0;
        // 20 подборов за 0.2 с (цикл harvest) при интервале 0.12 с — два звука, а не двадцать
        for (int i = 0; i < 20; i++)
            if (limiter.TryPlay((int)SoundId.Pickup, i * 0.01, 0.12f))
                played++;

        Assert.AreEqual(2, played);
    }

    [Test]
    public void RateLimiter_ZeroInterval_NeverLimits()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();

        Assert.IsTrue(limiter.TryPlay(1, 1.0, 0f));
        Assert.IsTrue(limiter.TryPlay(1, 1.0, 0f));
    }

    [Test]
    public void RateLimiter_Clear_ForgetsTimes()
    {
        SoundRateLimiter limiter = new SoundRateLimiter();
        limiter.TryPlay(1, 1.0, 10f);
        limiter.Clear();

        Assert.IsTrue(limiter.TryPlay(1, 1.5, 10f));
    }

    // ─── ProceduralSounds ─────────────────────────────────────

    [Test]
    public void Synthesize_EverySound_HasExpectedLengthAndIsAudible()
    {
        foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
        {
            float[] samples = ProceduralSounds.Synthesize(id);
            int expected = Mathf.CeilToInt(ProceduralSounds.GetDuration(id) * ProceduralSounds.SampleRate);

            Assert.AreEqual(expected, samples.Length, $"{id}: length");
            Assert.Less(ProceduralSounds.GetDuration(id), 1.5f, $"{id}: feedback sounds are short");

            float peak = 0f;
            int nonSilent = 0;
            foreach (float sample in samples)
            {
                Assert.IsFalse(float.IsNaN(sample) || float.IsInfinity(sample), $"{id}: invalid sample");
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                if (Mathf.Abs(sample) > 0.01f) nonSilent++;
            }

            Assert.AreEqual(ProceduralSounds.PeakAmplitude, peak, 1e-4f, $"{id}: normalized peak");
            Assert.Greater(nonSilent, samples.Length / 10, $"{id}: mostly silent");
            // Без щелчка в конце: последний сэмпл почти ноль
            Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.05f, $"{id}: click at the end");
        }
    }

    [Test]
    public void Synthesize_IsDeterministic()
    {
        CollectionAssert.AreEqual(
            ProceduralSounds.Synthesize(SoundId.Mine), ProceduralSounds.Synthesize(SoundId.Mine));
    }

    [Test]
    public void CreateClip_IsMono44kWithSynthesizedLength()
    {
        AudioClip clip = ProceduralSounds.CreateClip(SoundId.Craft);
        try
        {
            Assert.IsNotNull(clip);
            Assert.AreEqual(1, clip.channels);
            Assert.AreEqual(ProceduralSounds.SampleRate, clip.frequency);
            Assert.AreEqual(ProceduralSounds.Synthesize(SoundId.Craft).Length, clip.samples);
            Assert.AreEqual(ProceduralSounds.GetDuration(SoundId.Craft), clip.length, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(clip);
        }
    }

    // ─── AudioConfig ──────────────────────────────────────────

    [Test]
    public void AudioConfig_EverySoundHasSettings_AndIntervalFallsBackToDefault()
    {
        AudioConfig config = ScriptableObject.CreateInstance<AudioConfig>();
        try
        {
            config.defaultMinInterval = 0.2f;
            config.pickup.minInterval = 0f;
            config.playerHurt.minInterval = 0.5f;

            foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
                Assert.IsNotNull(config.Get(id), $"{id}: no settings");

            Assert.AreEqual(0.2f, config.GetMinInterval(SoundId.Pickup), 1e-6f);
            Assert.AreEqual(0.5f, config.GetMinInterval(SoundId.PlayerHurt), 1e-6f);
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    // ─── MetaProgress.SoundMuted ──────────────────────────────

    [Test]
    public void SoundMuted_DefaultsToFalse_SurvivesReloadAndReset()
    {
        using TestHelpers.TempSaveFolder saves = new TestHelpers.TempSaveFolder();

        MetaProgress progress = saves.Progress();
        Assert.IsFalse(progress.SoundMuted, "sound is on by default");

        progress.SoundMuted = true;
        Assert.IsTrue(progress.Save());
        Assert.IsTrue(saves.Progress().SoundMuted, "the setting survives a restart");

        // Настройка, как язык, переживает сброс прогресса
        Assert.IsTrue(progress.ResetAll());
        Assert.IsTrue(saves.Progress().SoundMuted, "the setting survives a progress reset");
    }

    [Test]
    public void SoundMuted_OldSaveWithoutField_IsFalse()
    {
        using TestHelpers.TempSaveFolder saves = new TestHelpers.TempSaveFolder();
        File.WriteAllText(saves.PathOf("meta_progress.json"), @"{ ""hintsEnabled"": true, ""runsFinished"": 1 }");

        Assert.IsFalse(saves.Progress().SoundMuted);
    }
}
