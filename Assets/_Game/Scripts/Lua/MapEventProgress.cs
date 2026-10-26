using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// События на карте по числу выполненных достижений (пороги — <see cref="UnlockConfig.mapEventThresholds"/>).
/// Чистая логика без состояния: какие события пора открыть и какое следующее. Открывает их
/// <see cref="UnlockRegistry.UnlockMapEventsForAchievements"/>, вызывает AchievementTracker.
/// </summary>
public static class MapEventProgress
{
    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Пороги по возрастанию числа достижений: без пустых строк и пустых id, у повторного id
    /// остаётся меньший порог (при равных — первый в списке). null → пустой список.
    /// </summary>
    public static List<MapEventThreshold> Normalize(IEnumerable<MapEventThreshold> thresholds)
    {
        var result = new List<MapEventThreshold>();
        if (thresholds == null) return result;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        // OrderBy устойчив: при равных порогах сохраняется порядок конфига
        foreach (MapEventThreshold threshold in thresholds
                     .Where(t => t != null && !string.IsNullOrEmpty(t.EventId))
                     .OrderBy(t => Math.Max(0, t.achievementsRequired)))
        {
            if (ids.Add(threshold.EventId))
                result.Add(threshold);
        }
        return result;
    }

    /// <summary>События, порог которых достигнут (completed ≥ achievementsRequired), по возрастанию порога.</summary>
    public static List<MapEventThreshold> GetDue(IEnumerable<MapEventThreshold> thresholds, int completed)
    {
        return Normalize(thresholds).Where(t => completed >= t.achievementsRequired).ToList();
    }

    /// <summary>Ближайшее ещё не достигнутое событие; null — все пороги достигнуты (или порогов нет).</summary>
    public static MapEventThreshold GetNext(IEnumerable<MapEventThreshold> thresholds, int completed)
    {
        return Normalize(thresholds).FirstOrDefault(t => completed < t.achievementsRequired);
    }
}
