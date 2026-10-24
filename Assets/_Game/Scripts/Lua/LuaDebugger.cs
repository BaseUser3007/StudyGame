using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Lua;

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

    /// <summary>Имена стартового окружения (команды, стандартная библиотека) — не показываются как глобальные игрока.</summary>
    public ISet<string> GlobalsBaseline { get; set; }

    /// <summary>Локальные переменные кадра, где стоит код (снимок на момент паузы; вне паузы — пусто).</summary>
    public IReadOnlyList<LuaVariable> Locals => _locals;

    /// <summary>Глобальные переменные игрока (снимок на момент паузы; вне паузы — пусто).</summary>
    public IReadOnlyList<LuaVariable> Globals => _globals;

    private static readonly List<LuaVariable> Empty = new List<LuaVariable>();
    private List<LuaVariable> _locals = Empty;
    private List<LuaVariable> _globals = Empty;

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
    public async UniTask PauseAsync(LuaState state, string chunk, int line, CancellationToken ct)
    {
        _stepRequested = false;
        // Значения на паузе не меняются — читаем один раз, до события (UI сразу их покажет)
        _locals = LuaFrameInspector.ReadLocals(state);
        _globals = state != null ? LuaFrameInspector.ReadGlobals(state.Environment, GlobalsBaseline) : Empty;
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
            _locals = Empty;
            _globals = Empty;
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
