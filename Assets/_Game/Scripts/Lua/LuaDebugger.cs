using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Точки останова и пошаговое выполнение кода игрока.
/// Останавливаться можно только там, где код платит энергию (вызов команды, проверка условия, итерация
/// цикла, вызов функции) — это те же точки, где <see cref="LuaLineTracker"/> узнаёт строку. Пауза — ожидание
/// внутри такой C#-функции: главный поток не блокируется, мир (враги) на паузе стоит, энергия не тратится.
/// Точки останова хранит UI (по окну и строке) и отдаёт через <see cref="BreakpointProvider"/>.
/// Точка срабатывает при входе выполнения на строку: цикл или рекурсия, целиком записанные в одну
/// строку, встают только на первом проходе (строка не меняется).
/// </summary>
public class LuaDebugger
{
    /// <summary>Пауза началась (окно, строка) / закончилась (null, 0).</summary>
    public event Action<string, int> OnPausedChanged;

    /// <summary>Есть ли точка останова в окне на строке (окно — заголовок; главное — LuaSandbox.MainChunkName).</summary>
    public Func<string, int, bool> BreakpointProvider { get; set; }

    public bool IsPaused { get; private set; }
    public string PausedChunk { get; private set; }
    public int PausedLine { get; private set; }

    private bool _stepRequested;
    private UniTaskCompletionSource _resume;

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Новый запуск: шаг и пауза прошлого запуска не переносятся.</summary>
    public void Reset()
    {
        _stepRequested = false;
        _resume?.TrySetCanceled();
        _resume = null;
        SetPaused(false, null, 0);
    }

    /// <summary>
    /// Выполнение пришло на новую строку: остановиться ли (точка останова или шаг)?
    /// </summary>
    public bool ShouldPause(string chunk, int line)
    {
        if (line <= 0) return false;
        if (_stepRequested) return true;
        return BreakpointProvider != null && BreakpointProvider(chunk, line);
    }

    /// <summary>Ждать Continue / Step / Stop (отмена ct бросает OperationCanceledException).</summary>
    public async UniTask PauseAsync(string chunk, int line, CancellationToken ct)
    {
        _stepRequested = false;
        var resume = new UniTaskCompletionSource();
        _resume = resume;
        SetPaused(true, chunk, line);
        try
        {
            using (ct.Register(() => resume.TrySetCanceled(ct)))
                await resume.Task;
        }
        finally
        {
            if (_resume == resume) _resume = null;
            SetPaused(false, null, 0);
        }
    }

    /// <summary>Продолжить до следующей точки останова.</summary>
    public void Continue()
    {
        if (!IsPaused) return;
        _stepRequested = false;
        _resume?.TrySetResult();
    }

    /// <summary>Выполнить до следующей строки, где код платит энергию, и снова встать.</summary>
    public void Step()
    {
        // Вне паузы шаг не имеет смысла (кнопки скрыты); иначе следующая строка встала бы неожиданно
        if (!IsPaused) return;
        _stepRequested = true;
        _resume?.TrySetResult();
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SetPaused(bool paused, string chunk, int line)
    {
        if (paused == IsPaused && chunk == PausedChunk && line == PausedLine) return;
        IsPaused = paused;
        PausedChunk = chunk;
        PausedLine = line;
        OnPausedChanged?.Invoke(chunk, line);
    }
}
