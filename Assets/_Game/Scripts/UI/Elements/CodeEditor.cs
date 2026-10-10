using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Поле ввода Lua кода: подсветка синтаксиса, номера строк,
/// стоимость каждой строки и автодополнение.
///
/// Подсветка — это Label с rich text, лежащий под прозрачным текстом
/// TextField в том же контейнере прокрутки, поэтому он прокручивается вместе с текстом.
/// </summary>
public class CodeEditor
{
    private const string Indent = "    ";

    public event Action<string> OnCodeChanged;

    private readonly TextField _codeField;
    private readonly Label _lineNumbers;
    private readonly Label _lineCosts;
    private readonly CodeEditorContext _context;

    private readonly ScrollView _scrollView;
    private readonly TextElement _textElement;
    private readonly Label _highlight;
    private readonly CompletionPopup _popup;

    private bool _focusedByMouse;
    private char _swallowNextChar;

    // После программной смены value TextField применяет новую позицию курсора
    // с задержкой в несколько кадров. Пока поле её не подтвердило, настоящей
    // позицией считается _pendingCursor.
    private int _pendingCursor = -1;

    private int CursorIndex => _pendingCursor >= 0 ? _pendingCursor : _codeField.cursorIndex;
    private int SelectIndex => _pendingCursor >= 0 ? _pendingCursor : _codeField.selectIndex;

    public string Code => _codeField?.value ?? string.Empty;

    public CodeEditor(
        TextField codeField,
        Label lineNumbers,
        Label lineCosts,
        VisualElement popupHost,
        CodeEditorContext context)
    {
        _codeField = codeField;
        _lineNumbers = lineNumbers;
        _lineCosts = lineCosts;
        _context = context;

        // Собственный ScrollView поля — в него кладём слой подсветки
        _codeField.verticalScrollerVisibility = ScrollerVisibility.Auto;
        _scrollView = _codeField.Q<ScrollView>();
        _textElement = _scrollView.contentContainer.Q<TextElement>();

        _highlight = CreateHighlightLayer();
        _scrollView.contentContainer.Insert(0, _highlight);

        _popup = new CompletionPopup();
        _popup.OnItemChosen += item =>
        {
            AcceptCompletion(item);
            RestoreFocus();
        };
        popupHost.Add(_popup);

        ConfigureTextField();
        RegisterCallbacks();
        Refresh();
    }

    // ─── Setup ────────────────────────────────────────────────

    private Label CreateHighlightLayer()
    {
        Label label = new Label
        {
            pickingMode = PickingMode.Ignore,
            enableRichText = true,
            parseEscapeSequences = false
        };
        label.AddToClassList("code-highlight");
        label.style.position = Position.Absolute;
        return label;
    }

    private void ConfigureTextField()
    {
        _textElement.enableRichText = false;
        _textElement.parseEscapeSequences = false;

        // Текст поля прозрачный — виден слой подсветки, а курсор и выделение
        // рисует само поле (цвета — в CodeEditorCommon.uss)
        _textElement.style.color = new Color(0f, 0f, 0f, 0f);

        // По умолчанию TextField выделяет весь текст при получении фокуса —
        // одно нажатие клавиши после этого стирает весь код
        _codeField.selectAllOnFocus = false;
        _codeField.selectAllOnMouseUp = false;
    }

    // ─── Callbacks ────────────────────────────────────────────

    private void RegisterCallbacks()
    {
        _codeField.RegisterValueChangedCallback(evt =>
        {
            Refresh();
            OnCodeChanged?.Invoke(evt.newValue);

            bool typed = (evt.newValue?.Length ?? 0) > (evt.previousValue?.Length ?? 0);
            // Позиция курсора обновится только после этого события
            _codeField.schedule.Execute(() => AfterEdit(typed));
        });

        _codeField.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);

        // Tab не должен переводить фокус на другой элемент
        _codeField.RegisterCallback<NavigationMoveEvent>(evt =>
        {
            if (evt.direction == NavigationMoveEvent.Direction.Next ||
                evt.direction == NavigationMoveEvent.Direction.Previous)
            {
                _codeField.focusController?.IgnoreEvent(evt);
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);

        // PointerDown приходит до смены фокуса (Click — уже после),
        // поэтому именно здесь отмечаем, что фокус получен мышью
        _codeField.RegisterCallback<PointerDownEvent>(_ =>
        {
            _focusedByMouse = true;
            _pendingCursor = -1;
            _popup.Hide();
        }, TrickleDown.TrickleDown);

        _codeField.RegisterCallback<FocusInEvent>(_ =>
        {
            if (!_focusedByMouse)
            {
                // Фокус пришёл не от мыши (клавиатурная навигация) — не принимаем его.
                // Blur внутри FocusIn может не сработать, поэтому повторяем в следующем кадре.
                _codeField.Blur();
                _codeField.schedule.Execute(() =>
                {
                    if (!_focusedByMouse) _codeField.Blur();
                });
                return;
            }
            PlayerEventBus.PublishUIFocused();
        });

        _codeField.RegisterCallback<FocusOutEvent>(_ =>
        {
            _focusedByMouse = false;
            _popup.Hide();
            PlayerEventBus.PublishUIUnfocused();
        });

        // Слой подсветки и колонки слева/справа следуют за текстом
        _textElement.RegisterCallback<GeometryChangedEvent>(_ => SyncLayout());
        _scrollView.verticalScroller.valueChanged += _ => SyncGutters();
        _lineNumbers?.parent?.RegisterCallback<GeometryChangedEvent>(_ => SyncGutters());
    }

    // ─── Keyboard ─────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent evt)
    {
        // Символьное событие, которое следует за уже обработанной клавишей
        if (evt.keyCode == KeyCode.None && evt.character != '\0')
        {
            char expected = _swallowNextChar;
            _swallowNextChar = '\0';
            if (expected != '\0' && (evt.character == expected ||
                (expected == '\n' && evt.character == '\r')))
            {
                evt.StopImmediatePropagation();
                return;
            }

            // Поле ещё не применило позицию курсора — вставляем символ сами, иначе он уйдёт не туда
            if (_pendingCursor >= 0 && !char.IsControl(evt.character))
            {
                InsertText(evt.character.ToString());
                evt.StopImmediatePropagation();
            }
            return;
        }

        bool ctrl = evt.ctrlKey || evt.commandKey;

        if (_popup.IsOpen)
        {
            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    _popup.MoveSelection(-1);
                    Consume(evt);
                    return;
                case KeyCode.DownArrow:
                    _popup.MoveSelection(1);
                    Consume(evt);
                    return;
                case KeyCode.Tab:
                    AcceptCompletion(_popup.SelectedItem);
                    Consume(evt, '\t');
                    return;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    AcceptCompletion(_popup.SelectedItem);
                    Consume(evt, '\n');
                    return;
                case KeyCode.Escape:
                    _popup.Hide();
                    Consume(evt);
                    return;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    _popup.Hide();
                    _pendingCursor = -1;
                    return;
            }
        }

        if (IsNavigationKey(evt.keyCode))
            _pendingCursor = -1;

        if (ctrl && evt.keyCode == KeyCode.Space)
        {
            Consume(evt, ' ');
            UpdateCompletion(typed: true, forced: true);
            return;
        }

        if (evt.keyCode == KeyCode.Tab && !evt.shiftKey)
        {
            InsertText(Indent);
            Consume(evt, '\t');
            return;
        }

        if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !ctrl)
        {
            InsertText("\n" + GetIndentForNewLine());
            Consume(evt, '\n');
        }
    }

    private static bool IsNavigationKey(KeyCode key)
    {
        return key == KeyCode.LeftArrow || key == KeyCode.RightArrow ||
               key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
               key == KeyCode.Home || key == KeyCode.End ||
               key == KeyCode.PageUp || key == KeyCode.PageDown;
    }

    private void Consume(KeyDownEvent evt, char followingChar = '\0')
    {
        // Если клавиша несёт символ в том же событии — следующего символьного события не будет
        _swallowNextChar = evt.character == '\0' ? followingChar : '\0';
        evt.StopImmediatePropagation();
    }

    // ─── Editing ──────────────────────────────────────────────

    private void InsertText(string text)
    {
        string code = Code;
        int start = Mathf.Min(CursorIndex, SelectIndex);
        int end = Mathf.Max(CursorIndex, SelectIndex);
        start = Mathf.Clamp(start, 0, code.Length);
        end = Mathf.Clamp(end, 0, code.Length);

        ReplaceRange(start, end, text, start + text.Length);
    }

    private void ReplaceRange(int start, int end, string text, int newCursor)
    {
        string code = Code;
        _pendingCursor = newCursor;
        _codeField.value = code.Substring(0, start) + text + code.Substring(end);
        _codeField.SelectRange(newCursor, newCursor);
    }

    private void AfterEdit(bool typed)
    {
        ConfirmPendingCursor();
        UpdateCompletion(typed, forced: false);
    }

    private void ConfirmPendingCursor()
    {
        if (_pendingCursor < 0) return;

        _pendingCursor = Mathf.Clamp(_pendingCursor, 0, Code.Length);
        if (_codeField.cursorIndex == _pendingCursor && _codeField.selectIndex == _pendingCursor)
        {
            _pendingCursor = -1;
            return;
        }

        _codeField.SelectRange(_pendingCursor, _pendingCursor);
        _codeField.schedule.Execute(ConfirmPendingCursor);
    }

    /// <summary>
    /// Отступ новой строки: как у текущей, плюс уровень после do/then/function/repeat/else.
    /// </summary>
    private string GetIndentForNewLine()
    {
        string code = Code;
        int cursor = Mathf.Clamp(CursorIndex, 0, code.Length);
        int lineStart = code.LastIndexOf('\n', Mathf.Max(0, cursor - 1)) + 1;
        if (cursor == 0) lineStart = 0;

        string line = code.Substring(lineStart, cursor - lineStart);
        string indent = line.Substring(0, line.Length - line.TrimStart(' ', '\t').Length);

        string trimmed = line.Trim();
        if (trimmed.EndsWith(" do") || trimmed == "do" ||
            trimmed.EndsWith(" then") ||
            trimmed == "repeat" || trimmed == "else" ||
            (trimmed.StartsWith("function") || trimmed.Contains("= function") || trimmed.StartsWith("local function"))
                && trimmed.EndsWith(")"))
            indent += Indent;

        return indent;
    }

    // ─── Completion ───────────────────────────────────────────

    private void UpdateCompletion(bool typed, bool forced)
    {
        if (_codeField.focusController?.focusedElement == null && !forced)
        {
            _popup.Hide();
            return;
        }

        if (!typed && !_popup.IsOpen) return;

        List<CompletionItem> items = _context.Completion.GetCompletions(
            Code, CursorIndex, forced);

        if (items.Count == 0)
        {
            _popup.Hide();
            return;
        }

        _popup.Show(items, GetPopupPosition(CursorIndex));
    }

    private Vector2 GetPopupPosition(int cursorIndex)
    {
        // Считаем сами: ширина текста строки до курсора и номер строки.
        // worldBound текстового элемента уже учитывает прокрутку.
        string code = Code;
        cursorIndex = Mathf.Clamp(cursorIndex, 0, code.Length);
        int lineStart = cursorIndex == 0 ? 0 : code.LastIndexOf('\n', cursorIndex - 1) + 1;
        int lineIndex = 0;
        for (int i = 0; i < lineStart; i++)
            if (code[i] == '\n') lineIndex++;

        string beforeCaret = code.Substring(lineStart, cursorIndex - lineStart);
        float x = beforeCaret.Length == 0 ? 0f : MeasureText(beforeCaret).x;
        float lineHeight = MeasureText("Ag").y;

        Rect text = _textElement.worldBound;
        IResolvedStyle s = _textElement.resolvedStyle;
        Vector2 world = new Vector2(
            text.x + s.paddingLeft + x,
            text.y + s.paddingTop + (lineIndex + 1) * lineHeight + 2f);

        return _popup.parent.WorldToLocal(world);
    }

    private Vector2 MeasureText(string text)
    {
        return _textElement.MeasureTextSize(
            text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
    }

    private void AcceptCompletion(CompletionItem item)
    {
        _popup.Hide();
        if (item == null) return;

        string code = Code;
        int cursor = Mathf.Clamp(CursorIndex, 0, code.Length);
        LuaCompletionProvider.GetPrefix(code, cursor, out int start);

        // Многострочные шаблоны получают отступ текущей строки
        int lineStart = code.LastIndexOf('\n', Mathf.Max(0, start - 1)) + 1;
        if (start == 0) lineStart = 0;
        string line = code.Substring(lineStart, start - lineStart);
        string indent = line.Substring(0, line.Length - line.TrimStart(' ', '\t').Length);

        string text = item.InsertText.Replace("\n", "\n" + indent);
        int caretOffset = text.IndexOf("$0", StringComparison.Ordinal);
        text = text.Replace("$0", string.Empty);
        if (caretOffset < 0) caretOffset = text.Length;

        ReplaceRange(start, cursor, text, start + caretOffset);
    }

    private void RestoreFocus()
    {
        _codeField.schedule.Execute(() =>
        {
            if (_codeField.focusController?.focusedElement == _codeField) return;
            int cursor = CursorIndex;
            _focusedByMouse = true;
            _codeField.Focus();
            _codeField.SelectRange(cursor, cursor);
        });
    }

    // ─── Rendering ────────────────────────────────────────────

    private void Refresh()
    {
        string code = Code;
        _highlight.text = _context.Highlighter.Highlight(code);
        UpdateGutters(code);
    }

    private void UpdateGutters(string code)
    {
        int lineCount = 1;
        foreach (char c in code)
            if (c == '\n') lineCount++;

        if (_lineNumbers != null)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                sb.Append(i);
                if (i < lineCount) sb.Append('\n');
            }
            _lineNumbers.text = sb.ToString();
        }

        if (_lineCosts != null)
        {
            Dictionary<int, float> costs = _context.LineCosts(code);
            var sb = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                if (costs.TryGetValue(i, out float cost) && cost > 0f)
                    sb.Append(cost.ToString("0.#"));
                else
                    sb.Append(' ');
                if (i < lineCount) sb.Append('\n');
            }
            _lineCosts.text = sb.ToString();
        }
    }

    private void SyncLayout()
    {
        Rect r = _textElement.layout;
        IResolvedStyle s = _textElement.resolvedStyle;

        _highlight.style.left = r.x;
        _highlight.style.top = r.y;
        _highlight.style.width = r.width;
        _highlight.style.height = r.height;

        _highlight.style.marginLeft = 0;
        _highlight.style.marginTop = 0;
        _highlight.style.marginRight = 0;
        _highlight.style.marginBottom = 0;
        _highlight.style.paddingLeft = s.paddingLeft;
        _highlight.style.paddingTop = s.paddingTop;
        _highlight.style.paddingRight = s.paddingRight;
        _highlight.style.paddingBottom = s.paddingBottom;
        _highlight.style.borderLeftWidth = s.borderLeftWidth;
        _highlight.style.borderTopWidth = s.borderTopWidth;

        _highlight.style.fontSize = s.fontSize;
        _highlight.style.unityFontDefinition = s.unityFontDefinition;
        _highlight.style.unityFontStyleAndWeight = s.unityFontStyleAndWeight;
        _highlight.style.letterSpacing = s.letterSpacing;
        _highlight.style.wordSpacing = s.wordSpacing;
        _highlight.style.unityParagraphSpacing = s.unityParagraphSpacing;
        _highlight.style.unityTextAlign = s.unityTextAlign;
        _highlight.style.whiteSpace = s.whiteSpace;

        SyncGutters();
    }

    /// <summary>
    /// Колонки номеров и стоимости выравниваются по первой строке текста —
    /// worldBound текстового элемента уже учитывает прокрутку.
    /// </summary>
    private void SyncGutters()
    {
        AlignGutter(_lineNumbers);
        AlignGutter(_lineCosts);
    }

    private void AlignGutter(Label gutter)
    {
        if (gutter?.parent == null) return;

        float textTop = _textElement.worldBound.yMin + _textElement.resolvedStyle.paddingTop;
        float containerTop = gutter.parent.worldBound.yMin;
        gutter.style.top = textTop - containerTop - gutter.resolvedStyle.paddingTop;
    }

    // ─── Public API ───────────────────────────────────────────

    public void SetCode(string code)
    {
        if (_codeField == null) return;
        _codeField.value = code;
        Refresh();
    }
}
