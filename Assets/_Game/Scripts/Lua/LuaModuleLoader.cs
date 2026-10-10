using Cysharp.Threading.Tasks;
using Lua;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Загружает Lua модули из окон-классов редактора.
/// Игрок использует require("ClassName") чтобы подключить свой класс.
/// </summary>
public class LuaModuleLoader : ILuaModuleLoader
{
    private readonly Dictionary<string, string> _modules = new Dictionary<string, string>();

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Регистрирует модуль по имени.
    /// Вызывается из CodeEditorManager перед запуском кода.
    /// </summary>
    public void RegisterModule(string name, string code)
    {
        _modules[name] = code;
    }

    /// <summary>
    /// Очищает все зарегистрированные модули.
    /// </summary>
    public void ClearModules()
    {
        _modules.Clear();
    }

    // ─── ILuaModuleLoader ─────────────────────────────────────

    public bool Exists(string moduleName)
    {
        return _modules.ContainsKey(moduleName);
    }

    public async ValueTask<LuaModule> LoadAsync(
        string moduleName,
        CancellationToken cancellationToken = default)
    {
        if (_modules.TryGetValue(moduleName, out string code))
            return new LuaModule(moduleName, code);

        throw new LuaRuntimeException(
            default,
            $"Module '{moduleName}' not found. Make sure the class window exists.");
    }
}