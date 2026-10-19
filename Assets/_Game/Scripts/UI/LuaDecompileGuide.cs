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
        /// <summary>Что готовая команда делала внутри.</summary>
        public readonly string Explanation;
        /// <summary>То же на базовых командах (Lua), подсвечивается; по нему считается цена.</summary>
        public readonly string Example;

        public Entry(string readyMade, string[] basics, string explanation, string example)
        {
            ReadyMade = readyMade;
            Basics = basics;
            Explanation = explanation;
            Example = example;
        }

        public bool IsValid => !string.IsNullOrEmpty(ReadyMade);

        public bool Contains(string command) => Basics != null && Array.IndexOf(Basics, command) >= 0;
    }

    private static readonly Entry[] Entries =
    {
        new Entry(
            "obtain",
            new[] { "findObjects", "moveTo", "harvest" },
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
            "autoExplore() just picked a random direction and walked 5 tiles (move) — even toward water " +
            "or back where you have already been. With move you choose the direction and the distance yourself, " +
            "and check lets you look at a tile before you go there.",
            "-- look one tile ahead, walk 5 tiles if it is land\n" +
            "local tile = check(\"east\")\n" +
            "if tile and tile.walkable then\n" +
            "  move(\"east\", 5)\n" +
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
