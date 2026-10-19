using System;
using System.Collections.Generic;

/// <summary>
/// Тексты окна «New command: X» по схеме Why → What → Example → Energy (GameDesign §6.2.4–6.2.5):
/// какую боль лечит команда, что она делает, пример и заметка о цене. Ключ — ILuaCommand.Name.
/// Тексты — английский исходник, русский — в LocRuUi (Loc.T в UnlockPopup). Цена за вызов сюда не входит — она берётся из
/// ILuaCommand.EnergyCost. Для команд без записи UnlockPopup показывает ILuaCommand.Description.
/// </summary>
public static class LuaCommandGuide
{
    public readonly struct Entry
    {
        /// <summary>Why: что игрок только что сделал и чем это было плохо.</summary>
        public readonly string Pain;
        /// <summary>What: что делает команда.</summary>
        public readonly string Explanation;
        /// <summary>Пример кода (Lua) или null — тогда показывается только сигнатура.</summary>
        public readonly string Example;
        /// <summary>Статичная заметка к цене (сравнение, подводные камни).</summary>
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
        ["mine"] = new Entry(
            "obtain() and harvest() only pick things up from the ground. A tree has to be chopped first — " +
            "and now you have an axe.",
            "mine(obj) chops or breaks an object with the right tool (tree — axe, boulder — pickaxe). " +
            "The loot falls on the ground around it: pick it up with harvest().",
            null,
            "1 per call plus the loot: a tree costs 1 + 12. The axe wears out after 20 uses."),

        ["print"] = new Entry(
            "Your code stopped with an error. The quickest way to find out why is to look at what your code " +
            "actually sees.",
            "print(...) writes values to the log under the main window: numbers, text, nil, and tables with their fields.",
            "local stick = findObjects(\"stick\"):getNear()\n" +
            "print(stick)               -- {type=\"stick\", x=3.4, y=-1.2, ...} or nil\n" +
            "print(getStatus().spent)   -- energy spent in this run so far",
            "0.5 per call. Remove debug prints once the code works — inside a loop they are paid on every pass."),

        ["attack"] = new Entry(
            "Goblins stand between you and the gold. You can sneak around them — or fight.",
            "attack(enemy) hits an enemy within 1.5 tiles: 5 damage bare-handed, 15 with a sword. " +
            "It does not walk up to the enemy — use moveTo first, or wait for it to come.",
            null,
            "2 per hit. The real price is health: fight with a sword.")
    };

    // ─── Public API ───────────────────────────────────────────

    public static bool TryGet(string commandName, out Entry entry)
    {
        if (commandName != null && Entries.TryGetValue(commandName, out entry)) return true;
        entry = default;
        return false;
    }
}
