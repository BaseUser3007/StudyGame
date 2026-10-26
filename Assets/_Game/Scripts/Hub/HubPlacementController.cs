using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Установка и перенос построек хаба мышью (этап 4). UI выбирает постройку в панели и вызывает
/// <see cref="Begin"/> (постройка) или <see cref="BeginMove"/> (перенос поставленной, бесплатно); дальше призрак
/// ходит за курсором по сетке (зелёный — можно, красный — нельзя), левый клик над картой —
/// <see cref="HubService.TryBuild"/> / <see cref="HubService.TryMove"/>, правый клик / Escape — отмена.
/// Во время переноса вид постройки на старом месте приглушён (HubGrid.SetMovingBuilding), а её старое место
/// не мешает (можно сдвинуть на клетку).
/// Над интерфейсом (IPointerOverUIProvider) призрак скрыт и клики игнорируются.
/// Под призраком считается предпросмотр бонусов за соседство (T15, HubService.PreviewAdjacencyBonuses — для
/// переноса старое место самой постройки не учитывается): соседи пары подсвечиваются через HubGrid.ShowGhost,
/// подписи строит HubBuildPanel по OnPreviewChanged.
/// Клики по стоящим постройкам вне размещения ничего не делают — зарезервировано под будущие меню построек.
/// Компонент создаёт Level на корне хаба (HubGrid.Root); живёт до перезагрузки сцены.
/// </summary>
public class HubPlacementController : MonoBehaviour
{
    /// <summary>Постройка поставлена (после оплаты и сохранения).</summary>
    public event Action<HubBuildingDefinition, Vector2Int> OnPlaced;
    /// <summary>Постройка перенесена (сохранено): новая клетка. Клик по своей же клетке — OnCancelled.</summary>
    public event Action<HubBuildingDefinition, Vector2Int> OnMoved;
    /// <summary>Размещение или перенос отменены (правый клик, Escape, Cancel(), перенос на свою же клетку).</summary>
    public event Action OnCancelled;
    /// <summary>Клик по клетке не удался: причина (место занято, не хватает материалов…).</summary>
    public event Action<HubBuildResult> OnFailed;
    /// <summary>Перенос не удался: причина (вне сетки, место занято…).</summary>
    public event Action<HubMoveResult> OnMoveFailed;
    /// <summary>
    /// Изменился предпросмотр бонусов за соседство под призраком (T15): новая клетка, призрак скрыт
    /// (пустой список) или размещение закончилось. Список — <see cref="Preview"/>.
    /// </summary>
    public event Action<IReadOnlyList<HubAdjacencyMatch>> OnPreviewChanged;

    private static readonly IReadOnlyList<HubAdjacencyMatch> NoPreview = Array.Empty<HubAdjacencyMatch>();

    private HubService _hub;
    private HubGrid _grid;
    private IPointerOverUIProvider _uiPointer;
    private HubBuildingDefinition _selected;
    // true — переносим поставленную _selected, false — строим новую
    private bool _moving;

    // Предпросмотр соседства пересчитывается только при смене клетки призрака
    private IReadOnlyList<HubAdjacencyMatch> _preview = NoPreview;
    private readonly List<RectInt> _previewAreas = new List<RectInt>();
    private Vector2Int? _previewCell;

    /// <summary>Идёт размещение новой постройки или перенос поставленной.</summary>
    public bool IsPlacing => _selected != null;
    /// <summary>Идёт перенос поставленной постройки (<see cref="Selected"/>).</summary>
    public bool IsMoving => _selected != null && _moving;
    public HubBuildingDefinition Selected => _selected;
    /// <summary>Бонусы за соседство, которые появятся в клетке под призраком (пусто — нет/скрыт).</summary>
    public IReadOnlyList<HubAdjacencyMatch> Preview => _preview;
    /// <summary>Клетка (левый нижний угол) призрака, для которой посчитан Preview; null — призрак скрыт.</summary>
    public Vector2Int? PreviewCell => _previewCell;

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
    /// Идущий перенос отменяется без события.
    /// </summary>
    public bool Begin(HubBuildingDefinition definition)
    {
        HubBuildResult availability = _hub.CheckAvailability(definition);
        if (availability != HubBuildResult.Ok)
        {
            UIEventBus.PublishNotification(Loc.T(HubService.Describe(availability)), LogEntryType.Warning);
            OnFailed?.Invoke(availability);
            return false;
        }

        StopCurrent();
        _selected = definition;
        _moving = false;
        return true;
    }

    /// <summary>
    /// Начинает перенос поставленной постройки (любой, включая портал). False (и уведомление) — постройки нет
    /// в каталоге или в хабе. Идущее размещение/перенос другой постройки отменяется без события.
    /// </summary>
    public bool BeginMove(HubBuildingDefinition definition)
    {
        HubMoveResult check = _hub.TryGetCell(definition, out Vector2Int cell)
            ? _hub.CheckMove(definition, cell)
            : HubMoveResult.NotBuilt;
        // На своём месте CheckMove = Ok; Unknown — определение не из каталога
        if (check == HubMoveResult.Unknown || check == HubMoveResult.NotBuilt)
        {
            UIEventBus.PublishNotification(Loc.T(HubService.Describe(check)), LogEntryType.Warning);
            OnMoveFailed?.Invoke(check);
            return false;
        }

        StopCurrent();
        _selected = definition;
        _moving = true;
        _grid.SetMovingBuilding(definition.Id);
        return true;
    }

    public void Cancel()
    {
        if (_selected == null) return;

        StopCurrent();
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
            ClearPreview();
            return;
        }

        _grid.TryScreenToCell(screen, out Vector2Int cursorCell);
        Vector2Int cell = _grid.GetPlacementCell(_selected, cursorCell);

        // Далеко за сеткой призрак не нужен — курсор ушёл на карту
        if (!IsNearGrid(cursorCell))
        {
            _grid.HideGhost();
            ClearPreview();
            return;
        }

        bool valid = _moving ? _hub.CanMoveTo(_selected, cell) : _hub.CanPlaceAt(_selected, cell);
        UpdatePreview(cell);
        _grid.ShowGhost(_selected, cell, valid, _previewAreas);

        if (!mouse.leftButton.wasPressedThisFrame) return;

        if (_moving)
            ClickMove(cell);
        else
            ClickBuild(cell);
    }

    private void ClickBuild(Vector2Int cell)
    {
        HubBuildingDefinition definition = _selected;
        HubBuildResult result = _hub.TryBuild(definition, cell);
        if (result == HubBuildResult.Ok)
        {
            StopCurrent();
            OnPlaced?.Invoke(definition, cell);
            return;
        }

        UIEventBus.PublishNotification(Loc.T(HubService.Describe(result)), LogEntryType.Warning);
        OnFailed?.Invoke(result);
    }

    private void ClickMove(Vector2Int cell)
    {
        HubBuildingDefinition definition = _selected;
        // Клик по своей же клетке — перенос просто заканчивается, без OnMoved и «X moved»
        if (_hub.TryGetCell(definition, out Vector2Int current) && current == cell)
        {
            Cancel();
            return;
        }

        HubMoveResult result = _hub.TryMove(definition, cell);
        if (result == HubMoveResult.Ok)
        {
            StopCurrent();
            OnMoved?.Invoke(definition, cell);
            return;
        }

        UIEventBus.PublishNotification(Loc.T(HubService.Describe(result)), LogEntryType.Warning);
        OnMoveFailed?.Invoke(result);
    }

    // ─── Предпросмотр соседства (T15) ─────────────────────────

    /// <summary>Пересчёт бонусов и областей подсветки, если клетка призрака сменилась.</summary>
    private void UpdatePreview(Vector2Int cell)
    {
        if (_previewCell == cell) return;

        _previewCell = cell;
        // Для переноса HubService сам не учитывает старое место постройки (тот же id)
        _preview = _hub.PreviewAdjacencyBonuses(_selected, cell);

        // Подсвечиваем вторую постройку каждой пары (новая — это сам призрак), без повторов
        _previewAreas.Clear();
        string id = _selected.Id;
        foreach (HubAdjacencyMatch match in _preview)
        {
            RectInt area = match.Other(id).Area;
            if (!_previewAreas.Contains(area))
                _previewAreas.Add(area);
        }

        OnPreviewChanged?.Invoke(_preview);
    }

    private void ClearPreview()
    {
        bool hadPreview = _previewCell.HasValue || _preview.Count > 0;
        _previewCell = null;
        _preview = NoPreview;
        _previewAreas.Clear();
        if (hadPreview)
            OnPreviewChanged?.Invoke(_preview);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Заканчивает текущее размещение/перенос без событий: призрак скрыт, вид постройки восстановлен.</summary>
    private void StopCurrent()
    {
        _selected = null;
        _moving = false;
        _grid.HideGhost();
        _grid.SetMovingBuilding(null);
        ClearPreview();
    }

    private bool IsNearGrid(Vector2Int cell)
    {
        Vector2Int size = _hub.GridSize;
        return cell.x >= -2 && cell.y >= -2 && cell.x < size.x + 2 && cell.y < size.y + 2;
    }
}
