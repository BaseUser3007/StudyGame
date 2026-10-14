using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Скрытая боковая панель сохранённого кода. Открывается ползунком посередине правого края.
/// Окно кода, перетащенное за заголовок на панель (или на ползунок), сохраняется в библиотеку —
/// саму проверку попадания и сохранение делает CodeEditorManager.
/// </summary>
public class CodeLibraryPanel : VisualElement
{
    private const string HiddenClass = "hidden";
    private const string DropTargetClass = "library-drop-target";
    private const string ConfirmClass = "snippet-btn--confirm";
    private const int PreviewLength = 60;

    private readonly CodeLibrary _library;

    private Button _btnToggle;
    private VisualElement _panel;
    private ScrollView _snippetList;
    private Label _listEmpty;

    public event Action OnOpened;
    public event Action<CodeSnippet> OnOpenSnippetRequested;

    public bool IsOpen => !_panel.ClassListContains(HiddenClass);

    public CodeLibraryPanel(VisualTreeAsset template, CodeLibrary library)
    {
        _library = library;

        VisualElement root = template.Instantiate();
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

        _btnToggle.clicked += Toggle;
        btnClose.clicked += Close;

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
    }

    private void ValidateReferences()
    {
        Debug.Assert(_library != null, "CodeLibraryPanel: library is null");
        Debug.Assert(_btnToggle != null, "CodeLibraryPanel: btn-library-toggle not found");
        Debug.Assert(_panel != null, "CodeLibraryPanel: library-panel not found");
        Debug.Assert(_snippetList != null, "CodeLibraryPanel: snippet-list not found");
        Debug.Assert(_listEmpty != null, "CodeLibraryPanel: list-empty not found");
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
        info.Add(CreateLabel($"{CountLines(snippet.code)} стр. · {snippet.savedAt}", "snippet-meta"));
        info.Add(CreateLabel(GetPreview(snippet.code), "snippet-preview"));
        entry.Add(info);

        Button btnOpen = new Button(() => OnOpenSnippetRequested?.Invoke(snippet)) { text = "Открыть" };
        btnOpen.AddToClassList("snippet-btn");
        btnOpen.focusable = false;
        btnOpen.tooltip = "Открыть в новом окне";
        entry.Add(btnOpen);

        // Удаление в два нажатия: «×» → «Удалить?» → удалено
        Button btnDelete = new Button { text = "×" };
        btnDelete.AddToClassList("snippet-btn");
        btnDelete.focusable = false;
        btnDelete.tooltip = "Удалить из библиотеки";
        btnDelete.clicked += () =>
        {
            if (btnDelete.ClassListContains(ConfirmClass))
            {
                _library.Remove(snippet);
                return;
            }
            btnDelete.AddToClassList(ConfirmClass);
            btnDelete.text = "Удалить?";
        };
        entry.Add(btnDelete);

        return entry;
    }

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
