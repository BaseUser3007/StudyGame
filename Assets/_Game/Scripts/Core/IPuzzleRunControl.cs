using System;

/// <summary>
/// Управление запуском кода для головоломок (T25): PuzzleManager узнаёт, идёт ли запуск, следит за его
/// началом и концом (попытка = следующий запуск) и прерывает запуск, когда игрок встал на стартовую платформу.
/// Реализует песочница Lua (LuaSandbox): прерывание — как enterPortal, итог "puzzle", запуск дня не тратится.
/// </summary>
public interface IPuzzleRunControl
{
    /// <summary>Сейчас выполняется код игрока.</summary>
    bool IsRunning { get; }

    /// <summary>Запуск кода начался (true) / закончился (false) — как LuaSandbox.OnRunningChanged.</summary>
    event Action<bool> OnRunningChanged;

    /// <summary>
    /// Прервать текущий запуск: головоломка начата посреди кода. Запуск заканчивается, как при Stop
    /// (OnRunningChanged(false) — позже, когда песочница остановится).
    /// </summary>
    void InterruptForPuzzle();
}
