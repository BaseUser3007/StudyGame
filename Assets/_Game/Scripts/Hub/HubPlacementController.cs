using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Установка постройки хаба мышью (этап 4). UI выбирает постройку в панели и вызывает
/// <see cref="Begin"/>; дальше призрак ходит за курсором по сетке (зелёный — можно, красный — нельзя),
/// левый клик над картой — <see cref="HubService.TryBuild"/>, правый клик / Escape — отмена.
/// Над интерфейсом (IPointerOverUIProvider) призрак скрыт и клики игнорируются.
/// Компонент создаёт Level на корне хаба (HubGrid.Root); живёт до перезагрузки сцены.
/// </summary>
public class HubPlacementController : MonoBehaviour
{
    /// <summary>Постройка поставлена (после оплаты и сохранения).</summary>
    public event Action<HubBuildingDefinition, Vector2Int> OnPlaced;
    /// <summary>Размещение отменено (правый клик, Escape, Cancel()).</summary>
    public event Action OnCancelled;
    /// <summary>Клик по клетке не удался: причина (место занято, не хватает материалов…).</summary>
    public event Action<HubBuildResult> OnFailed;

    private HubService _hub;
    private HubGrid _grid;
    private IPointerOverUIProvider _uiPointer;
    private HubBuildingDefinition _selected;

    public bool IsPlacing => _selected != null;
    public HubBuildingDefinition Selected => _selected;

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(HubService hub, HubGrid grid, IPointerOverUIProvider uiPointer)
    {
        _hub = hub;
        _grid = grid;
        _uiPointer = uiPointer;
        ValidateReferences();
    }

    private void ValidateReferences()
    {
        Debug.Assert(_hub != null, "HubPlacementController: HubService is null");
        Debug.Assert(_grid != null, "HubPlacementController: HubGrid is null");
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>
    /// Начинает размещение. False (и уведомление) — если постройку сейчас нельзя выбрать:
    /// не открыта, уже стоит, не хватает материалов (HubService.CheckAvailability).
    /// </summary>
    public bool Begin(HubBuildingDefinition definition)
    {
        HubBuildResult availability = _hub.CheckAvailability(definition);
        if (availability != HubBuildResult.Ok)
        {
            UIEventBus.PublishNotification(HubService.Describe(availability), LogEntryType.Warning);
            OnFailed?.Invoke(availability);
            return false;
        }

        _selected = definition;
        return true;
    }

    public void Cancel()
    {
        if (_selected == null) return;

        _selected = null;
        _grid.HideGhost();
        OnCancelled?.Invoke();
    }

    // ─── Update ───────────────────────────────────────────────

    private void Update()
    {
        if (_selected == null) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame
            || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            Cancel();
            return;
        }

        Vector2 screen = mouse.position.ReadValue();
        if (_uiPointer != null && _uiPointer.IsPointerOverUI(screen))
        {
            _grid.HideGhost();
            return;
        }

        _grid.TryScreenToCell(screen, out Vector2Int cursorCell);
        Vector2Int cell = _grid.GetPlacementCell(_selected, cursorCell);

        // Далеко за сеткой призрак не нужен — курсор ушёл на карту
        if (!IsNearGrid(cursorCell))
        {
            _grid.HideGhost();
            return;
        }

        _grid.ShowGhost(_selected, cell, _hub.CanPlaceAt(_selected, cell));

        if (!mouse.leftButton.wasPressedThisFrame) return;

        HubBuildingDefinition definition = _selected;
        HubBuildResult result = _hub.TryBuild(definition, cell);
        if (result == HubBuildResult.Ok)
        {
            _selected = null;
            _grid.HideGhost();
            OnPlaced?.Invoke(definition, cell);
            return;
        }

        UIEventBus.PublishNotification(HubService.Describe(result), LogEntryType.Warning);
        OnFailed?.Invoke(result);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool IsNearGrid(Vector2Int cell)
    {
        Vector2Int size = _hub.GridSize;
        return cell.x >= -2 && cell.y >= -2 && cell.x < size.x + 2 && cell.y < size.y + 2;
    }
}
