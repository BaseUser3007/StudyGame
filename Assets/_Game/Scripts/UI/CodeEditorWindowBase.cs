using System;
using UnityEngine;
using UnityEngine.UIElements;

public abstract class CodeEditorWindowBase : VisualElement
{
    // ─── События ──────────────────────────────────────────────
    public event Action<CodeEditorWindowBase> OnCloseRequested;
    public event Action<CodeEditorWindowBase, string> OnCodeChanged;
    public event Action<CodeEditorWindowBase, string> OnTitleChanged;
    // Перетаскивание за заголовок: позиция курсора в координатах панели
    public event Action<CodeEditorWindowBase, Vector2> OnDragMoved;
    public event Action<CodeEditorWindowBase, Vector2> OnDragEnded;

    // ─── Данные ───────────────────────────────────────────────
    public string WindowId { get; private set; }
    public string Title { get; private set; }
    public string Code => _codeEditor?.Code ?? string.Empty;

    // ─── UI элементы ──────────────────────────────────────────
    protected VisualElement _window;
    protected Label _titleLabel;
    protected Button _btnClose;

    // ─── Вспомогательные классы ───────────────────────────────
    private WindowDragger _dragger;
    private WindowResizer _resizer;
    private CodeEditor _codeEditor;

    protected CodeEditorContext _context;

    // ─── Инициализация ────────────────────────────────────────

    protected CodeEditorWindowBase(
        string windowId,
        string title,
        VisualTreeAsset template,
        CodeEditorContext context)
    {
        WindowId = windowId;
        Title = title;
        _context = context;

        BuildUI(template);
        SetupWindowButtons();
    }

    // ─── Abstract ─────────────────────────────────────────────

    protected abstract string GetWindowClass();
    protected abstract void SetupWindowButtons();

    // ─── Build UI ─────────────────────────────────────────────

    private void BuildUI(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
        _window = root.Q<VisualElement>("window");
        _titleLabel = root.Q<Label>("title");
        _btnClose = root.Q<Button>("btn-close");

        TextField codeField = root.Q<TextField>("code-field");
        Label lineNumbers = root.Q<Label>("line-numbers");
        Label lineCosts = root.Q<Label>("line-costs");
        VisualElement header = root.Q<VisualElement>("header");
        VisualElement resizeHandle = root.Q<VisualElement>("resize-handle");

        Debug.Assert(_window != null, "CodeEditorWindowBase: window not found");
        Debug.Assert(_titleLabel != null, "CodeEditorWindowBase: title not found");
        Debug.Assert(codeField != null, "CodeEditorWindowBase: code-field not found");

        _titleLabel.text = Title;
        _window.style.width = 400f;
        _window.style.height = 500f;

        _window.AddToClassList(GetWindowClass());

        // Инициализируем вспомогательные классы
        _codeEditor = new CodeEditor(codeField, lineNumbers, lineCosts, _window, _context);
        _codeEditor.OnCodeChanged += code => OnCodeChanged?.Invoke(this, code);

        if (header != null)
        {
            _dragger = new WindowDragger(_window, header);
            _dragger.OnDragMoved += position => OnDragMoved?.Invoke(this, position);
            _dragger.OnDragEnded += position => OnDragEnded?.Invoke(this, position);
        }

        if (resizeHandle != null)
            _resizer = new WindowResizer(_window, resizeHandle);

        Add(root);
    }

    // ─── Protected Helpers ────────────────────────────────────

    protected void SetTitle(string newTitle)
    {
        Title = newTitle;
        _titleLabel.text = newTitle;
        OnTitleChanged?.Invoke(this, newTitle);
    }

    // ─── Public API ───────────────────────────────────────────

    public void SetCode(string code)
    {
        _codeEditor?.SetCode(code);
    }

    public void SetPosition(float left, float top)
    {
        _window.style.left = left;
        _window.style.top = top;
    }

    /// <summary>
    /// Возвращает окно на место, откуда его начали перетаскивать.
    /// </summary>
    public void ReturnToDragStart()
    {
        _dragger?.ReturnToStart();
    }

    public void DestroyWindow()
    {
        RemoveFromHierarchy();
    }

    protected void PublishCloseRequested()
    {
        OnCloseRequested?.Invoke(this);
    }
}