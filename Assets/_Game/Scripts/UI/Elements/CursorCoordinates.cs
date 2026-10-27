using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Координаты тайла под курсором мыши («x, y») рядом с курсором — те же числа, что принимают
/// moveTo(x, y) / check(x, y). Только в забеге; скрыта над интерфейсом (IPointerOverUIProvider) и за картой.
/// Курсор опрашивается планировщиком UI Toolkit, без MonoBehaviour и подписок на шины.
/// </summary>
public class CursorCoordinates : Label
{
    // ─── Константы ────────────────────────────────────────────

    private const long RefreshMs = 33;
    // Смещение подписи от острия курсора (пиксели панели), чтобы курсор её не закрывал
    private static readonly Vector2 CursorOffset = new Vector2(16f, 18f);
    private const float EdgeMargin = 4f;

    private readonly IMapDataProvider _map;
    private readonly Camera _camera;
    private readonly IPointerOverUIProvider _uiPointer;

    private Vector2Int _shownTile = new Vector2Int(int.MinValue, int.MinValue);

    public CursorCoordinates(IMapDataProvider map, Camera camera, IPointerOverUIProvider uiPointer)
    {
        Debug.Assert(map != null, "CursorCoordinates: map is not assigned");
        Debug.Assert(camera != null, "CursorCoordinates: camera is not assigned");

        _map = map;
        _camera = camera;
        _uiPointer = uiPointer;

        name = "cursor-coordinates";
        pickingMode = PickingMode.Ignore;
        focusable = false;

        style.position = Position.Absolute;
        style.paddingLeft = 5;
        style.paddingRight = 5;
        style.paddingTop = 2;
        style.paddingBottom = 2;
        style.fontSize = 13;
        style.color = new Color(1f, 1f, 1f, 0.95f);
        style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.78f);
        style.borderTopLeftRadius = 3;
        style.borderTopRightRadius = 3;
        style.borderBottomLeftRadius = 3;
        style.borderBottomRightRadius = 3;
        style.unityTextAlign = TextAnchor.MiddleLeft;
        style.display = DisplayStyle.None;

        schedule.Execute(Refresh).Every(RefreshMs);
    }

    // ─── Update ───────────────────────────────────────────────

    private void Refresh()
    {
        if (panel == null || !TryGetTile(out Vector2Int tile, out Vector2 screen))
        {
            style.display = DisplayStyle.None;
            return;
        }

        if (tile != _shownTile)
        {
            _shownTile = tile;
            text = System.FormattableString.Invariant($"{tile.x}, {tile.y}");
        }

        style.display = DisplayStyle.Flex;

        // Input System считает Y снизу, панель — сверху
        Vector2 cursor = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        Vector2 position = cursor + CursorOffset;

        // У правого и нижнего края — по другую сторону курсора, чтобы подпись не уходила за экран
        Rect bounds = panel.visualTree.layout;
        float width = resolvedStyle.width;
        float height = resolvedStyle.height;
        if (!float.IsNaN(width) && position.x + width > bounds.width - EdgeMargin)
            position.x = cursor.x - CursorOffset.x - width;
        if (!float.IsNaN(height) && position.y + height > bounds.height - EdgeMargin)
            position.y = cursor.y - CursorOffset.y - height;

        style.left = position.x;
        style.top = position.y;
    }

    /// <summary>Тайл карты под курсором; false — нет мыши, курсор над интерфейсом или за картой.</summary>
    private bool TryGetTile(out Vector2Int tile, out Vector2 screen)
    {
        tile = default;
        screen = default;

        Mouse mouse = Mouse.current;
        if (mouse == null || _camera == null) return false;

        screen = mouse.position.ReadValue();
        if (screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return false;
        if (_uiPointer != null && _uiPointer.IsPointerOverUI(screen)) return false;

        Vector3 world = _camera.ScreenToWorldPoint(
            new Vector3(screen.x, screen.y, -_camera.transform.position.z));
        tile = _map.WorldToTileIndex(world);
        return _map.IsInBounds(tile.x, tile.y);
    }
}
