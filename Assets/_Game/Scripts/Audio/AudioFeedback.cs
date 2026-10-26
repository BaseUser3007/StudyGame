using System;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Звуки на действия (T09): подбор, добыча, крафт, постройка, победа над врагом, урон игроку, еда,
/// итог запуска кода, достижение. Plain class, создаётся в Level, Dispose — в Level.OnDestroy
/// (подписан на статическую GameEventBus). Клипы — из AudioConfig, иначе процедурные (ProceduralSounds).
/// Без AudioConfig молчит, но переключатель звука в лагере работает. Настройка — MetaProgress.SoundMuted.
/// </summary>
public sealed class AudioFeedback : IDisposable
{
    private readonly AudioConfig _config;
    private readonly MetaProgress _progress;
    private readonly EnemyManager _enemies;
    private readonly AchievementTracker _achievements;
    private readonly RunScreens _screens;
    private readonly SoundRateLimiter _limiter = new SoundRateLimiter();

    private GameObject _root;
    // Звуки мира — со случайным разбросом высоты; итоги запусков и достижения — без него
    private AudioSource _worldSource;
    private AudioSource _uiSource;
    private bool _disposed;

    /// <summary>Звук выключен игроком (MetaProgress.SoundMuted).</summary>
    public bool IsMuted => _progress != null && _progress.SoundMuted;

    public AudioFeedback(
        AudioConfig config, MetaProgress progress, EnemyManager enemies, AchievementTracker achievements,
        RunScreens screens)
    {
        _config = config;
        _progress = progress;
        _enemies = enemies;
        _achievements = achievements;
        _screens = screens;

        // Переключатель в лагере — и без конфига (настройка сохраняется для следующего запуска)
        if (_screens != null)
        {
            _screens.SetSoundEnabled(!IsMuted);
            _screens.OnSoundToggleRequested += HandleSoundToggleRequested;
        }

        // Без конфига игра молчит (предупреждение выводит Level)
        if (_config == null) return;

        _root = new GameObject("AudioFeedback");
        _worldSource = CreateSource(_root);
        _uiSource = CreateSource(_root);

        GameEventBus.OnGameEvent += HandleGameEvent;
        GameEventBus.OnRunFinished += HandleRunFinished;
        if (_enemies != null) _enemies.OnPlayerAttacked += HandlePlayerAttacked;
        if (_achievements != null) _achievements.OnAchievementCompleted += HandleAchievementCompleted;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_screens != null) _screens.OnSoundToggleRequested -= HandleSoundToggleRequested;

        if (_config == null) return;

        GameEventBus.OnGameEvent -= HandleGameEvent;
        GameEventBus.OnRunFinished -= HandleRunFinished;
        if (_enemies != null) _enemies.OnPlayerAttacked -= HandlePlayerAttacked;
        if (_achievements != null) _achievements.OnAchievementCompleted -= HandleAchievementCompleted;

        if (_root != null) Object.Destroy(_root);
        _root = null;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Сыграть звук id с учётом настройки, громкости и ограничения частоты.
    /// True — звук запущен.
    /// </summary>
    public bool Play(SoundId id)
    {
        if (_disposed || _config == null || IsMuted || _worldSource == null) return false;

        SoundSettings settings = _config.Get(id);
        float volume = settings.volume * _config.masterVolume;
        if (volume <= 0f) return false;

        if (!_limiter.TryPlay((int)id, Time.unscaledTimeAsDouble, _config.GetMinInterval(id))) return false;

        AudioClip clip = settings.clip != null ? settings.clip : ProceduralSounds.GetClip(id);
        if (clip == null) return false;

        AudioSource source = IsWorldSound(id) ? _worldSource : _uiSource;
        if (source == _worldSource)
            source.pitch = 1f + UnityEngine.Random.Range(-_config.pitchVariation, _config.pitchVariation);
        source.PlayOneShot(clip, volume);
        return true;
    }

    // ─── Обработчики ──────────────────────────────────────────

    private void HandleGameEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Type)
        {
            case GameEventType.Harvested: Play(SoundId.Pickup); break;
            case GameEventType.Mined: Play(SoundId.Mine); break;
            case GameEventType.Crafted: Play(SoundId.Craft); break;
            case GameEventType.Built:
            case GameEventType.HubBuildingBuilt:
            case GameEventType.HubBuildingUpgraded:
                Play(SoundId.Build);
                break;
            case GameEventType.EnemyKilled: Play(SoundId.EnemyKilled); break;
            case GameEventType.FoodEaten: Play(SoundId.Eat); break;
        }
    }

    private void HandleRunFinished(CodeRunReport report)
    {
        // Остановка (Stop, смерть, watchdog) — без звука: игрок сам её вызвал или слышит удар
        if (report.Outcome == GameEventBus.RunTagOk) Play(SoundId.RunSuccess);
        else if (report.Outcome == GameEventBus.RunTagEnergy) Play(SoundId.RunOutOfEnergy);
        else if (report.Outcome == GameEventBus.RunTagError || report.Outcome == GameEventBus.RunTagLocked)
            Play(SoundId.RunError);
    }

    private void HandlePlayerAttacked(Enemy attacker, float damage) => Play(SoundId.PlayerHurt);

    private void HandleAchievementCompleted(AchievementDefinition definition) => Play(SoundId.Achievement);

    private void HandleSoundToggleRequested()
    {
        if (_progress == null) return;

        _progress.SoundMuted = !_progress.SoundMuted;
        _progress.Save();
        _screens?.SetSoundEnabled(!_progress.SoundMuted);

        if (_progress.SoundMuted)
        {
            if (_worldSource != null) _worldSource.Stop();
            if (_uiSource != null) _uiSource.Stop();
        }
        else
        {
            // Короткий звук — понятно, что звук снова включён
            Play(SoundId.Pickup);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static bool IsWorldSound(SoundId id) =>
        id == SoundId.Pickup || id == SoundId.Mine || id == SoundId.Build || id == SoundId.Eat
        || id == SoundId.EnemyKilled || id == SoundId.PlayerHurt;

    private static AudioSource CreateSource(GameObject root)
    {
        AudioSource source = root.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        // 2D: громкость не зависит от положения камеры
        source.spatialBlend = 0f;
        return source;
    }
}
