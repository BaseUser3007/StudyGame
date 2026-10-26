using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Тексты достижений для UI (панель, тост, итоги забега) — на языке игры (Loc).
/// </summary>
public static class AchievementText
{
    /// <summary>"command `mine`, loop `for`" — или пустая строка, если наград нет.</summary>
    public static string FormatRewards(AchievementDefinition definition)
    {
        if (definition == null || definition.Rewards == null) return string.Empty;

        var parts = new List<string>();
        foreach (AchievementReward reward in definition.Rewards)
            if (reward != null && !string.IsNullOrEmpty(reward.Id))
                parts.Add(reward.Describe());
        return string.Join(", ", parts);
    }

    /// <summary>
    /// «Achievements: 7/18 · next map event: Maze at 11» (или «… · all map events unlocked»).
    /// null — трекера нет или пороги событий не заданы (строку не показываем).
    /// </summary>
    public static string FormatMapEvents(AchievementTracker tracker)
    {
        if (tracker == null || !tracker.HasMapEventThresholds) return null;

        int completed = tracker.CompletedCount;
        int total = tracker.Definitions.Count;
        return tracker.TryGetNextMapEvent(out string eventId, out int required)
            ? Loc.F("Achievements: {0}/{1} · next map event: {2} at {3}",
                completed, total, PuzzleTypes.GetDisplayName(eventId), required)
            : Loc.F("Achievements: {0}/{1} · all map events unlocked", completed, total);
    }

    /// <summary>
    /// Условие EfficientGather: «4 × log in one code run, spending ≤ 50 energy». Для остальных — null
    /// (их смысл и так в описании).
    /// </summary>
    public static string FormatCondition(AchievementDefinition definition)
    {
        AchievementCondition condition = definition?.Condition;
        if (condition == null || condition.type != AchievementConditionType.EfficientGather) return null;

        string what = string.IsNullOrWhiteSpace(condition.filter) ? Loc.T("items") : condition.filter.Trim();
        return Loc.F("Goal: {0} × {1} in one code run, spending ≤ {2} energy",
            condition.Target, what, condition.maxEnergy.ToString("0.#"));
    }
}

/// <summary>
/// Панель достижений (кнопка Achievements в верхней панели и в лагере): все определения в порядке каталога —
/// иконка, название, описание, прогресс current/target (у EfficientGather — условие и текущий запуск),
/// награды; выполненные отмечены. Модальная карточка поверх всего, клик мимо карточки закрывает.
/// Пока закрыта — изменения только помечают список устаревшим, перестройка при открытии.
/// Элементы — экземпляр шаблона AchievementsPanel.uxml (хост выдаёт RunScreens.GetOverlay).
/// </summary>
public class AchievementsPanel
{
    private const string HiddenClass = "hidden";

    private readonly AchievementTracker _tracker;
    private readonly VisualElement _backdrop;
    private readonly VisualElement _card;
    private readonly Label _count;
    private readonly ScrollView _list;
    private readonly Label _empty;
    private readonly Label _mapEvents;

    private bool _dirty = true;
    private bool _refreshScheduled;

    public bool IsOpen => !_backdrop.ClassListContains(HiddenClass);

    /// <param name="root">Хост шаблона AchievementsPanel.uxml.</param>
    /// <param name="tracker">null — каталог не настроен, панель покажет пустой список.</param>
    public AchievementsPanel(VisualElement root, AchievementTracker tracker)
    {
        _tracker = tracker;

        _backdrop = root.Q<VisualElement>("achievements-backdrop");
        _card = root.Q<VisualElement>("achievements-card");
        _count = root.Q<Label>("achievements-count");
        _list = root.Q<ScrollView>("achievements-list");
        _empty = root.Q<Label>("achievements-empty");
        Button btnClose = root.Q<Button>("btn-achievements-close");

        Debug.Assert(_backdrop != null && _card != null && _count != null && _list != null && _empty != null
                     && btnClose != null, "AchievementsPanel: elements not found");

        // Строка о событиях на карте (открываются числом достижений) — над списком, в стиле пояснения
        _mapEvents = new Label { enableRichText = false };
        _mapEvents.AddToClassList("achievements-note");
        _mapEvents.AddToClassList("achievements-map-events");
        int listIndex = _card.IndexOf(_list);
        if (listIndex >= 0) _card.Insert(listIndex, _mapEvents);
        else _card.Add(_mapEvents);

        // UI управляется только мышью — кнопка не должна забирать фокус
        btnClose.focusable = false;
        btnClose.clicked += Close;
        _backdrop.RegisterCallback<PointerDownEvent>(HandleBackdropPointerDown);
    }

    // ─── Public API ───────────────────────────────────────────

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        _backdrop.RemoveFromClassList(HiddenClass);
        Rebuild();
    }

    public void Close() => _backdrop.AddToClassList(HiddenClass);

    /// <summary>
    /// Прогресс или выполнение изменились. Открытая панель перестраивается один раз в ближайшем кадре
    /// (событий прогресса за кадр может быть много), закрытая — при следующем открытии.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
        if (!IsOpen || _refreshScheduled) return;

        _refreshScheduled = true;
        _backdrop.schedule.Execute(RefreshIfDirty);
    }

    // ─── Build ────────────────────────────────────────────────

    private void RefreshIfDirty()
    {
        _refreshScheduled = false;
        if (_dirty && IsOpen) Rebuild();
    }

    private void Rebuild()
    {
        _dirty = false;

        Vector2 scroll = _list.scrollOffset;
        _list.Clear();

        IReadOnlyList<AchievementDefinition> definitions = _tracker != null
            ? _tracker.Definitions
            : (IReadOnlyList<AchievementDefinition>)System.Array.Empty<AchievementDefinition>();

        foreach (AchievementDefinition definition in definitions)
            _list.Add(CreateEntry(definition));

        _count.text = _tracker != null ? $"{_tracker.CompletedCount} / {definitions.Count}" : "0 / 0";
        _empty.EnableInClassList(HiddenClass, definitions.Count > 0);

        string mapEvents = AchievementText.FormatMapEvents(_tracker);
        _mapEvents.text = mapEvents ?? string.Empty;
        _mapEvents.style.display = mapEvents != null ? DisplayStyle.Flex : DisplayStyle.None;

        // Живое обновление не должно сбрасывать прокрутку
        _list.schedule.Execute(() => _list.scrollOffset = scroll);
    }

    private VisualElement CreateEntry(AchievementDefinition definition)
    {
        AchievementProgress progress = _tracker.GetProgress(definition);
        bool efficient = definition.Condition.type == AchievementConditionType.EfficientGather;

        var entry = new VisualElement();
        entry.AddToClassList("achievement-entry");
        entry.EnableInClassList("achievement-entry--done", progress.Completed);

        if (definition.Icon != null)
        {
            var icon = new VisualElement();
            icon.AddToClassList("achievement-icon");
            icon.style.backgroundImage = new StyleBackground(definition.Icon);
            entry.Add(icon);
        }

        var body = new VisualElement();
        body.AddToClassList("achievement-body");
        entry.Add(body);

        var titleRow = new VisualElement();
        titleRow.AddToClassList("achievement-title-row");
        body.Add(titleRow);

        titleRow.Add(CreateLabel(Loc.T(definition.Title), "achievement-title"));

        string status = progress.Completed ? Loc.T("Completed")
            : efficient ? Loc.F("This run: {0} / {1}", progress.Current, progress.Target)
            : $"{progress.Current} / {progress.Target}";
        titleRow.Add(CreateLabel(status, "achievement-status"));

        if (!string.IsNullOrEmpty(definition.Description))
            body.Add(CreateLabel(Loc.T(definition.Description), "achievement-description"));

        string condition = AchievementText.FormatCondition(definition);
        if (condition != null)
            body.Add(CreateLabel(condition, "achievement-condition"));

        var bar = new VisualElement();
        bar.AddToClassList("achievement-progress");
        var fill = new VisualElement();
        fill.AddToClassList("achievement-progress-fill");
        fill.style.width = Length.Percent(progress.Fraction * 100f);
        bar.Add(fill);
        body.Add(bar);

        string rewards = AchievementText.FormatRewards(definition);
        if (!string.IsNullOrEmpty(rewards))
            body.Add(CreateLabel(Loc.F("Rewards: {0}", rewards), "achievement-rewards"));

        return entry;
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>Клик по затемнению (не по карточке) закрывает панель.</summary>
    private void HandleBackdropPointerDown(PointerDownEvent evt)
    {
        if (evt.target == _backdrop) Close();
    }

    private static Label CreateLabel(string text, string className)
    {
        // Описания из ассетов — не разметка
        var label = new Label(text) { enableRichText = false };
        label.AddToClassList(className);
        return label;
    }
}
