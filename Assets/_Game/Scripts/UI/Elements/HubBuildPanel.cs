using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Панель построек лагеря (этап 4): карточки HubService.Definitions — иконка, название, размер, описание,
/// эффекты, стоимость с иконками «есть/нужно» и состояние (Locked с достижением, которое открывает,
/// Built, Not enough materials). У построенной многоуровневой (T14) — «Level N/M», итог текущего уровня,
/// прибавка и стоимость следующего и кнопка Upgrade (HubService.TryUpgrade; на максимуме кнопки нет).
/// Build → HubPlacementController.Begin; во время размещения внизу
/// подсказка «Click a cell to build · Right click / Esc to cancel». Туда же выводятся уведомления
/// (их передаёт UIManager через INotificationDisplay) — в лагере верхней панели нет.
/// Бонусы за соседство (T15): в карточке — «Neighbour bonuses:» (возможные/действующие), при размещении под
/// подсказкой — строки предпросмотра «+5 energy limit (next to Shrine)» по HubPlacementController.OnPreviewChanged.
/// Перенос: у каждой построенной (включая портал) кнопка Move → HubPlacementController.BeginMove; во время
/// переноса она же — Cancel, внизу «Click a cell to move · Right click / Esc to cancel». Постоянная постройка
/// (портал) не строится и не улучшается — только «Built» и Move.
/// Элементы — из шаблона HubPanel.uxml (часть RunScreens.uxml), создаёт CodeEditorManager только в лагере.
/// </summary>
public class HubBuildPanel : INotificationDisplay
{
    private const string HiddenClass = "hidden";
    private const long StatusDurationMs = 4000;

    private readonly HubService _hub;
    private readonly HubPlacementController _placement;
    // id постройки → название достижения, награда которого её открывает
    private readonly Dictionary<string, string> _unlockSources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ScrollView _list;
    private readonly Label _empty;
    private readonly VisualElement _hint;
    private readonly Label _hintTitle;
    private readonly Label _hintText;
    private readonly Label _status;
    // Бонусы за соседство под призраком (T15): строка подсказки, создаётся кодом (без правки UXML)
    private readonly Label _adjacency;
    private IVisualElementScheduledItem _statusHide;

    /// <param name="root">Корень экрана лагеря (экземпляр HubPanel.uxml).</param>
    /// <param name="achievements">Откуда узнать, какое достижение открывает постройку; null — без подсказки.</param>
    public HubBuildPanel(
        VisualElement root, HubService hub, HubPlacementController placement, AchievementTracker achievements)
    {
        _hub = hub;
        _placement = placement;

        _list = root.Q<ScrollView>("hub-buildings-list");
        _empty = root.Q<Label>("hub-buildings-empty");
        _hint = root.Q<VisualElement>("hub-hint");
        _hintTitle = root.Q<Label>("hub-hint-title");
        _hintText = root.Q<Label>("hub-hint-text");
        _status = root.Q<Label>("hub-status");

        ValidateReferences();
        CollectUnlockSources(achievements);

        // Стиль — как у строки уведомления (переносы, по центру), зелёный цвет «успеха»
        _adjacency = new Label { name = "hub-adjacency", enableRichText = false, pickingMode = PickingMode.Ignore };
        _adjacency.AddToClassList("hub-status");
        _adjacency.AddToClassList("hub-status--success");
        _adjacency.AddToClassList(HiddenClass);
        _hint.Insert(_hint.IndexOf(_hintText) + 1, _adjacency);

        if (_placement != null)
        {
            _placement.OnPlaced += HandlePlaced;
            _placement.OnMoved += HandleMoved;
            _placement.OnCancelled += HandleCancelled;
            _placement.OnFailed += HandleFailed;
            _placement.OnMoveFailed += HandleMoveFailed;
            _placement.OnPreviewChanged += HandlePreviewChanged;
        }

        if (_hub != null)
            _hub.OnUpgraded += HandleUpgraded;

        Refresh();
    }

    private void ValidateReferences()
    {
        Debug.Assert(_hub != null, "HubBuildPanel: HubService is null");
        Debug.Assert(_placement != null, "HubBuildPanel: HubPlacementController is null");
        Debug.Assert(_list != null && _empty != null, "HubBuildPanel: building list elements not found");
        Debug.Assert(_hint != null && _hintTitle != null && _hintText != null && _status != null,
            "HubBuildPanel: hint elements not found");
    }

    public void Dispose()
    {
        if (_placement != null)
        {
            _placement.OnPlaced -= HandlePlaced;
            _placement.OnMoved -= HandleMoved;
            _placement.OnCancelled -= HandleCancelled;
            _placement.OnFailed -= HandleFailed;
            _placement.OnMoveFailed -= HandleMoveFailed;
            _placement.OnPreviewChanged -= HandlePreviewChanged;
        }

        if (_hub != null)
            _hub.OnUpgraded -= HandleUpgraded;

        _statusHide?.Pause();
        _statusHide = null;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Идёт размещение постройки (карточка подсказки туториала в это время скрыта).</summary>
    public bool IsPlacing => _placement != null && _placement.IsPlacing;

    /// <summary>Хоть одну постройку можно поставить прямо сейчас (для шага туториала).</summary>
    public bool IsAnyBuildAvailable()
    {
        if (_hub == null) return false;
        foreach (HubBuildingDefinition definition in _hub.Definitions)
            if (_hub.CheckAvailability(definition) == HubBuildResult.Ok)
                return true;
        return false;
    }

    /// <summary>
    /// В лагере стоит хоть одна постройка игрока — постоянные (портал) не считаются, они стоят всегда
    /// (для шага туториала hub_build).
    /// </summary>
    public bool HasPlayerBuilding()
    {
        if (_hub == null) return false;
        foreach (HubPlacement placed in _hub.GetPlaced())
            if (placed.Definition == null || !placed.Definition.IsPermanent)
                return true;
        return false;
    }

    /// <summary>Перестроить карточки (склад, открытия или расстановка изменились).</summary>
    public void Refresh()
    {
        _list.Clear();

        IReadOnlyList<HubBuildingDefinition> definitions = _hub != null
            ? _hub.Definitions
            : (IReadOnlyList<HubBuildingDefinition>)Array.Empty<HubBuildingDefinition>();

        foreach (HubBuildingDefinition definition in definitions)
            _list.Add(CreateCard(definition));
        _empty.EnableInClassList(HiddenClass, definitions.Count > 0);

        RefreshHint();
    }

    /// <summary>Последнее уведомление UIEventBus — строкой под подсказкой размещения, на несколько секунд.</summary>
    public void ShowNotification(string message, LogEntryType type)
    {
        if (string.IsNullOrEmpty(message)) return;

        _status.text = message;
        _status.EnableInClassList("hub-status--success", type == LogEntryType.Success);
        _status.EnableInClassList("hub-status--warning", type == LogEntryType.Warning);
        _status.EnableInClassList("hub-status--error", type == LogEntryType.Error);
        _status.RemoveFromClassList(HiddenClass);

        _statusHide?.Pause();
        _statusHide = _status.schedule.Execute(HideStatus).StartingIn(StatusDurationMs);
        RefreshHint();
    }

    // ─── Карточка ─────────────────────────────────────────────

    private VisualElement CreateCard(HubBuildingDefinition definition)
    {
        HubBuildResult availability = _hub.CheckAvailability(definition);
        bool built = availability == HubBuildResult.AlreadyBuilt;
        // И размещение новой, и перенос этой постройки подсвечивают карточку
        bool placingThis = _placement != null && _placement.IsPlacing && _placement.Selected == definition;

        // Уровень: у построенной — текущий, иначе постройка даёт уровень 1. Портал не улучшается
        int level = built ? _hub.GetLevel(definition) : 1;
        bool upgradable = built && !definition.IsPermanent && level < definition.MaxLevel;
        HubUpgradeResult upgrade = built ? _hub.CheckUpgrade(definition) : HubUpgradeResult.NotBuilt;

        var card = new VisualElement();
        card.AddToClassList("hub-card");
        card.EnableInClassList("hub-card--ready",
            availability == HubBuildResult.Ok || upgrade == HubUpgradeResult.Ok);
        card.EnableInClassList("hub-card--built", built);
        card.EnableInClassList("hub-card--locked", availability == HubBuildResult.Locked);
        card.EnableInClassList("hub-card--placing", placingThis);

        card.Add(CreateHeader(definition, built ? level : 0));

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            var description = new Label(Loc.T(definition.Description)) { enableRichText = false };
            description.AddToClassList("hub-card-description");
            card.Add(description);
        }

        // Итог текущего уровня (у непостроенной — уровня 1)
        foreach (string effect in definition.DescribeEffects(level))
            card.Add(CreateEffectLabel(effect, next: false));

        if (upgradable)
        {
            // Что добавит следующий уровень и сколько он стоит (чипы «есть/нужно», как у постройки)
            var nextTitle = new Label(Loc.F("Level {0} adds:", level + 1)) { enableRichText = false };
            nextTitle.AddToClassList("hub-card-next");
            card.Add(nextTitle);
            foreach (string effect in definition.DescribeLevelEffects(level + 1))
                card.Add(CreateEffectLabel(effect, next: true));
            card.Add(CreateCost(definition.GetCost(level + 1), showStock: true));
        }
        else if ((!built || definition.MaxLevel == 1) && definition.GetCost().Count > 0)
        {
            // Непостроенная — цена с запасом склада; одноуровневая построенная — просто цена, как раньше.
            // Бесплатная (портал) — без строки цены
            card.Add(CreateCost(definition.GetCost(), showStock: !built));
        }

        AddAdjacencyBonuses(card, definition, built);

        card.Add(built
            ? CreateUpgradeFooter(definition, level, upgrade, placingThis)
            : CreateFooter(definition, availability, placingThis));
        return card;
    }

    /// <summary>
    /// Бонусы за соседство (T15): заголовок и строки «+5 energy limit (next to Shrine)» — свои записи постройки
    /// и записи других построек с её id. Действующие сейчас (обе стоят рядом) — зелёным с пометкой «active»,
    /// возможные — жёлтым. Нет бонусов — ничего не добавляется.
    /// </summary>
    private void AddAdjacencyBonuses(VisualElement card, HubBuildingDefinition definition, bool built)
    {
        var possible = _hub.GetPossibleAdjacencyBonuses(definition);
        if (possible.Count == 0) return;

        List<HubAdjacencyMatch> active = built ? _hub.GetAdjacencyBonuses() : null;

        var title = new Label(Loc.T("Neighbour bonuses:")) { enableRichText = false };
        title.AddToClassList("hub-card-next");
        card.Add(title);

        foreach ((HubBuildingDefinition owner, string otherId, HubAdjacencyBonus bonus) in possible)
        {
            HubBuildingDefinition other = _hub.Find(otherId);
            string otherTitle = other != null ? Loc.T(other.Title) : otherId;
            string line = Loc.F("{0} (next to {1})", bonus.ToEffect().Describe(), otherTitle);

            bool isActive = active != null && active.Exists(m => m.Bonus == bonus && m.Owner.Definition == owner);
            if (isActive) line = Loc.F("{0} · active", line);
            card.Add(CreateEffectLabel(line, next: !isActive));
        }
    }

    private static Label CreateEffectLabel(string text, bool next)
    {
        var label = new Label(text) { enableRichText = false };
        label.AddToClassList("hub-card-effect");
        label.EnableInClassList("hub-card-effect--next", next);
        return label;
    }

    /// <param name="level">Уровень построенной (подпись «Level N/M» у многоуровневых); 0 — не построена.</param>
    private static VisualElement CreateHeader(HubBuildingDefinition definition, int level)
    {
        var header = new VisualElement();
        header.AddToClassList("hub-card-header");

        var icon = new VisualElement();
        icon.AddToClassList("hub-card-icon");
        Sprite sprite = definition.Icon;
        if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
        header.Add(icon);

        var heading = new VisualElement();
        heading.AddToClassList("hub-card-heading");

        var title = new Label(Loc.T(definition.Title)) { enableRichText = false };
        title.AddToClassList("hub-card-title");
        heading.Add(title);

        Vector2Int size = definition.Size;
        string sizeText = Loc.F("{0}×{1} cells", size.x, size.y);
        if (definition.MaxLevel > 1)
            sizeText += " · " + (level > 0
                ? Loc.F("Level {0}/{1}", level, definition.MaxLevel)
                : Loc.F("Up to level {0}", definition.MaxLevel));
        var sizeLabel = new Label(sizeText);
        sizeLabel.AddToClassList("hub-card-size");
        heading.Add(sizeLabel);

        header.Add(heading);
        return header;
    }

    /// <summary>
    /// Стоимость: иконка материала и «есть/нужно» (красным, если не хватает). showStock = false — только цена
    /// (построенная одноуровневая постройка).
    /// </summary>
    private VisualElement CreateCost(List<HubBuildingCost> costs, bool showStock)
    {
        bool built = !showStock;
        var row = new VisualElement();
        row.AddToClassList("hub-card-cost");

        foreach (HubBuildingCost cost in costs)
        {
            int stored = _hub.GetStored(cost.Key);
            bool missing = !built && stored < cost.Amount;

            var chip = new VisualElement();
            chip.AddToClassList("hub-cost-chip");
            chip.EnableInClassList("hub-cost-chip--missing", missing);
            chip.tooltip = cost.Item != null ? Loc.T(cost.Item.DisplayName) : cost.Key;

            Sprite sprite = cost.Item != null ? cost.Item.Icon : null;
            if (sprite != null)
            {
                var icon = new VisualElement();
                icon.AddToClassList("hub-cost-icon");
                icon.style.backgroundImage = new StyleBackground(sprite);
                chip.Add(icon);
            }

            string amount = built ? $"{cost.Amount}" : $"{stored}/{cost.Amount}";
            // Без иконки — название материала текстом
            string text = sprite != null ? amount : $"{chip.tooltip} {amount}";
            var label = new Label(text) { enableRichText = false };
            label.AddToClassList("hub-cost-text");
            chip.Add(label);

            row.Add(chip);
        }

        return row;
    }

    private VisualElement CreateFooter(HubBuildingDefinition definition, HubBuildResult availability, bool placingThis)
    {
        var footer = new VisualElement();
        footer.AddToClassList("hub-card-footer");

        var state = new Label(DescribeState(definition, availability, placingThis)) { enableRichText = false };
        state.AddToClassList("hub-card-state");
        state.EnableInClassList("hub-card-state--locked", availability == HubBuildResult.Locked);
        state.EnableInClassList("hub-card-state--built", availability == HubBuildResult.AlreadyBuilt);
        state.EnableInClassList("hub-card-state--missing", availability == HubBuildResult.NotAffordable);
        footer.Add(state);

        // Портал не строится из панели (не поставлен — значит, сетка забита: кнопки нет)
        if (availability == HubBuildResult.AlreadyBuilt || availability == HubBuildResult.Permanent) return footer;

        // UI управляется только мышью — кнопка не должна забирать фокус
        var button = new Button { text = Loc.T(placingThis ? "Cancel" : "Build"), focusable = false };
        button.AddToClassList("screen-btn");
        button.AddToClassList(placingThis ? "screen-btn--danger" : "screen-btn--primary");
        button.AddToClassList("hub-card-build");
        button.SetEnabled(availability == HubBuildResult.Ok);
        button.clicked += () => HandleBuildClicked(definition);
        footer.Add(button);

        return footer;
    }

    /// <summary>
    /// Низ карточки построенной постройки: состояние (Built / Ready to upgrade / нехватка материалов /
    /// максимальный уровень), кнопка Move (перенос; во время переноса этой постройки — Cancel) и кнопка
    /// Upgrade, пока уровень не максимальный (у портала её нет).
    /// </summary>
    private VisualElement CreateUpgradeFooter(
        HubBuildingDefinition definition, int level, HubUpgradeResult upgrade, bool movingThis)
    {
        var footer = new VisualElement();
        footer.AddToClassList("hub-card-footer");

        string text;
        switch (upgrade)
        {
            case HubUpgradeResult.MaxLevel:
                text = definition.MaxLevel > 1 && !definition.IsPermanent
                    ? Loc.T("Built · maximum level")
                    : Loc.T("Built");
                break;
            default:
                text = Loc.T(HubService.Describe(upgrade));
                break;
        }

        var state = new Label(text) { enableRichText = false };
        state.AddToClassList("hub-card-state");
        state.EnableInClassList("hub-card-state--built", upgrade == HubUpgradeResult.MaxLevel || upgrade == HubUpgradeResult.Ok);
        state.EnableInClassList("hub-card-state--missing", upgrade == HubUpgradeResult.NotAffordable);
        footer.Add(state);

        var buttons = new VisualElement();
        buttons.AddToClassList("hub-card-buttons");
        footer.Add(buttons);

        // Перенос — у любой построенной, бесплатно; UI управляется только мышью — кнопки без фокуса
        var move = new Button { text = Loc.T(movingThis ? "Cancel" : "Move"), focusable = false };
        move.AddToClassList("screen-btn");
        move.AddToClassList(movingThis ? "screen-btn--danger" : "screen-btn--secondary");
        move.AddToClassList("hub-card-build");
        move.AddToClassList("hub-card-move");
        move.clicked += () => HandleMoveClicked(definition);
        buttons.Add(move);

        if (definition.IsPermanent || level >= definition.MaxLevel) return footer;

        var button = new Button { text = Loc.T("Upgrade"), focusable = false };
        button.AddToClassList("screen-btn");
        button.AddToClassList("screen-btn--primary");
        button.AddToClassList("hub-card-build");
        button.AddToClassList("hub-card-upgrade");
        button.SetEnabled(upgrade == HubUpgradeResult.Ok);
        button.clicked += () => HandleUpgradeClicked(definition);
        buttons.Add(button);

        return footer;
    }

    private string DescribeState(HubBuildingDefinition definition, HubBuildResult availability, bool placingThis)
    {
        switch (availability)
        {
            case HubBuildResult.Ok:
                return Loc.T(placingThis ? "Placing — click a free cell" : "Ready to build");
            case HubBuildResult.AlreadyBuilt:
                return Loc.T("Built");
            case HubBuildResult.Locked:
                return _unlockSources.TryGetValue(definition.Id, out string achievement)
                    ? Loc.F("Locked · Unlock: {0}", Loc.T(achievement))
                    : Loc.T("Locked · complete an achievement");
            default:
                return Loc.T(HubService.Describe(availability));
        }
    }

    // ─── Размещение ───────────────────────────────────────────

    /// <summary>Build — начать размещение; повторный клик по той же карточке (Cancel) — отменить.</summary>
    private void HandleBuildClicked(HubBuildingDefinition definition)
    {
        if (_placement == null) return;

        if (_placement.IsPlacing && !_placement.IsMoving && _placement.Selected == definition)
        {
            _placement.Cancel();
            return;
        }

        // При отказе Begin сам публикует уведомление и OnFailed
        if (_placement.Begin(definition))
            Refresh();
    }

    // ─── Перенос ──────────────────────────────────────────────

    /// <summary>Move — начать перенос построенной; повторный клик по той же карточке (Cancel) — отменить.</summary>
    private void HandleMoveClicked(HubBuildingDefinition definition)
    {
        if (_placement == null) return;

        if (_placement.IsMoving && _placement.Selected == definition)
        {
            _placement.Cancel();
            return;
        }

        // При отказе BeginMove сам публикует уведомление и OnMoveFailed
        if (_placement.BeginMove(definition))
            Refresh();
    }

    /// <summary>Перенос завершён: карточки (действующие бонусы соседства) и сообщение.</summary>
    private void HandleMoved(HubBuildingDefinition definition, Vector2Int cell)
    {
        Refresh();
        UIEventBus.PublishNotification(Loc.F("{0} moved", Loc.T(definition.Title)), LogEntryType.Success);
    }

    // Место занято / вне сетки: причину уже показало уведомление
    private void HandleMoveFailed(HubMoveResult result) => Refresh();

    // ─── Улучшение ────────────────────────────────────────────

    /// <summary>Upgrade — сразу улучшить (оплата со склада и сохранение — в HubService.TryUpgrade).</summary>
    private void HandleUpgradeClicked(HubBuildingDefinition definition)
    {
        HubUpgradeResult result = _hub.TryUpgrade(definition);
        if (result == HubUpgradeResult.Ok) return; // карточки и уведомление — в HandleUpgraded

        UIEventBus.PublishNotification(Loc.T(HubService.Describe(result)), LogEntryType.Warning);
        Refresh();
    }

    private void HandleUpgraded(HubBuildingDefinition definition, int level)
    {
        Refresh();
        UIEventBus.PublishNotification(
            Loc.F("{0} upgraded to level {1}", Loc.T(definition.Title), level), LogEntryType.Success);
    }

    private void HandlePlaced(HubBuildingDefinition definition, Vector2Int cell) => Refresh();

    private void HandleCancelled() => Refresh();

    /// <summary>Подпись бонусов за соседство под призраком: по строке на бонус или «нет бонуса», если он возможен.</summary>
    private void HandlePreviewChanged(IReadOnlyList<HubAdjacencyMatch> preview)
    {
        _adjacency.text = DescribePreview(preview);
        RefreshHint();
    }

    private string DescribePreview(IReadOnlyList<HubAdjacencyMatch> preview)
    {
        HubBuildingDefinition selected = _placement != null ? _placement.Selected : null;
        if (selected == null) return string.Empty;

        if (preview == null || preview.Count == 0)
        {
            // «Здесь бонуса нет» — только если у постройки вообще есть бонусы за соседство и призрак стоит
            // на допустимом месте (на красном призраке подпись не нужна)
            Vector2Int? cell = _placement.PreviewCell;
            bool valid = cell.HasValue && (_placement.IsMoving
                ? _hub.CanMoveTo(selected, cell.Value)
                : _hub.CanPlaceAt(selected, cell.Value));
            return valid
                   && _hub.GetPossibleAdjacencyBonuses(selected).Count > 0
                ? Loc.T("No neighbour bonus here")
                : string.Empty;
        }

        var lines = new List<string>(preview.Count);
        foreach (HubAdjacencyMatch match in preview)
            lines.Add(match.Describe(selected.Id));
        return string.Join("\n", lines);
    }

    // Место занято / не хватает материалов: причину уже показало уведомление, карточки могли устареть
    private void HandleFailed(HubBuildResult result) => Refresh();

    // ─── Подсказка внизу ──────────────────────────────────────

    private void RefreshHint()
    {
        bool placing = _placement != null && _placement.IsPlacing;
        bool hasStatus = !_status.ClassListContains(HiddenClass);

        bool moving = placing && _placement.IsMoving;

        _hintTitle.text = !placing
            ? string.Empty
            : moving
                ? Loc.F("Moving: {0}", Loc.T(_placement.Selected.Title))
                : Loc.F("Placing: {0}", Loc.T(_placement.Selected.Title));
        // Текст из UXML переводит LocUxml; здесь он ставится заново, т.к. у переноса своя подсказка
        _hintText.text = Loc.T(moving
            ? "Click a cell to move · Right click / Esc to cancel"
            : "Click a cell to build · Right click / Esc to cancel");
        _hintTitle.EnableInClassList(HiddenClass, !placing);
        _hintText.EnableInClassList(HiddenClass, !placing);
        _adjacency.EnableInClassList(HiddenClass, !placing || string.IsNullOrEmpty(_adjacency.text));
        _hint.EnableInClassList(HiddenClass, !placing && !hasStatus);
    }

    private void HideStatus()
    {
        _statusHide = null;
        _status.AddToClassList(HiddenClass);
        RefreshHint();
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Какое достижение открывает постройку: награда HubBuilding с её id.</summary>
    private void CollectUnlockSources(AchievementTracker achievements)
    {
        if (achievements == null) return;

        foreach (AchievementDefinition achievement in achievements.Definitions)
        {
            if (achievement == null || achievement.Rewards == null) continue;

            foreach (AchievementReward reward in achievement.Rewards)
            {
                if (reward == null || reward.kind != AchievementRewardKind.HubBuilding) continue;
                if (string.IsNullOrEmpty(reward.Id) || _unlockSources.ContainsKey(reward.Id)) continue;
                _unlockSources[reward.Id] = achievement.Title;
            }
        }
    }
}
