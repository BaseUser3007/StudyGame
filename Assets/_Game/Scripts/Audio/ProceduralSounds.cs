using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Звуки обратной связи, синтезированные в коде (T09): никаких скачанных файлов — простые волны
/// (синус, треугольник, мягкий меандр, пила) и шум с огибающей, 44.1 кГц моно.
/// Synthesize — чистая генерация сэмплов (тесты), GetClip — AudioClip, созданный один раз и закэшированный.
/// </summary>
public static class ProceduralSounds
{
    public const int SampleRate = 44100;
    /// <summary>Пиковая амплитуда после нормализации (запас до 1, чтобы наложение звуков не клиппировало).</summary>
    public const float PeakAmplitude = 0.8f;

    // Хвост, за который огибающая плавно уходит в ноль — без щелчка в конце звука
    private const float ReleaseSeconds = 0.006f;

    private static readonly Dictionary<SoundId, AudioClip> Cache = new Dictionary<SoundId, AudioClip>();

    // Статика переживает Play Mode без перезагрузки домена — клипы прошлой сессии могли быть выгружены
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Cache.Clear();

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Клип звука id: создаётся при первом запросе и кэшируется.</summary>
    public static AudioClip GetClip(SoundId id)
    {
        if (Cache.TryGetValue(id, out AudioClip cached) && cached != null)
            return cached;

        AudioClip clip = CreateClip(id);
        Cache[id] = clip;
        return clip;
    }

    /// <summary>Новый (не кэшированный) клип звука id.</summary>
    public static AudioClip CreateClip(SoundId id)
    {
        float[] samples = Synthesize(id);
        AudioClip clip = AudioClip.Create("Sfx_" + id, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Длительность звука id в секундах.</summary>
    public static float GetDuration(SoundId id) => id switch
    {
        SoundId.Pickup => 0.09f,
        SoundId.Mine => 0.22f,
        SoundId.Craft => 0.5f,
        SoundId.Build => 0.34f,
        SoundId.EnemyKilled => 0.36f,
        SoundId.PlayerHurt => 0.22f,
        SoundId.Eat => 0.24f,
        SoundId.RunSuccess => 0.44f,
        SoundId.RunError => 0.4f,
        SoundId.RunOutOfEnergy => 0.6f,
        SoundId.Achievement => 0.95f,
        _ => 0.1f
    };

    /// <summary>Сэмплы звука id (моно, SampleRate), нормализованные к PeakAmplitude.</summary>
    public static float[] Synthesize(SoundId id)
    {
        float[] buffer = new float[Mathf.CeilToInt(GetDuration(id) * SampleRate)];

        switch (id)
        {
            case SoundId.Pickup:
                // Короткий восходящий «блип»
                AddTone(buffer, 0f, 0.09f, 900f, 1500f, Wave.Triangle, 1f, 0.003f, 30f);
                AddTone(buffer, 0f, 0.06f, 1800f, 3000f, Wave.Sine, 0.25f, 0.003f, 45f);
                break;

            case SoundId.Mine:
                // Глухой удар: низкий спадающий тон + щелчок шума + призвук «топора»
                AddTone(buffer, 0f, 0.22f, 140f, 55f, Wave.Sine, 1f, 0.002f, 18f);
                AddNoise(buffer, 0f, 0.08f, 0.6f, 0.001f, 40f, 0.25f, 11);
                AddTone(buffer, 0f, 0.03f, 900f, 600f, Wave.Square, 0.15f, 0.001f, 120f);
                break;

            case SoundId.Craft:
                // Колокольчик из двух нот (до и ми шестой октавы) с обертоном
                AddBell(buffer, 0f, 0.35f, 1046.5f, 1f);
                AddBell(buffer, 0.09f, 0.41f, 1318.5f, 0.9f);
                break;

            case SoundId.Build:
                // Два «тук» — молоток по дереву
                AddTone(buffer, 0f, 0.2f, 120f, 60f, Wave.Sine, 1f, 0.002f, 20f);
                AddNoise(buffer, 0f, 0.05f, 0.5f, 0.001f, 50f, 0.3f, 21);
                AddTone(buffer, 0.13f, 0.21f, 160f, 80f, Wave.Sine, 0.7f, 0.002f, 20f);
                AddNoise(buffer, 0.13f, 0.05f, 0.35f, 0.001f, 50f, 0.3f, 22);
                break;

            case SoundId.EnemyKilled:
                // Падающий «бззт» + шумовой всплеск
                AddTone(buffer, 0f, 0.36f, 520f, 110f, Wave.Square, 0.5f, 0.002f, 7f);
                AddNoise(buffer, 0f, 0.12f, 0.5f, 0.001f, 25f, 0.5f, 31);
                break;

            case SoundId.PlayerHurt:
                // Резкий шум удара + низкий хрип пилы
                AddNoise(buffer, 0f, 0.1f, 0.8f, 0.001f, 30f, 0.6f, 41);
                AddTone(buffer, 0f, 0.22f, 260f, 120f, Wave.Saw, 0.6f, 0.002f, 12f);
                break;

            case SoundId.Eat:
                // Два «хрум»: глухой шум с низким призвуком
                AddNoise(buffer, 0f, 0.08f, 0.8f, 0.005f, 35f, 0.2f, 51);
                AddTone(buffer, 0f, 0.08f, 210f, 150f, Wave.Sine, 0.35f, 0.005f, 30f);
                AddNoise(buffer, 0.13f, 0.08f, 0.7f, 0.005f, 35f, 0.2f, 52);
                AddTone(buffer, 0.13f, 0.08f, 190f, 140f, Wave.Sine, 0.3f, 0.005f, 30f);
                break;

            case SoundId.RunSuccess:
                // Восходящее арпеджио до-ми-соль
                AddTone(buffer, 0f, 0.14f, 523.25f, 523.25f, Wave.Triangle, 0.9f, 0.004f, 10f);
                AddTone(buffer, 0.1f, 0.14f, 659.25f, 659.25f, Wave.Triangle, 0.9f, 0.004f, 10f);
                AddTone(buffer, 0.2f, 0.24f, 783.99f, 783.99f, Wave.Triangle, 1f, 0.004f, 8f);
                break;

            case SoundId.RunError:
                // Два низких «бу-бу» (соль — ре)
                AddTone(buffer, 0f, 0.15f, 196f, 196f, Wave.Square, 0.5f, 0.004f, 6f);
                AddTone(buffer, 0.18f, 0.22f, 147f, 147f, Wave.Square, 0.5f, 0.004f, 6f);
                break;

            case SoundId.RunOutOfEnergy:
                // «Сдувающийся» спад высоты — энергия кончилась
                AddTone(buffer, 0f, 0.6f, 700f, 140f, Wave.Sine, 1f, 0.005f, 3f);
                AddTone(buffer, 0f, 0.6f, 350f, 70f, Wave.Triangle, 0.4f, 0.005f, 3f);
                break;

            case SoundId.Achievement:
                // Фанфара: до-ми-соль-до с долгой последней нотой и «блеском» октавой выше
                AddTone(buffer, 0f, 0.12f, 523.25f, 523.25f, Wave.Triangle, 0.8f, 0.004f, 8f);
                AddTone(buffer, 0.1f, 0.12f, 659.25f, 659.25f, Wave.Triangle, 0.8f, 0.004f, 8f);
                AddTone(buffer, 0.2f, 0.12f, 783.99f, 783.99f, Wave.Triangle, 0.8f, 0.004f, 8f);
                AddTone(buffer, 0.3f, 0.65f, 1046.5f, 1046.5f, Wave.Triangle, 1f, 0.004f, 4f);
                AddTone(buffer, 0.3f, 0.65f, 2093f, 2093f, Wave.Sine, 0.2f, 0.004f, 6f);
                break;
        }

        Normalize(buffer, PeakAmplitude);
        return buffer;
    }

    // ─── Синтез ───────────────────────────────────────────────

    private enum Wave
    {
        Sine,
        Triangle,
        Square,
        Saw
    }

    /// <summary>
    /// Тон с экспоненциальным глиссандо f0 → f1 и огибающей: линейная атака, экспоненциальный спад
    /// (decayRate — 1/с), короткое затухание в конце.
    /// </summary>
    private static void AddTone(
        float[] buffer, float start, float duration, float f0, float f1, Wave wave, float amplitude,
        float attack, float decayRate)
    {
        int first = (int)(start * SampleRate);
        int count = (int)(duration * SampleRate);
        double phase = 0d;
        double ratio = f1 / (double)f0;

        for (int i = 0; i < count; i++)
        {
            int index = first + i;
            if (index >= buffer.Length) break;

            float t = i / (float)SampleRate;
            double frequency = f0 * Math.Pow(ratio, i / (double)count);
            phase += frequency / SampleRate;
            phase -= Math.Floor(phase);

            buffer[index] += Sample(wave, phase) * amplitude * Envelope(t, duration, attack, decayRate);
        }
    }

    /// <summary>Колокольчик: синус с обертонами 2× и 3×, быстрый спад.</summary>
    private static void AddBell(float[] buffer, float start, float duration, float frequency, float amplitude)
    {
        AddTone(buffer, start, duration, frequency, frequency, Wave.Sine, amplitude, 0.002f, 9f);
        AddTone(buffer, start, duration, frequency * 2f, frequency * 2f, Wave.Sine, amplitude * 0.3f, 0.002f, 14f);
        AddTone(buffer, start, duration, frequency * 3f, frequency * 3f, Wave.Sine, amplitude * 0.12f, 0.002f, 20f);
    }

    /// <summary>
    /// Белый шум через однополюсный фильтр низких частот (lowpass 0..1: меньше — глуше),
    /// с той же огибающей. Seed фиксирован — звук всегда одинаковый.
    /// </summary>
    private static void AddNoise(
        float[] buffer, float start, float duration, float amplitude, float attack, float decayRate,
        float lowpass, int seed)
    {
        System.Random random = new System.Random(seed);
        int first = (int)(start * SampleRate);
        int count = (int)(duration * SampleRate);
        float filtered = 0f;

        for (int i = 0; i < count; i++)
        {
            int index = first + i;
            if (index >= buffer.Length) break;

            float white = (float)(random.NextDouble() * 2d - 1d);
            filtered += (white - filtered) * lowpass;
            float t = i / (float)SampleRate;
            buffer[index] += filtered * amplitude * Envelope(t, duration, attack, decayRate);
        }
    }

    private static float Sample(Wave wave, double phase)
    {
        switch (wave)
        {
            case Wave.Triangle:
                return (float)(1d - 4d * Math.Abs(phase - 0.5d));
            case Wave.Square:
                // Мягкий меандр: насыщенный синус — меньше резких гармоник, чем у чистого меандра
                return (float)Math.Tanh(4d * Math.Sin(2d * Math.PI * phase));
            case Wave.Saw:
                return (float)(2d * phase - 1d);
            default:
                return (float)Math.Sin(2d * Math.PI * phase);
        }
    }

    private static float Envelope(float t, float duration, float attack, float decayRate)
    {
        float attackGain = attack > 0f ? Math.Min(1f, t / attack) : 1f;
        float decayGain = (float)Math.Exp(-decayRate * t);
        float releaseGain = Math.Min(1f, Math.Max(0f, (duration - t) / ReleaseSeconds));
        return attackGain * decayGain * releaseGain;
    }

    private static void Normalize(float[] buffer, float peak)
    {
        float max = 0f;
        foreach (float sample in buffer)
            max = Math.Max(max, Math.Abs(sample));
        if (max <= 0f) return;

        float scale = peak / max;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= scale;
    }
}
