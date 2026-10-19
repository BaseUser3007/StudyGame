using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Лог внизу главного окна кода: итог запуска, уведомления забега и ошибки с объяснением
/// (LuaErrorExplainer). Скрыт, пока пуст; кнопка × очищает и прячет. Только мышь: элементы
/// лога не принимают фокус, тексты — обычные (не rich text), чтобы `<` в ошибках не ломал разметку.
/// </summary>
public class RunLog
{
    private const int MaxEntries = 30;

    private readonly VisualElement _root;
    private readonly ScrollView _entries;
    private readonly CodeEditorTheme _theme;

    public RunLog(VisualElement root, CodeEditorTheme theme)
    {
        _root = root;
        _theme = theme;
        _entries = root?.Q<ScrollView>("run-log-entries");
        Button clear = root?.Q<Button>("btn-run-log-clear");

        Debug.Assert(_root != null, "RunLog: run-log not found");
        Debug.Assert(_entries != null, "RunLog: run-log-entries not found");
        Debug.Assert(clear != null, "RunLog: btn-run-log-clear not found");

        if (clear != null)
        {
            clear.focusable = false;
            clear.clicked += Clear;
        }
        if (_entries != null) _entries.focusable = false;
        _root?.AddToClassList("hidden");
    }

    // ─── Public API ───────────────────────────────────────────

    public void AddMessage(string message, LogEntryType type)
    {
        if (string.IsNullOrEmpty(message) || _entries == null) return;

        VisualElement entry = CreateEntry(type);
        entry.Add(CreateText(message, "run-log-message", ColorOf(type)));
        Append(entry);
    }

    /// <summary>
    /// Ошибка запуска: сырой текст, затем (если есть) место, объяснение и что попробовать.
    /// </summary>
    public void AddCodeError(LuaErrorExplanation error)
    {
        if (error == null || _entries == null) return;

        VisualElement entry = CreateEntry(LogEntryType.Error);
        entry.Add(CreateText(error.Raw, "run-log-message", ColorOf(LogEntryType.Error)));

        if (error.HasExplanation)
        {
            if (error.Location != null)
                entry.Add(CreateText(error.Location, "run-log-location", null));
            entry.Add(CreateText(error.Explanation, "run-log-explanation", null));
            if (!string.IsNullOrEmpty(error.Suggestion))
                entry.Add(CreateText(Loc.F("Try: {0}", error.Suggestion), "run-log-suggestion", null));
        }
        Append(entry);
    }

    public void Clear()
    {
        _entries?.Clear();
        _root?.AddToClassList("hidden");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private VisualElement CreateEntry(LogEntryType type)
    {
        var entry = new VisualElement { pickingMode = PickingMode.Ignore };
        entry.AddToClassList("run-log-entry");
        entry.AddToClassList($"run-log-entry--{type.ToString().ToLowerInvariant()}");
        return entry;
    }

    private static Label CreateText(string text, string className, Color? color)
    {
        var label = new Label(text)
        {
            enableRichText = false,
            parseEscapeSequences = false,
            pickingMode = PickingMode.Ignore
        };
        label.AddToClassList(className);
        if (color.HasValue) label.style.color = color.Value;
        return label;
    }

    private void Append(VisualElement entry)
    {
        _root.RemoveFromClassList("hidden");
        _entries.Add(entry);
        while (_entries.contentContainer.childCount > MaxEntries)
            _entries.contentContainer.RemoveAt(0);

        // Прокрутка к новой записи — после раскладки
        _entries.schedule.Execute(() =>
        {
            if (entry.panel != null) _entries.ScrollTo(entry);
        });
    }

    private Color? ColorOf(LogEntryType type)
    {
        if (_theme == null) return null;
        switch (type)
        {
            case LogEntryType.Success: return _theme.logColorSuccess;
            case LogEntryType.Warning: return _theme.logColorWarning;
            case LogEntryType.Error: return _theme.logColorError;
            default: return _theme.logColorInfo;
        }
    }
}
