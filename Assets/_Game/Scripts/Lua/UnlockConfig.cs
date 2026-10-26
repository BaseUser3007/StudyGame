using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Порог события на карте: событие (тип головоломки, <see cref="PuzzleTypes"/>) открывается, когда выполнено
/// не меньше achievementsRequired достижений; вместе с ним открываются его команды.
/// </summary>
[Serializable]
public class MapEventThreshold
{
    [Tooltip("Id события на карте (PuzzleTypes: code_task, maze, plates)")]
    public string eventId;

    [Tooltip("Сколько достижений нужно выполнить (любых из каталога)")]
    [Min(0)] public int achievementsRequired;

    [Tooltip("Lua-команды, которые открываются вместе с событием (ILuaCommand.Name)")]
    public List<string> commands = new List<string>();

    public string EventId => eventId != null ? eventId.Trim() : string.Empty;
}

/// <summary>
/// Стартовый набор открытий: команды и конструкции языка, доступные без достижений, и пороги событий
/// на карте по числу выполненных достижений (<see cref="MapEventProgress"/>).
/// Всё остальное открывается мета-прогрессом (<see cref="UnlockRegistry"/>, сохраняется в MetaProgress).
/// </summary>
[CreateAssetMenu(fileName = "UnlockConfig", menuName = "CodeRogue/Lua/UnlockConfig")]
public class UnlockConfig : ScriptableObject
{
    [Tooltip("Имена Lua-команд (ILuaCommand.Name), открытых с самого начала")]
    public List<string> startingCommands = new List<string>
    {
        "obtain", "autoExplore", "craft", "build", "drop", "getInventory", "getStatus", "getPosition", "eat"
    };

    [Tooltip("Id конструкций языка (LuaConstructs: local, if, for, while, repeat, function, classes), открытых с начала")]
    public List<string> startingConstructs = new List<string>
    {
        LuaConstructs.Local, LuaConstructs.If
    };

    [Tooltip("События на карте по числу выполненных достижений (+ их команды). Пусто — события открываются " +
             "только наградами достижений (или unlockAll)")]
    public List<MapEventThreshold> mapEventThresholds = new List<MapEventThreshold>();

    [Tooltip("Отладка: открыть все команды и конструкции, не трогая сохранение")]
    public bool unlockAll;
}
