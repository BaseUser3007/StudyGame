using System.Collections.Generic;
using System.Threading.Tasks;
using Lua;
using Lua.Standard;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Общие заготовки для EditMode-тестов: предметы, Lua с учётом энергии как в LuaSandbox, ассеты проекта.
/// </summary>
public static class TestHelpers
{
    // ─── Предметы ─────────────────────────────────────────────

    public static T Item<T>(string id, int maxStack = 20) where T : ResourceItem
    {
        T item = ScriptableObject.CreateInstance<T>();
        item.name = id;
        item.itemId = id;
        item.displayName = id;
        item.maxStackSize = maxStack;
        return item;
    }

    public static MaterialResource Material(string id, int maxStack = 20) => Item<MaterialResource>(id, maxStack);

    public static ToolItem Tool(string id, ToolType type, int maxDurability = 3, int wear = 1)
    {
        ToolItem tool = Item<ToolItem>(id, 1);
        tool.toolType = type;
        tool.maxDurability = maxDurability;
        tool.wearPerUse = wear;
        return tool;
    }

    // ─── Lua ──────────────────────────────────────────────────

    /// <summary>
    /// Lua-состояние с функцией списания энергии, как в LuaSandbox.CreateLuaState.
    /// </summary>
    public sealed class LuaHost
    {
        public readonly LuaState State;
        public readonly PlayerEnergy Energy;
        public readonly EnergyMeter Meter;
        public readonly EnergyInstrumenter Instrumenter;

        public LuaHost(EnergyConfig config, float energy)
        {
            Energy = new PlayerEnergy(energy);
            Meter = new EnergyMeter(Energy);
            Instrumenter = new EnergyInstrumenter(config);

            State = LuaState.Create();
            State.OpenStandardLibraries();
            State.Environment[EnergyInstrumenter.ChargeFunction] = new LuaFunction((context, ct) =>
            {
                double amount = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out double n) ? n : 0d;
                Meter.Charge((float)amount, "code");
                context.Return(true);
                return new ValueTask<int>(1);
            });
        }

        public float Spent => Energy.Max - Energy.Current;

        /// <summary>
        /// Выполняет код, размеченный списаниями энергии, и возвращает первое значение return.
        /// </summary>
        public LuaValue Run(string code)
        {
            LuaValue[] result = State.DoStringAsync(Instrumenter.Instrument(code), "Test")
                .AsTask().GetAwaiter().GetResult();
            return result.Length > 0 ? result[0] : LuaValue.Nil;
        }
    }

    public static EnergyConfig ProjectEnergyConfig()
        => Load<EnergyConfig>("Assets/_Game/ScriptableObjects/DefaultEnergyConfig.asset");

    public static double Number(LuaValue value)
    {
        Assert(value.TryRead(out double number), $"expected a number, got {value.Type}");
        return number;
    }

    // ─── Ассеты ───────────────────────────────────────────────

    public static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert(asset != null, $"asset not found: {path}");
        return asset;
    }

    public static List<T> LoadAll<T>(string folder) where T : Object
    {
        var assets = new List<T>();
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
            assets.Add(AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)));
        return assets;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) NUnit.Framework.Assert.Fail(message);
    }
}
