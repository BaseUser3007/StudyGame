using System;
using Lua;
using Lua.Runtime;

/// <summary>
/// Где сейчас выполняется код игрока: окно и строка — для подсветки выполняемой строки в редакторе.
/// Опрашивается из C#-функций, которые вызывает Lua (команды и списания <c>__energy</c>), поэтому
/// строка обновляется на каждом вызове команды, проверке условия и итерации цикла.
/// Строка берётся из вызывающего Lua-кадра без аллокаций: <c>LineInfo[CallerInstructionIndex]</c>.
/// </summary>
public class LuaLineTracker
{
    /// <summary>Окно (заголовок; главное — <see cref="MainChunkName"/>) и строка в нём; (null, 0) — код не выполняется.</summary>
    public event Action<string, int> OnLineChanged;

    // Код вспомогательных окон оборачивается в `function Title(...)` отдельной первой строкой
    private const int AuxiliaryWrapperLines = 1;

    private readonly string _mainChunkName;

    public string Chunk { get; private set; }
    public int Line { get; private set; }

    public LuaLineTracker(string mainChunkName)
    {
        _mainChunkName = mainChunkName;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Вызывать из C#-функции, вызванной Lua: определяет строку вызова в коде игрока.
    /// Возвращает true, если выполнение перешло на другую строку.
    /// </summary>
    public bool Probe(LuaState state)
    {
        if (state == null) return false;

        ReadOnlySpan<CallStackFrame> frames = state.GetCallStackFrames();
        if (frames.Length < 2) return false;

        // Последний кадр — сама C#-функция. Ближайший Lua-кадр под ней — код игрока; между ними могут быть
        // другие C#-функции (pcall(harvest, …)). Номер инструкции вызова хранит кадр сразу над Lua-кадром
        int callerIndex = frames.Length - 2;
        while (callerIndex >= 0 && !(frames[callerIndex].Function is LuaClosure))
            callerIndex--;
        if (callerIndex < 0) return false;

        var caller = (LuaClosure)frames[callerIndex].Function;
        ReadOnlySpan<int> lineInfo = caller.Proto.LineInfo;
        int pc = frames[callerIndex + 1].CallerInstructionIndex;
        if (pc < 0 || pc >= lineInfo.Length) return false;

        string chunk = caller.Proto.ChunkName;
        int line = lineInfo[pc];
        if (chunk != _mainChunkName) line -= AuxiliaryWrapperLines;
        if (line < 1) return false;

        return Set(chunk, line);
    }

    /// <summary>Код не выполняется — подсветку снять.</summary>
    public void Clear()
    {
        Set(null, 0);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool Set(string chunk, int line)
    {
        if (line == Line && chunk == Chunk) return false;
        Chunk = chunk;
        Line = line;
        OnLineChanged?.Invoke(chunk, line);
        return true;
    }
}
