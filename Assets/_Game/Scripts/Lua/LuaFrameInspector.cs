using System;
using System.Collections.Generic;
using Lua;
using Lua.CodeAnalysis;
using Lua.Runtime;

/// <summary>Переменная, видимая на точке останова: имя и значение так, как его выводит print (строки в кавычках).</summary>
public readonly struct LuaVariable
{
    public readonly string Name;
    public readonly string Value;
    public readonly bool IsGlobal;

    public LuaVariable(string name, string value, bool isGlobal)
    {
        Name = name;
        Value = value;
        IsGlobal = isGlobal;
    }
}

/// <summary>
/// Чтение переменных кода игрока на паузе: локальные переменные Lua-кадра, вызвавшего текущую C#-функцию,
/// и глобальные переменные, которые завёл сам игрок (не из стартового окружения и не функции).
/// </summary>
public static class LuaFrameInspector
{
    /// <summary>
    /// Активные локальные переменные ближайшего Lua-кадра под текущей C#-функцией (в порядке объявления).
    /// Служебные переменные цикла вида «(for index)» пропускаются.
    /// </summary>
    public static List<LuaVariable> ReadLocals(LuaState state)
    {
        var result = new List<LuaVariable>();
        if (state == null) return result;

        ReadOnlySpan<CallStackFrame> frames = state.GetCallStackFrames();
        int callerIndex = frames.Length - 2;
        while (callerIndex >= 0 && !(frames[callerIndex].Function is LuaClosure))
            callerIndex--;
        if (callerIndex < 0) return result;

        CallStackFrame frame = frames[callerIndex];
        var closure = (LuaClosure)frame.Function;
        int pc = frames[callerIndex + 1].CallerInstructionIndex;
        ReadOnlySpan<LuaValue> stack = state.GetStackValues();

        // Как luaF_getlocalname: n-я активная в точке pc переменная лежит в регистре n кадра
        int register = 0;
        foreach (LocalVariable local in closure.Proto.LocalVariables)
        {
            if (local.StartPc > pc || pc >= local.EndPc) continue;

            int index = frame.Base + register;
            register++;
            if (string.IsNullOrEmpty(local.Name) || local.Name[0] == '(') continue;
            if (index < 0 || index >= stack.Length) continue;

            result.Add(new LuaVariable(local.Name, PrintCommand.FormatValue(stack[index]), false));
        }

        return result;
    }

    /// <summary>
    /// Глобальные переменные игрока: ключи окружения, которых не было в стартовом наборе, кроме функций
    /// (функции — это окна-«классы» и функции игрока, их значение ничего не говорит). По имени.
    /// </summary>
    public static List<LuaVariable> ReadGlobals(LuaTable environment, ISet<string> baseline)
    {
        var result = new List<LuaVariable>();
        if (environment == null) return result;

        foreach (KeyValuePair<LuaValue, LuaValue> pair in environment)
        {
            if (!pair.Key.TryRead(out string name)) continue;
            if (baseline != null && baseline.Contains(name)) continue;
            if (pair.Value.Type == LuaValueType.Function || pair.Value.Type == LuaValueType.Nil) continue;

            result.Add(new LuaVariable(name, PrintCommand.FormatValue(pair.Value), true));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    /// <summary>Имена в окружении сейчас — стартовый набор, который не показывается как переменные игрока.</summary>
    public static HashSet<string> SnapshotNames(LuaTable environment)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (environment == null) return names;
        foreach (KeyValuePair<LuaValue, LuaValue> pair in environment)
            if (pair.Key.TryRead(out string name))
                names.Add(name);
        return names;
    }
}
