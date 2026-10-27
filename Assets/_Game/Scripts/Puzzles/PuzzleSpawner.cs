using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// События карты посреди забега (T24): когда достижение открывает тип головоломки (UnlockRegistry.OnUnlocked,
/// UnlockKind.MapEvent — сразу, даже во время выполнения кода), строит ждущие места этого типа из
/// <see cref="PuzzlePlan"/>. Место строится, если вся его область с полем ни разу не была открыта и в ней нет
/// врагов; иначе ищется новое по правилам старта, но только на ни разу не открытых тайлах; не нашлось —
/// предупреждение. После постройки — Physics2D.SyncTransforms и уведомление игроку.
/// Plain-класс, создаётся в Level после PuzzleManager (только в забеге), Dispose — в Level.OnDestroy.
/// </summary>
public sealed class PuzzleSpawner : IDisposable
{
    /// <summary>Головоломка построена посреди забега (уже зарегистрирована в PuzzleManager).</summary>
    public event Action<PuzzleStructure> OnSpawned;

    private readonly PuzzlePlan _plan;
    private readonly UnlockRegistry _unlocks;
    private readonly EnemyManager _enemies;
    private readonly Func<RectInt, bool> _hasEnemies;
    private bool _disposed;

    /// <param name="unlocks">Источник открытий; null — подписки нет (тесты вызывают SpawnType).</param>
    /// <param name="enemies">Враги для проверки области; null — без проверки.</param>
    public PuzzleSpawner(PuzzlePlan plan, UnlockRegistry unlocks, EnemyManager enemies)
    {
        _plan = plan;
        _unlocks = unlocks;
        _enemies = enemies;
        _hasEnemies = HasEnemies;
        if (_unlocks != null) _unlocks.OnUnlocked += HandleUnlocked;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_unlocks != null) _unlocks.OnUnlocked -= HandleUnlocked;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Строит ждущие места типа. Возвращает построенные головоломки (пусто — нечего или негде).
    /// </summary>
    public List<PuzzleStructure> SpawnType(string typeId)
    {
        var built = new List<PuzzleStructure>();
        if (_plan == null || string.IsNullOrEmpty(typeId)) return built;

        foreach (PuzzleSlot reserved in _plan.GetPending(typeId))
        {
            PuzzleSlot slot = reserved;
            if (!_plan.IsStillHidden(slot, _hasEnemies))
            {
                slot = _plan.Relocate(slot, _hasEnemies);
                if (slot == null)
                {
                    Debug.LogWarning($"PuzzleSpawner: no unexplored place for '{typeId}' (id {reserved.Id})");
                    continue;
                }
            }

            PuzzleStructure puzzle = _plan.Build(slot);
            if (puzzle == null)
            {
                Debug.LogWarning($"PuzzleSpawner: could not build '{typeId}' (id {slot.Id})");
                continue;
            }
            built.Add(puzzle);
        }

        if (built.Count == 0) return built;

        // Стены и обелиски должны сразу участвовать в проверках движения
        Physics2D.SyncTransforms();
        UIEventBus.PublishNotification(
            Loc.F("Something appeared on an unexplored part of the island: {0}", PuzzleTypes.GetDisplayName(typeId)),
            LogEntryType.Info);

        foreach (PuzzleStructure puzzle in built)
            OnSpawned?.Invoke(puzzle);
        return built;
    }

    // ─── Handlers ─────────────────────────────────────────────

    private void HandleUnlocked(UnlockKind kind, string id)
    {
        if (_disposed || kind != UnlockKind.MapEvent) return;

        // Вызов идёт из AchievementTracker.Complete: исключение здесь сорвало бы открытие команд события,
        // сохранение и тост достижения — ошибку постройки только логируем
        try
        {
            SpawnType(id);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>В области (тайлы с полем) стоит живой враг.</summary>
    private bool HasEnemies(RectInt area)
    {
        if (_enemies == null) return false;

        // Центр области в координатах карты (целые — центры тайлов) и радиус, накрывающий её целиком
        Vector2 center = new Vector2((area.xMin + area.xMax - 1) * 0.5f, (area.yMin + area.yMax - 1) * 0.5f);
        float radius = Mathf.Sqrt(area.width * area.width + area.height * area.height) * 0.5f + 1f;
        foreach (Enemy enemy in _enemies.GetEnemiesInRadius(center, radius))
            if (area.Contains(enemy.Tile))
                return true;
        return false;
    }
}
