using System;
using System.Collections.Generic;

/// <summary>
/// «Декомпиляция» (метафора, GameDesign §5): готовая команда раскладывается на базовые, которые игрок
/// получает наградой. Для окна «Decompiled: X()» здесь — из каких базовых команд состояла готовая,
/// объяснение и пример того же на базовых командах. Тексты на английском (UI игры английский).
/// Цены сюда не входят: UnlockPopup считает их из реестра команд (ILuaCommand.EnergyCost),
/// LuaObjectList.MethodCost и EnergyConfig по токенам примера — как EnergyCounter, за один проход.
/// Тексты должны соответствовать реальным ObtainCommand / AutoExploreCommand.
/// </summary>
public static class LuaDecompileGuide
{
    public readonly struct Entry
    {
        /// <summary>Имя готовой команды (ILuaCommand.Name).</summary>
        public readonly string ReadyMade;
        /// <summary>Базовые команды, на которые она раскладывается (в порядке показа).</summary>
        public readonly string[] Basics;
        /// <summary>Why: сколько игрок переплатил за готовую команду (GameDesign §6.2.5).</summary>
        public readonly string Pain;
        /// <summary>Что готовая команда делала внутри.</summary>
        public readonly string Explanation;
        /// <summary>То же на базовых командах (Lua), подсвечивается; по нему считается цена.</summary>
        public readonly string Example;
        /// <summary>
        /// Заметка к сравнению цены (null — только строки «X() / Your code», их UnlockPopup считает сам).
        /// </summary>
        public readonly string EnergyNote;

        public Entry(string readyMade, string[] basics, string pain, string explanation, string example,
            string energyNote = null)
        {
            ReadyMade = readyMade;
            Basics = basics;
            Pain = pain;
            Explanation = explanation;
            Example = example;
            EnergyNote = energyNote;
        }

        public bool IsValid => !string.IsNullOrEmpty(ReadyMade);

        public bool Contains(string command) => Basics != null && Array.IndexOf(Basics, command) >= 0;
    }

    private static readonly Entry[] Entries =
    {
        new Entry(
            "obtain",
            new[] { "findObjects", "moveTo", "harvest" },
            "Every obtain() cost you 8 energy — three of them, 24.",
            "obtain() was never magic. Inside, it looked for the nearest visible object it could pick up " +
            "(findObjects + getNear), walked up to it (moveTo) and picked it up (harvest). " +
            "Now you can write the same steps yourself and pay only for the ones you need.",
            "-- obtain(\"stick\"), written by hand\n" +
            "local stick = findObjects(\"stick\"):getNear()\n" +
            "if stick then\n" +
            "  moveTo(stick.x, stick.y)\n" +
            "  harvest(stick)\n" +
            "end"),

        new Entry(
            "autoExplore",
            new[] { "move", "check" },
            "Every autoExplore() cost you 10 energy, and it only walks a few tiles.",
            "autoExplore() was a walk with a good memory. It compared the 8 directions, picked the one where " +
            "6 tiles of walking (move) would reveal the most land you had never seen, and stopped at water; " +
            "when everything around was already explored, it headed for the nearest edge of the fog. " +
            "check only sees tiles that are in sight, so your code can't count hidden land — but it doesn't need to: " +
            "keep walking one way while check shows land ahead, and turn when it shows water. " +
            "Every step goes into new ground, and a check plus a move costs a fraction of autoExplore().",
            "-- go east while there is land ahead, otherwise turn north\n" +
            "local ahead = check(\"east\", 2)\n" +
            "if ahead and ahead.walkable then\n" +
            "  move(\"east\", 6)\n" +
            "else\n" +
            "  move(\"north\", 6)\n" +
            "end")
    };

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Декомпиляция, в которую входит базовая команда.</summary>
    public static bool TryGetByBasic(string command, out Entry entry)
    {
        if (!string.IsNullOrEmpty(command))
        {
            foreach (Entry candidate in Entries)
            {
                if (!candidate.Contains(command)) continue;
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>Декомпиляция готовой команды (например, для справки).</summary>
    public static bool TryGetByReadyMade(string command, out Entry entry)
    {
        foreach (Entry candidate in Entries)
        {
            if (candidate.ReadyMade != command) continue;
            entry = candidate;
            return true;
        }

        entry = default;
        return false;
    }
}
