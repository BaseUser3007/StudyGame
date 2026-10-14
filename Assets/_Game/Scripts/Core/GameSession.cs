using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameMode
{
    Hub,
    Run
}

/// <summary>
/// Состояние между загрузками сцены: режим (хаб или забег) и мета-прогресс.
/// Переход хаб ↔ забег перезагружает сцену — каждый забег начинается с чистого состояния
/// и новой карты (MapGenerationConfig.randomSeed).
/// </summary>
public static class GameSession
{
    private static GameMode? _requestedMode;
    private static MetaProgress _progress;

    public static MetaProgress Progress => _progress ??= new MetaProgress();

    /// <summary>
    /// Режим, запрошенный переходом. null — первая загрузка: режим выбирает Level.
    /// </summary>
    public static GameMode? RequestedMode => _requestedMode;

    // Статика переживает Play Mode без перезагрузки домена — сбрасываем вручную
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _requestedMode = null;
        _progress = null;
    }

    // ─── Переходы ─────────────────────────────────────────────

    public static void StartRun() => LoadMode(GameMode.Run);

    public static void ReturnToHub() => LoadMode(GameMode.Hub);

    private static void LoadMode(GameMode mode)
    {
        _requestedMode = mode;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
