using System;
using UnityEngine;

/// <summary>
/// Настройки звука на один вид события: свой клип (пусто — процедурный звук ProceduralSounds),
/// громкость и минимальный интервал между повторами.
/// </summary>
[Serializable]
public class SoundSettings
{
    [Tooltip("Свой клип. Пусто — процедурный звук, синтезированный в коде (ProceduralSounds)")]
    public AudioClip clip;

    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Не чаще одного раза за столько секунд. 0 — значение по умолчанию из AudioConfig")]
    [Min(0f)] public float minInterval;

    public SoundSettings()
    {
    }

    public SoundSettings(float volume, float minInterval = 0f)
    {
        this.volume = volume;
        this.minInterval = minInterval;
    }
}

/// <summary>
/// Звуки обратной связи (T09): общая громкость, ограничение частоты и настройки каждого события.
/// Назначается на Level; без ассета игра работает молча (предупреждение).
/// </summary>
[CreateAssetMenu(fileName = "AudioConfig", menuName = "CodeRogue/Audio/AudioConfig")]
public class AudioConfig : ScriptableObject
{
    [Header("General")]
    [Range(0f, 1f)]
    public float masterVolume = 0.7f;

    [Tooltip("Один и тот же звук — не чаще одного раза за столько секунд (если у звука не задан свой интервал)")]
    [Min(0f)] public float defaultMinInterval = 0.12f;

    [Tooltip("Случайный разброс высоты тона повторяющихся звуков мира (подбор, добыча), ±")]
    [Range(0f, 0.3f)] public float pitchVariation = 0.06f;

    [Header("World")]
    public SoundSettings pickup = new SoundSettings(0.55f);
    public SoundSettings mine = new SoundSettings(0.8f);
    public SoundSettings craft = new SoundSettings(0.7f);
    public SoundSettings build = new SoundSettings(0.8f);
    public SoundSettings enemyKilled = new SoundSettings(0.8f);
    public SoundSettings playerHurt = new SoundSettings(0.8f, 0.2f);
    public SoundSettings eat = new SoundSettings(0.7f);

    [Header("Code runs")]
    public SoundSettings runSuccess = new SoundSettings(0.6f, 0.3f);
    public SoundSettings runError = new SoundSettings(0.6f, 0.3f);
    public SoundSettings runOutOfEnergy = new SoundSettings(0.7f, 0.3f);

    [Header("Meta")]
    public SoundSettings achievement = new SoundSettings(0.8f, 0.5f);

    /// <summary>Настройки звука id (не null).</summary>
    public SoundSettings Get(SoundId id)
    {
        SoundSettings settings = id switch
        {
            SoundId.Pickup => pickup,
            SoundId.Mine => mine,
            SoundId.Craft => craft,
            SoundId.Build => build,
            SoundId.EnemyKilled => enemyKilled,
            SoundId.PlayerHurt => playerHurt,
            SoundId.Eat => eat,
            SoundId.RunSuccess => runSuccess,
            SoundId.RunError => runError,
            SoundId.RunOutOfEnergy => runOutOfEnergy,
            SoundId.Achievement => achievement,
            _ => null
        };
        return settings ?? new SoundSettings();
    }

    /// <summary>Минимальный интервал между повторами звука id (свой или по умолчанию).</summary>
    public float GetMinInterval(SoundId id)
    {
        float own = Get(id).minInterval;
        return own > 0f ? own : defaultMinInterval;
    }
}
