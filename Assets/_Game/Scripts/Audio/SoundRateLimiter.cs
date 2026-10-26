using System.Collections.Generic;

/// <summary>
/// Ограничение частоты одного и того же звука: не чаще одного раза за minInterval секунд.
/// Цикл из 20 harvest не должен трещать — лишние повторы просто пропускаются.
/// Чистый класс (время передаётся снаружи) — проверяется Edit Mode тестами.
/// </summary>
public sealed class SoundRateLimiter
{
    private readonly Dictionary<int, double> _lastPlayed = new Dictionary<int, double>();

    /// <summary>
    /// Можно ли сыграть звук key в момент now (секунды). True — звук засчитан как сыгранный;
    /// false — с прошлого раза прошло меньше minInterval. minInterval ≤ 0 — без ограничения.
    /// </summary>
    public bool TryPlay(int key, double now, float minInterval)
    {
        if (minInterval > 0f
            && _lastPlayed.TryGetValue(key, out double last)
            && now - last < minInterval)
            return false;

        _lastPlayed[key] = now;
        return true;
    }

    /// <summary>Забыть все отметки времени (например, после смены сцены).</summary>
    public void Clear() => _lastPlayed.Clear();
}
