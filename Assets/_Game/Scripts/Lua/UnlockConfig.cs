using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стартовый набор открытий: команды и конструкции языка, доступные без достижений.
/// Всё остальное открывается мета-прогрессом (<see cref="UnlockRegistry"/>, сохраняется в MetaProgress).
/// </summary>
[CreateAssetMenu(fileName = "UnlockConfig", menuName = "CodeRogue/Lua/UnlockConfig")]
public class UnlockConfig : ScriptableObject
{
    [Tooltip("Имена Lua-команд (ILuaCommand.Name), открытых с самого начала")]
    public List<string> startingCommands = new List<string>
    {
        "obtain", "autoExplore", "craft", "build", "drop", "getInventory", "getStatus", "getPosition"
    };

    [Tooltip("Id конструкций языка (LuaConstructs: local, if, for, while, repeat, function, classes), открытых с начала")]
    public List<string> startingConstructs = new List<string>
    {
        LuaConstructs.Local, LuaConstructs.If
    };

    [Tooltip("Отладка: открыть все команды и конструкции, не трогая сохранение")]
    public bool unlockAll;
}
