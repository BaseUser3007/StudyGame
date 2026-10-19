using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Панель построек лагеря (этап 4): карточки HubService.Definitions — иконка, название, размер, описание,
/// эффекты, стоимость с иконками «есть/нужно» и состояние (Locked с достижением, которое открывает,
/// Built, Not enough materials). Build → HubPlacementController.Begin; во время размещения внизу
/// подсказка «Click a cell to build · Right click / Esc to cancel». Туда же выводятся уведомления
/// (их передаёт UIManager через INotificationDisplay) — в лагере верхней панели нет.
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

        if (_placement != null)
        {
            _placement.OnPlaced += HandlePlaced;
            _placement.OnCancelled += HandleCancelled;
            _placement.OnFailed += HandleFailed;
        }

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
            _placement.OnCancelled -= HandleCancelled;
            _placement.OnFailed -= HandleFailed;
        }

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
        bool placingThis = _placement != null && _placement.IsPlacing && _placement.Selected == definition;

        var card = new VisualElement();
        card.AddToClassList("hub-card");
        card.EnableInClassList("hub-card--ready", availability == HubBuildResult.Ok);
        card.EnableInClassList("hub-card--built", built);
        card.EnableInClassList("hub-card--locked", availability == HubBuildResult.Locked);
        card.EnableInClassList("hub-card--placing", placingThis);

        card.Add(CreateHeader(definition));

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            var description = new Label(Loc.T(definition.Description)) { enableRichText = false };
            description.AddToClassList("hub-card-description");
            card.Add(description);
        }

        foreach (string effect in definition.DescribeEffects())
        {
            var effectLabel = new Label(effect) { enableRichText = false };
            effectLabel.AddToClassList("hub-card-effect");
            card.Add(effectLabel);
        }

        card.Add(CreateCost(definition, built));
        card.Add(CreateFooter(definition, availability, placingThis));
        return card;
    }

    private static VisualElement CreateHeader(HubBuildingDefinition definition)
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
        var sizeLabel = new Label(Loc.F("{0}×{1} cells", size.x, size.y));
        sizeLabel.AddToClassList("hub-card-size");
        heading.Add(sizeLabel);

        header.Add(heading);
        return header;
    }

    /// <summary>Стоимость: иконка материала и «есть/нужно» (красным, если не хватает). У построенной — только цена.</summary>
    private VisualElement CreateCost(HubBuildingDefinition definition, bool built)
    {
        var row = new VisualElement();
        row.AddToClassList("hub-card-cost");

        foreach (HubBuildingCost cost in definition.GetCost())
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

        if (availability == HubBuildResult.AlreadyBuilt) return footer;

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

        if (_placement.IsPlacing && _placement.Selected == definition)
        {
            _placement.Cancel();
            return;
        }

        // При отказе Begin сам публикует уведомление и OnFailed
        if (_placement.Begin(definition))
            Refresh();
    }

    private void HandlePlaced(HubBuildingDefinition definition, Vector2Int cell) => Refresh();

    private void HandleCancelled() => Refresh();

    // Место занято / не хватает материалов: причину уже показало уведомление, карточки могли устареть
    private void HandleFailed(HubBuildResult result) => Refresh();

    // ─── Подсказка внизу ──────────────────────────────────────

    private void RefreshHint()
    {
        bool placing = _placement != null && _placement.IsPlacing;
        bool hasStatus = !_status.ClassListContains(HiddenClass);

        _hintTitle.text = placing ? Loc.F("Placing: {0}", Loc.T(_placement.Selected.Title)) : string.Empty;
        _hintTitle.EnableInClassList(HiddenClass, !placing);
        _hintText.EnableInClassList(HiddenClass, !placing);
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
