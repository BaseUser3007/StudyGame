using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Скрытая боковая панель сохранённого кода. Открывается ползунком посередине правого края.
/// Окно кода, перетащенное за заголовок на панель (или на ползунок), сохраняется в библиотеку —
/// саму проверку попадания и сохранение делает CodeEditorManager.
/// Copy у записи кладёт её в буфер обмена с заголовком «-- CodeRogue: Title», Paste вверху панели
/// создаёт запись из буфера (так игроки делятся решениями).
/// </summary>
public class CodeLibraryPanel : VisualElement
{
    private const string HiddenClass = "hidden";
    private const string DropTargetClass = "library-drop-target";
    private const string ConfirmClass = "snippet-btn--confirm";
    private const int PreviewLength = 60;
    private const string StatusWarningClass = "library-status--warning";
    private const long StatusDurationMs = 3000;

    // ─── Буфер обмена ─────────────────────────────────────────
    // Первая строка текста в буфере: «-- CodeRogue: <Title>»
    private const string ClipboardHeader = "-- CodeRogue:";
    private const string PastedTitle = "Pasted";
    private const int MaxPasteLength = 20000;

    private static readonly LuaIdentifierValidator TitleValidator = new LuaIdentifierValidator();

    private readonly CodeLibrary _library;

    private Button _btnToggle;
    private VisualElement _panel;
    private ScrollView _snippetList;
    private Label _listEmpty;
    private Label _lockedNote;
    private Button _btnPaste;
    private Label _status;
    private IVisualElementScheduledItem _statusHide;
    // Окна-«классы» открыты: записи можно открыть как окна
    private bool _canOpenSnippets = true;

    public event Action OnOpened;
    public event Action<CodeSnippet> OnOpenSnippetRequested;

    public bool IsOpen => !_panel.ClassListContains(HiddenClass);

    public CodeLibraryPanel(VisualTreeAsset template, CodeLibrary library)
    {
        _library = library;

        VisualElement root = template.Instantiate();
        LocUxml.Translate(root);
        QueryElements(root);
        ValidateReferences();

        // Корень растянут на весь экран — клики мимо панели должны проходить в игру
        pickingMode = PickingMode.Ignore;
        root.pickingMode = PickingMode.Ignore;
        root.style.flexGrow = 1;
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.right = 0;
        style.bottom = 0;

        // UI управляется только мышью — кнопки не должны забирать фокус
        _btnToggle.focusable = false;
        Button btnClose = root.Q<Button>("btn-close");
        btnClose.focusable = false;
        _btnPaste.focusable = false;

        _btnToggle.clicked += Toggle;
        btnClose.clicked += Close;
        _btnPaste.clicked += PasteFromClipboard;

        _library.OnChanged += BuildList;

        BuildList();
        Add(root);
    }

    private void QueryElements(VisualElement root)
    {
        _btnToggle = root.Q<Button>("btn-library-toggle");
        _panel = root.Q<VisualElement>("library-panel");
        _snippetList = root.Q<ScrollView>("snippet-list");
        _listEmpty = root.Q<Label>("list-empty");
        _lockedNote = root.Q<Label>("locked-note");
        _btnPaste = root.Q<Button>("btn-paste");
        _status = root.Q<Label>("library-status");
    }

    private void ValidateReferences()
    {
        Debug.Assert(_library != null, "CodeLibraryPanel: library is null");
        Debug.Assert(_btnToggle != null, "CodeLibraryPanel: btn-library-toggle not found");
        Debug.Assert(_panel != null, "CodeLibraryPanel: library-panel not found");
        Debug.Assert(_snippetList != null, "CodeLibraryPanel: snippet-list not found");
        Debug.Assert(_listEmpty != null, "CodeLibraryPanel: list-empty not found");
        Debug.Assert(_lockedNote != null, "CodeLibraryPanel: locked-note not found");
        Debug.Assert(_btnPaste != null, "CodeLibraryPanel: btn-paste not found");
        Debug.Assert(_status != null, "CodeLibraryPanel: library-status not found");
    }

    // ─── Public API ───────────────────────────────────────────

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        _panel.RemoveFromClassList(HiddenClass);
        _btnToggle.AddToClassList("library-toggle--active");
        _btnToggle.text = "›";
        OnOpened?.Invoke();
    }

    public void Close()
    {
        _panel.AddToClassList(HiddenClass);
        _btnToggle.RemoveFromClassList("library-toggle--active");
        _btnToggle.text = "‹";
        BuildList(); // сбрасывает неподтверждённые удаления
    }

    /// <summary>
    /// Попадает ли точка (координаты панели UI) в зону сброса: открытую панель или ползунок.
    /// </summary>
    public bool IsDropTarget(Vector2 panelPosition)
    {
        if (_btnToggle.worldBound.Contains(panelPosition)) return true;
        return IsOpen && _panel.worldBound.Contains(panelPosition);
    }

    /// <summary>
    /// Подсветка зоны сброса, пока над ней тащат окно.
    /// </summary>
    public void SetDropHighlight(bool active)
    {
        _panel.EnableInClassList(DropTargetClass, active);
        _btnToggle.EnableInClassList(DropTargetClass, active);
    }

    /// <summary>
    /// Пока окна-«классы» закрыты, записи нельзя открыть как окна: кнопки «Открыть» выключены,
    /// над списком — пояснение. Хранить и удалять записи можно всегда.
    /// </summary>
    public void SetSnippetOpeningAvailable(bool available)
    {
        if (_canOpenSnippets == available) return;
        _canOpenSnippets = available;
        _lockedNote.EnableInClassList(HiddenClass, available);
        BuildList();
    }

    // ─── Snippet List ─────────────────────────────────────────

    private void BuildList()
    {
        _snippetList.Clear();

        foreach (CodeSnippet snippet in _library.Snippets)
            _snippetList.Add(CreateEntry(snippet));

        _listEmpty.EnableInClassList(HiddenClass, _library.Snippets.Count > 0);
    }

    private VisualElement CreateEntry(CodeSnippet snippet)
    {
        VisualElement entry = new VisualElement();
        entry.AddToClassList("snippet-entry");
        entry.tooltip = snippet.code;

        VisualElement info = new VisualElement();
        info.AddToClassList("snippet-info");

        info.Add(CreateLabel(snippet.title, "snippet-name"));
        info.Add(CreateLabel(Loc.F("{0} lines · {1}", CountLines(snippet.code), snippet.savedAt), "snippet-meta"));
        info.Add(CreateLabel(GetPreview(snippet.code), "snippet-preview"));
        entry.Add(info);

        Button btnOpen = new Button(() => OnOpenSnippetRequested?.Invoke(snippet)) { text = Loc.T("Open") };
        btnOpen.AddToClassList("snippet-btn");
        btnOpen.focusable = false;
        btnOpen.tooltip = Loc.T(_canOpenSnippets ? "Open in a new window" : "Class windows are not unlocked yet");
        btnOpen.SetEnabled(_canOpenSnippets);
        entry.Add(btnOpen);

        Button btnCopy = new Button(() => CopyToClipboard(snippet)) { text = Loc.T("Copy") };
        btnCopy.AddToClassList("snippet-btn");
        btnCopy.focusable = false;
        btnCopy.tooltip = Loc.T("Copy to the clipboard to share");
        entry.Add(btnCopy);

        // Удаление в два нажатия: «×» → «Delete?» → удалено
        Button btnDelete = new Button { text = "×" };
        btnDelete.AddToClassList("snippet-btn");
        btnDelete.focusable = false;
        btnDelete.tooltip = Loc.T("Delete from the library");
        btnDelete.clicked += () =>
        {
            if (btnDelete.ClassListContains(ConfirmClass))
            {
                _library.Remove(snippet);
                return;
            }
            btnDelete.AddToClassList(ConfirmClass);
            btnDelete.text = Loc.T("Delete?");
        };
        entry.Add(btnDelete);

        return entry;
    }

    // ─── Clipboard ────────────────────────────────────────────

    private void CopyToClipboard(CodeSnippet snippet)
    {
        GUIUtility.systemCopyBuffer = FormatForClipboard(snippet.title, snippet.code);
        ShowStatus(Loc.T("Copied"), LogEntryType.Success);
    }

    private void PasteFromClipboard()
    {
        string text = GUIUtility.systemCopyBuffer;

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowStatus(Loc.T("The clipboard is empty"), LogEntryType.Warning);
            return;
        }

        if (text.Length > MaxPasteLength)
        {
            ShowStatus(Loc.F("The text is too long: {0} characters (at most {1})", text.Length, MaxPasteLength),
                LogEntryType.Warning);
            return;
        }

        ParseClipboard(text, out string title, out string code);
        // Одинаковое название перезаписывает запись — как при сбросе окна в библиотеку
        _library.Save(title, code);
        ShowStatus(Loc.F("Saved: {0}", title), LogEntryType.Success);
    }

    /// <summary>Текст для буфера: строка-заголовок и код без изменений.</summary>
    private static string FormatForClipboard(string title, string code)
        => $"{ClipboardHeader} {title}\n{code ?? string.Empty}";

    /// <summary>
    /// Разбирает текст из буфера. Первая строка «-- CodeRogue: Title» даёт заголовок (исправленный
    /// до допустимого Lua-идентификатора), остальное — код; без неё весь текст — код с заголовком Pasted.
    /// </summary>
    private static void ParseClipboard(string text, out string title, out string code)
    {
        // Буфер Windows отдаёт \r\n — в окнах кода только \n
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        int lineEnd = text.IndexOf('\n');
        string firstLine = (lineEnd < 0 ? text : text.Substring(0, lineEnd)).Trim();

        if (firstLine.StartsWith(ClipboardHeader, StringComparison.Ordinal))
        {
            title = MakeValidTitle(firstLine.Substring(ClipboardHeader.Length).Trim());
            code = lineEnd < 0 ? string.Empty : text.Substring(lineEnd + 1);
            return;
        }

        title = PastedTitle;
        code = text;
    }

    /// <summary>
    /// Делает из строки допустимый Lua-идентификатор: недопустимые символы → «_», цифра в начале
    /// или ключевое слово → префикс «_»; пустой результат → Pasted.
    /// </summary>
    private static string MakeValidTitle(string raw)
    {
        if (TitleValidator.IsValid(raw)) return raw;
        if (string.IsNullOrWhiteSpace(raw)) return PastedTitle;

        StringBuilder builder = new StringBuilder(raw.Length + 1);
        foreach (char c in raw)
            builder.Append(IsAsciiIdentifierChar(c) ? c : '_');

        string title = builder.ToString().Trim('_');
        if (title.Length == 0) return PastedTitle;
        if (!TitleValidator.IsValid(title)) title = "_" + title;
        return TitleValidator.IsValid(title) ? title : PastedTitle;
    }

    private static bool IsAsciiIdentifierChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';

    /// <summary>
    /// Короткое сообщение рядом с Paste (гаснет через StatusDurationMs) + обычное уведомление UIEventBus.
    /// </summary>
    private void ShowStatus(string message, LogEntryType type)
    {
        _status.text = message;
        _status.EnableInClassList(StatusWarningClass, type == LogEntryType.Warning || type == LogEntryType.Error);
        _status.RemoveFromClassList(HiddenClass);

        _statusHide?.Pause();
        _statusHide = _status.schedule.Execute(HideStatus).StartingIn(StatusDurationMs);

        UIEventBus.PublishNotification(message, type);
    }

    private void HideStatus() => _status.AddToClassList(HiddenClass);

    // ─── Helpers ──────────────────────────────────────────────

    private static Label CreateLabel(string text, string className)
    {
        Label label = new Label(text) { enableRichText = false };
        label.AddToClassList(className);
        return label;
    }

    private static int CountLines(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        int lines = 1;
        foreach (char c in code)
            if (c == '\n') lines++;
        return lines;
    }

    /// <summary>
    /// Первая непустая строка кода, обрезанная до PreviewLength символов.
    /// </summary>
    private static string GetPreview(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        foreach (string line in code.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.Length > PreviewLength ? trimmed.Substring(0, PreviewLength) + "…" : trimmed;
        }
        return string.Empty;
    }
}
