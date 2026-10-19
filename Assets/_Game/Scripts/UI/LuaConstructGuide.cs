using System;
using System.Collections.Generic;

/// <summary>
/// Справочник для окна «Unlocked: X»: зачем нужна конструкция языка и короткий пример с реальными
/// командами игры. Ключи — id из <see cref="LuaConstructs"/>. Тексты на английском (UI игры английский).
/// Цена конструкции сюда не входит — она берётся из EnergyConfig (см. UnlockPopup).
/// </summary>
public static class LuaConstructGuide
{
    public readonly struct Entry
    {
        /// <summary>Объяснение: что делает и зачем это игроку.</summary>
        public readonly string Explanation;
        /// <summary>Пример кода (Lua), подсвечивается в окне.</summary>
        public readonly string Example;

        public Entry(string explanation, string example)
        {
            Explanation = explanation;
            Example = example;
        }
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal)
    {
        [LuaConstructs.Local] = new Entry(
            "A variable keeps a value under a name, so you can use it again instead of asking the world twice. " +
            "Every findObjects() call costs energy — store its result in a local and reuse it.",
            "local trees = findObjects(\"tree\")\n" +
            "local tree = trees:getNear()\n" +
            "moveTo(tree.x, tree.y)\n" +
            "mine(tree)"),

        [LuaConstructs.If] = new Entry(
            "A condition runs code only when it makes sense: skip an action that would fail and keep your energy. " +
            "elseif and else add alternatives. Every check of a condition costs energy.",
            "local stick = findObjects(\"stick\"):getNear()\n" +
            "if stick then\n" +
            "  moveTo(stick.x, stick.y)\n" +
            "  harvest(stick)\n" +
            "else\n" +
            "  autoExplore()\n" +
            "end"),

        [LuaConstructs.For] = new Entry(
            "The for loop repeats a block a fixed number of times. It saves lines, not energy: " +
            "every iteration pays for the loop itself and for everything inside it, just like writing the code out.",
            "-- pick up the 3 nearest sticks\n" +
            "for i = 1, 3 do\n" +
            "  local stick = findObjects(\"stick\"):getNear()\n" +
            "  if stick then\n" +
            "    moveTo(stick.x, stick.y)\n" +
            "    harvest(stick)\n" +
            "  end\n" +
            "end"),

        [LuaConstructs.While] = new Entry(
            "The while loop repeats as long as its condition is true — use it when you don't know in advance " +
            "how many times. Each check of the condition costs energy, and a loop that never ends drains " +
            "the whole day, so make sure the condition becomes false.",
            "-- chop trees while any are in sight\n" +
            "local trees = findObjects(\"tree\")\n" +
            "while trees.count > 0 do\n" +
            "  local tree = trees:getNear()\n" +
            "  moveTo(tree.x, tree.y)\n" +
            "  mine(tree)\n" +
            "  trees = findObjects(\"tree\")\n" +
            "end"),

        [LuaConstructs.Repeat] = new Entry(
            "repeat … until is like while, but the condition is checked after the body, so the body " +
            "always runs at least once. The loop stops when the until condition becomes true.",
            "-- explore until a boulder comes into view\n" +
            "repeat\n" +
            "  autoExplore()\n" +
            "until findObjects(\"boulder\").count > 0"),

        [LuaConstructs.Function] = new Entry(
            "A function gives a name to a piece of code, so you can call it from several places instead of " +
            "copying it. Parameters make it work for different cases. Its body is paid on every call.",
            "function collect(kind)\n" +
            "  local obj = findObjects(kind):getNear()\n" +
            "  if obj then\n" +
            "    moveTo(obj.x, obj.y)\n" +
            "    harvest(obj)\n" +
            "  end\n" +
            "end\n" +
            "\n" +
            "collect(\"stick\")\n" +
            "collect(\"stone\")"),

        [LuaConstructs.Classes] = new Entry(
            "Extra code windows: press + on the main window. Each window becomes a function named after its " +
            "title, which Main can call with arguments (read them with ...). Drag a window by its header onto " +
            "the code library tab on the right to save it and reuse it in later runs.",
            "-- window \"Gather\":\n" +
            "local kind = ...\n" +
            "local obj = findObjects(kind):getNear()\n" +
            "if obj then\n" +
            "  moveTo(obj.x, obj.y)\n" +
            "  harvest(obj)\n" +
            "end\n" +
            "\n" +
            "-- window Main:\n" +
            "Gather(\"stick\")\n" +
            "Gather(\"stone\")")
    };

    // ─── Public API ───────────────────────────────────────────

    public static bool TryGet(string constructId, out Entry entry)
    {
        if (constructId != null && Entries.TryGetValue(constructId, out entry)) return true;
        entry = default;
        return false;
    }
}
