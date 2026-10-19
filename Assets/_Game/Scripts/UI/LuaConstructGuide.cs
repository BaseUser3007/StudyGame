using System;
using System.Collections.Generic;

/// <summary>
/// Справочник для окна «Unlocked: X» по схеме Why → What → Example → Energy (GameDesign §6.2.4–6.2.5):
/// какую боль лечит конструкция языка, что она делает, короткий пример с реальными командами игры
/// и статичная заметка о цене. Ключи — id из <see cref="LuaConstructs"/>. Тексты — английский исходник
/// (русский — в LocRuUi, Loc.T в UnlockPopup). Сама цена конструкции сюда не входит — она берётся из EnergyConfig (см. UnlockPopup).
/// </summary>
public static class LuaConstructGuide
{
    public readonly struct Entry
    {
        /// <summary>Why: что игрок только что сделал и чем это было плохо (null — блока нет).</summary>
        public readonly string Pain;
        /// <summary>What: что делает и зачем это игроку.</summary>
        public readonly string Explanation;
        /// <summary>Пример кода (Lua), подсвечивается в окне.</summary>
        public readonly string Example;
        /// <summary>Заметка к цене: сравнение и подводные камни (null — только цена из EnergyConfig).</summary>
        public readonly string EnergyNote;

        public Entry(string pain, string explanation, string example, string energyNote)
        {
            Pain = pain;
            Explanation = explanation;
            Example = example;
            EnergyNote = energyNote;
        }
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal)
    {
        // Стартовая конструкция: окна открытия обычно нет, текст нужен понятию «Variable» туториала
        [LuaConstructs.Local] = new Entry(
            null,
            "A variable keeps a value under a name, so you can use it again instead of asking the world twice. " +
            "Every findObjects() call costs energy — store its result in a local and reuse it.",
            "local trees = findObjects(\"tree\")\n" +
            "local tree = trees:getNear()\n" +
            "moveTo(tree.x, tree.y)\n" +
            "mine(tree)",
            null),

        [LuaConstructs.If] = new Entry(
            "obtain() never complained: when nothing was in sight, it simply did nothing. Your own code is less " +
            "forgiving — findObjects(\"stick\"):getNear() returns nil when there is no stick, and stick.x on nil " +
            "stops the run with an error.",
            "if runs a block only when its condition is true. nil and false count as \"no\", anything else " +
            "as \"yes\". elseif and else add alternatives.",
            "local stick = findObjects(\"stick\"):getNear()\n" +
            "if stick then\n" +
            "  moveTo(stick.x, stick.y)\n" +
            "  harvest(stick)\n" +
            "else\n" +
            "  autoExplore()\n" +
            "end",
            "Each check costs 0.5. An error ends the run and wastes one of today's runs — checking first is cheaper."),

        [LuaConstructs.For] = new Entry(
            "You have called harvest() 6 times — line after line of the same code. One tree drops 4 items, " +
            "and each of them needed its own lines.",
            "for repeats a block a set number of times. Its variable (i) counts the passes: 1, 2, 3… " +
            "Use count to go through a whole list.",
            "local logs = findObjects(\"log\")\n" +
            "for i = 1, logs.count do\n" +
            "  local log = logs[i]\n" +
            "  moveTo(log.x, log.y)\n" +
            "  harvest(log)\n" +
            "end",
            "A loop saves lines, not energy: every pass costs 1 plus everything inside. Search once before " +
            "the loop — a findObjects() inside it is paid on every pass. Here: 1 + 4 per item, instead of 5 " +
            "per item with a new search each time."),

        [LuaConstructs.While] = new Entry(
            "You paid 10 energy for every autoExplore(). Walking yourself is cheaper — but you never know how " +
            "far the land goes, so how many move() lines should you write?",
            "while repeats a block as long as its condition is true. Use it when you don't know the number of " +
            "passes in advance: walk until something stops you, chop while trees are in sight.",
            "-- walk east until water or an obstacle stops you\n" +
            "local steps = move(\"east\", 6)\n" +
            "while steps > 0 do\n" +
            "  steps = move(\"east\", 6)\n" +
            "end",
            "Each check of the condition costs 1: here 2 energy per 6 tiles, against 10 for autoExplore(). " +
            "Make sure the condition becomes false — an endless loop drains the whole day."),

        [LuaConstructs.Repeat] = new Entry(
            "Clean code — 4 logs on a tight budget. But while checks before the first pass, and sometimes you " +
            "have to act first and only then see whether it was enough.",
            "repeat … until runs the body first and checks the condition after it, so the body always runs at " +
            "least once. The loop stops when the until condition becomes true. Locals of the body are visible in until.",
            "-- step north until a tree comes into view (or the way is blocked)\n" +
            "repeat\n" +
            "  local moved = move(\"north\", 3)\n" +
            "  local trees = findObjects(\"tree\")\n" +
            "until trees.count > 0 or moved == 0",
            "Each until check costs 1, like a while check. Here: 3 energy per 3 tiles walked."),

        [LuaConstructs.Function] = new Entry(
            "You have written a for loop in 5 runs — probably with the same \"find, walk, pick up\" lines inside " +
            "every time. A copied block has to be fixed in every copy.",
            "function gives a name to a piece of code; call it by that name like a command. Parameters (kind) " +
            "receive the arguments of the call, and return hands a value back.",
            "function collect(kind)\n" +
            "  local obj = findObjects(kind):getNear()\n" +
            "  if obj then\n" +
            "    moveTo(obj.x, obj.y)\n" +
            "    harvest(obj)\n" +
            "  end\n" +
            "  return obj\n" +
            "end\n" +
            "\n" +
            "collect(\"stick\")\n" +
            "collect(\"stone\")",
            "Defining a function is free; each call costs 0.5 and its body is paid as usual. collect() below " +
            "costs 6 per call — still less than obtain() at 8."),

        [LuaConstructs.Classes] = new Entry(
            "Every new island starts with an empty editor: the functions you wrote last run are gone, and you " +
            "type them again.",
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
            "Gather(\"stone\")",
            "Calling a window costs the same as calling a function (0.5). Saved windows are kept between runs — " +
            "besides materials, your code is the only thing you take home.")
    };

    // ─── Public API ───────────────────────────────────────────

    public static bool TryGet(string constructId, out Entry entry)
    {
        if (constructId != null && Entries.TryGetValue(constructId, out entry)) return true;
        entry = default;
        return false;
    }
}
