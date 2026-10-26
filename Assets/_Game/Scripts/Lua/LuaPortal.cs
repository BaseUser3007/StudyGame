using Lua;
using UnityEngine;

/// <summary>
/// Портал забега для Lua-команд: имя типа, таблица-элемент findObjects, видимость под туманом,
/// точка подхода для moveTo(portal) и отказы harvest/mine/attack («portal can't be harvested — use enterPortal()»).
/// Портал один на забег (в лагере его нет — null), поэтому id у него постоянный — <see cref="PortalId"/>.
/// </summary>
public static class LuaPortal
{
    /// <summary>Тип и kind портала в Lua: findObjects("portal"), check(x, y).type.</summary>
    public const string TypeName = "portal";

    /// <summary>
    /// id портала в Lua. Числа Lua — double, поэтому id может быть больше int: 3·10⁹ не пересекается
    /// ни с объектами (GetInstanceID), ни с врагами (1·10⁹ + Id), ни с головоломками (2·10⁹ + Id).
    /// </summary>
    public const double PortalId = 3000000000d;

    // ─── Видимость ────────────────────────────────────────────

    /// <summary>Хотя бы один тайл портала сейчас без тумана (fog = null — виден).</summary>
    public static bool IsVisible(RunPortal portal, IFogProvider fog)
    {
        return AnyTile(portal, fog, explored: false);
    }

    /// <summary>
    /// Портал хоть раз был в поле зрения: он не двигается, поэтому findObjects("portal") находит его
    /// издалека — чтобы код мог вернуться к нему (moveTo(p)).
    /// </summary>
    public static bool IsExplored(RunPortal portal, IFogProvider fog)
    {
        return AnyTile(portal, fog, explored: true);
    }

    private static bool AnyTile(RunPortal portal, IFogProvider fog, bool explored)
    {
        if (portal == null) return false;
        if (fog == null) return true;

        RectInt area = portal.TileArea;
        for (int x = area.xMin; x < area.xMax; x++)
            for (int y = area.yMin; y < area.yMax; y++)
                if (explored ? fog.IsExplored(x, y) : fog.IsVisible(x, y))
                    return true;
        return false;
    }

    // ─── Таблицы ──────────────────────────────────────────────

    /// <summary>Таблица — портал из findObjects/check (kind = "portal").</summary>
    public static bool IsPortalTable(LuaTable table)
    {
        return table != null && table["kind"].TryRead(out string kind) && kind == TypeName;
    }

    /// <summary>
    /// moveTo(p) с таблицей портала: точка подхода (approachX, approachY) вместо центра — портал сплошной,
    /// и путь в центр упирался бы в коллайдер до stuckTimeout. false — аргумент не портал или без полей подхода.
    /// </summary>
    public static bool TryReadApproach(LuaFunctionExecutionContext context, out Vector2 point)
    {
        point = default;
        if (context.ArgumentCount == 0 || !context.GetArgument(0).TryRead(out LuaTable table)) return false;
        if (!IsPortalTable(table)) return false;
        if (!table["approachX"].TryRead(out double x) || !table["approachY"].TryRead(out double y)) return false;

        point = new Vector2((float)x, (float)y);
        return true;
    }

    // ─── Ошибки ───────────────────────────────────────────────

    /// <summary>
    /// Для harvest/mine/attack: цель — портал (его таблица или точка на нём)? Возвращает причину или null.
    /// Таблицу проверяет всегда, точку — только когда checkPoint (в ней нет ресурса/врага — это решает вызывающий).
    /// action — "harvested" / "mined" / "attacked".
    /// </summary>
    public static string GetNotTargetError(
        LuaFunctionExecutionContext context, Vector2 point, RunPortal portal, string action, bool checkPoint)
    {
        if (context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out LuaTable table) && IsPortalTable(table))
            return Explain(action);

        if (!checkPoint || portal == null) return null;
        return portal.ContainsTile(MapPosition.ToTile(point)) ? Explain(action) : null;
    }

    private static string Explain(string action) => $"{TypeName} can't be {action} — use enterPortal()";
}
