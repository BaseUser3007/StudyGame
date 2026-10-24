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
    private const string CommentPrefix = "-- ";

    public event Action<string> OnCodeChanged;
    // Ctrl+Enter в поле кода — то же, что кнопка Run (во время выполнения — Stop)
    public event Action OnRunRequested;

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
    // После обработанного сочетания с Ctrl символьное событие (тоже с Ctrl) поле вставлять не должно
    private bool _swallowCtrlChar;

    // После программной смены value TextField применяет новую позицию курсора
    // с задержкой в несколько кадров. Пока поле её не подтвердило, настоящей
    // позицией считается _pendingCursor.
    private int _pendingCursor = -1;
    // Второй конец выделения, которое ждёт применения вместе с _pendingCursor; -1 — выделения нет
    private int _pendingSelect = -1;

    // Строка последней ошибки запуска (номер в колонке слева выделен цветом); 0 — нет
    private int _errorLine;

    // Строка, которая выполняется сейчас (полоса за текстом + цвет номера); 0 — нет.
    // На паузе (точка останова, шаг) полоса другого цвета
    private int _activeLine;
    private bool _activeLinePaused;
    private readonly VisualElement _activeLineBar;

    // Точки останова (номера строк); ставятся кликом по номеру строки, только где строка стоит энергию
    private readonly HashSet<int> _breakpoints = new HashSet<int>();

    private int CursorIndex => _pendingCursor >= 0 ? _pendingCursor : _codeField.cursorIndex;
    private int SelectIndex => _pendingCursor >= 0
        ? (_pendingSelect >= 0 ? _pendingSelect : _pendingCursor)
        : _codeField.selectIndex;

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

        // Полоса выполняемой строки — самый нижний слой, под подсветкой и текстом
        _activeLineBar = CreateActiveLineBar();
        _scrollView.contentContainer.Insert(0, _activeLineBar);

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

    private VisualElement CreateActiveLineBar()
    {
        VisualElement bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.style.position = Position.Absolute;
        bar.style.left = 0;
        bar.style.right = 0;
        bar.style.backgroundColor = _context.Theme != null
            ? _context.Theme.activeLineColor
            : new Color(0.95f, 0.80f, 0.20f, 0.16f);
        bar.style.display = DisplayStyle.None;
        return bar;
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
            // Код изменился — место ошибки прошлого запуска уже может быть не там
            _errorLine = 0;
            PruneBreakpoints(evt.newValue);
            Refresh();
            OnCodeChanged?.Invoke(evt.newValue);

            bool typed = (evt.newValue?.Length ?? 0) > (evt.previousValue?.Length ?? 0);
            // Позиция курсора обновится только после этого события
            _codeField.schedule.Execute(() => AfterEdit(typed));
        });

        _codeField.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);

        // Клик по колонке номеров — поставить / снять точку останова
        VisualElement numbersGutter = _lineNumbers?.parent;
        if (numbersGutter != null)
        {
            numbersGutter.tooltip = Loc.T("Click a line number to set a breakpoint: the code will pause there")
                                    + "\n" + Loc.T("Ctrl+Enter — run / stop, Ctrl+/ — comment lines, Ctrl+D — duplicate line");
            numbersGutter.RegisterCallback<PointerDownEvent>(HandleGutterPointerDown);
        }

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
            bool swallowCtrl = _swallowCtrlChar && (evt.ctrlKey || evt.commandKey);
            _swallowCtrlChar = false;
            if (swallowCtrl)
            {
                _swallowNextChar = '\0';
                evt.StopImmediatePropagation();
                return;
            }

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

        _swallowCtrlChar = false;
        bool ctrl = evt.ctrlKey || evt.commandKey;

        // Горячие клавиши — раньше окна автодополнения: Ctrl+Enter запускает код, а не принимает подсказку
        if (ctrl && !evt.altKey && HandleHotkey(evt))
            return;

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

    /// <summary>
    /// Ctrl+Enter — запуск / остановка, Ctrl+/ — комментарий строк, Ctrl+D — дубль строки.
    /// Событие поглощается, чтобы его не получили поле, навигация UI и камера.
    /// </summary>
    private bool HandleHotkey(KeyDownEvent evt)
    {
        switch (evt.keyCode)
        {
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                _popup.Hide();
                ConsumeHotkey(evt);
                OnRunRequested?.Invoke();
                return true;
            case KeyCode.Slash:
            case KeyCode.KeypadDivide:
                _popup.Hide();
                ConsumeHotkey(evt);
                ToggleLineComments();
                return true;
            case KeyCode.D:
                _popup.Hide();
                ConsumeHotkey(evt);
                DuplicateLines();
                return true;
            default:
                return false;
        }
    }

    private void ConsumeHotkey(KeyDownEvent evt)
    {
        // Символ может прийти в этом же событии или следующим (с тем же Ctrl) — тогда его тоже съедаем
        _swallowNextChar = '\0';
        _swallowCtrlChar = evt.character == '\0';
        evt.StopImmediatePropagation();
        _codeField.focusController?.IgnoreEvent(evt);
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

    private void ReplaceRange(int start, int end, string text, int newCursor, int newSelect = -1)
    {
        string code = Code;
        // Выделение нулевой длины — просто курсор
        if (newSelect == newCursor) newSelect = -1;
        _pendingCursor = newCursor;
        _pendingSelect = newSelect;
        _codeField.value = code.Substring(0, start) + text + code.Substring(end);
        _codeField.SelectRange(newCursor, newSelect >= 0 ? newSelect : newCursor);
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
        int select = _pendingSelect >= 0 ? Mathf.Clamp(_pendingSelect, 0, Code.Length) : _pendingCursor;
        if (_codeField.cursorIndex == _pendingCursor && _codeField.selectIndex == select)
        {
            _pendingCursor = -1;
            _pendingSelect = -1;
            return;
        }

        _codeField.SelectRange(_pendingCursor, select);
        _codeField.schedule.Execute(ConfirmPendingCursor);
    }

    // ─── Line Commands ────────────────────────────────────────

    /// <summary>
    /// Границы строк, которых касается курсор или выделение: [blockStart, blockEnd), без завершающего '\n'.
    /// Если выделение кончается в самом начале строки, эта строка не считается выделенной.
    /// </summary>
    private void GetSelectedLines(string code, out int blockStart, out int blockEnd)
    {
        int start = Mathf.Clamp(Mathf.Min(CursorIndex, SelectIndex), 0, code.Length);
        int end = Mathf.Clamp(Mathf.Max(CursorIndex, SelectIndex), 0, code.Length);
        if (end > start && code[end - 1] == '\n') end--;

        blockStart = start == 0 ? 0 : code.LastIndexOf('\n', start - 1) + 1;
        blockEnd = code.IndexOf('\n', end);
        if (blockEnd < 0) blockEnd = code.Length;
    }

    /// <summary>
    /// Ctrl+/: если все непустые строки уже начинаются с «--» — снимает комментарий,
    /// иначе ставит «-- » на одном отступе (наименьшем среди строк). Выделение сохраняется.
    /// </summary>
    private void ToggleLineComments()
    {
        string code = Code;
        GetSelectedLines(code, out int blockStart, out int blockEnd);
        string[] lines = code.Substring(blockStart, blockEnd - blockStart).Split('\n');

        bool anyText = false;
        bool allCommented = true;
        int minIndent = int.MaxValue;
        foreach (string line in lines)
        {
            int indent = GetIndentLength(line);
            if (indent == line.Length) continue;
            anyText = true;
            minIndent = Mathf.Min(minIndent, indent);
            if (string.CompareOrdinal(line, indent, "--", 0, 2) != 0) allCommented = false;
        }
        bool uncomment = anyText && allCommented;

        // Правки по строкам (по возрастанию позиции): позиция в исходном коде, сколько убрать, что вставить
        var edits = new List<(int Pos, int Remove, string Insert)>();
        var sb = new StringBuilder();
        int lineStart = blockStart;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int indent = GetIndentLength(line);
            bool blank = indent == line.Length;

            if (uncomment && !blank)
            {
                int remove = string.CompareOrdinal(line, indent, CommentPrefix, 0, CommentPrefix.Length) == 0
                    ? CommentPrefix.Length
                    : 2;
                edits.Add((lineStart + indent, remove, string.Empty));
                sb.Append(line, 0, indent).Append(line, indent + remove, line.Length - indent - remove);
            }
            else if (!uncomment && (!blank || !anyText))
            {
                // Пустые строки комментируем, только если выбраны одни пустые строки
                int at = anyText ? minIndent : line.Length;
                edits.Add((lineStart + at, 0, CommentPrefix));
                sb.Append(line, 0, at).Append(CommentPrefix).Append(line, at, line.Length - at);
            }
            else
            {
                sb.Append(line);
            }

            if (i < lines.Length - 1) sb.Append('\n');
            lineStart += line.Length + 1;
        }

        if (edits.Count == 0) return;

        int cursor = MapIndexThroughEdits(Mathf.Clamp(CursorIndex, 0, code.Length), edits);
        int select = MapIndexThroughEdits(Mathf.Clamp(SelectIndex, 0, code.Length), edits);
        ReplaceRange(blockStart, blockEnd, sb.ToString(), cursor, select);
    }

    /// <summary>Позиция в коде после правок (правки идут по возрастанию позиции и не пересекаются).</summary>
    private static int MapIndexThroughEdits(int index, List<(int Pos, int Remove, string Insert)> edits)
    {
        int shift = 0;
        foreach ((int pos, int remove, string insert) in edits)
        {
            if (index < pos) break;
            if (remove > 0)
            {
                // Курсор внутри удалённого «-- » встаёт туда, где начинался комментарий
                if (index < pos + remove) return pos + shift;
                shift -= remove;
            }
            else
            {
                shift += insert.Length;
            }
        }
        return index + shift;
    }

    /// <summary>Ctrl+D: копия текущей строки (или всех выделенных) вставляется ниже, курсор и выделение — в копии.</summary>
    private void DuplicateLines()
    {
        string code = Code;
        GetSelectedLines(code, out int blockStart, out int blockEnd);
        string block = code.Substring(blockStart, blockEnd - blockStart);

        int offset = block.Length + 1;
        int cursor = Mathf.Clamp(CursorIndex, 0, code.Length) + offset;
        int select = Mathf.Clamp(SelectIndex, 0, code.Length) + offset;
        ReplaceRange(blockEnd, blockEnd, "\n" + block, cursor, select);
    }

    private static int GetIndentLength(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
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
        RemoveLockedKeywords(items);

        if (items.Count == 0)
        {
            _popup.Hide();
            return;
        }

        _popup.Show(items, GetPopupPosition(CursorIndex));
    }

    /// <summary>
    /// Ключевые слова и шаблоны закрытых конструкций не предлагаем: шаблон убирается, если любое слово
    /// в его названии или вставляемом тексте закрыто (например, «local function» при закрытом function).
    /// </summary>
    private void RemoveLockedKeywords(List<CompletionItem> items)
    {
        if (_context.IsKeywordLocked == null) return;

        items.RemoveAll(item =>
        {
            if (item.Kind != CompletionKind.Keyword && item.Kind != CompletionKind.Snippet) return false;
            return HasLockedWord(item.Label) || HasLockedWord(item.InsertText);
        });
    }

    private bool HasLockedWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]) && text[i] != '_')
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i++;
            if (_context.IsKeywordLocked(text.Substring(start, i - start))) return true;
        }

        return false;
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
            string errorColor = ColorUtility.ToHtmlStringRGB(
                _context.Theme != null ? _context.Theme.logColorError : Color.red);
            string activeColor = ColorUtility.ToHtmlStringRGB(
                _context.Theme != null ? _context.Theme.activeLineNumberColor : Color.yellow);
            string breakpointColor = ColorUtility.ToHtmlStringRGB(
                _context.Theme != null ? _context.Theme.breakpointColor : Color.red);
            for (int i = 1; i <= lineCount; i++)
            {
                if (_breakpoints.Contains(i))
                    sb.Append("<color=#").Append(breakpointColor).Append(">●</color>");
                // В колонке только цифры и теги цвета — номер строки с ошибкой (или выполняемой) выделен
                if (i == _errorLine)
                    sb.Append("<color=#").Append(errorColor).Append('>').Append(i).Append("</color>");
                else if (i == _activeLine)
                    sb.Append("<color=#").Append(activeColor).Append('>').Append(i).Append("</color>");
                else
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
        PositionActiveLineBar();
    }

    /// <summary>
    /// Полоса выполняемой строки: высота строки — разница высот текста из двух строк и из одной
    /// (тем же шрифтом, что и поле), сверху — отступ текстового элемента.
    /// </summary>
    private void PositionActiveLineBar()
    {
        if (_activeLineBar == null) return;
        if (_activeLine <= 0)
        {
            _activeLineBar.style.display = DisplayStyle.None;
            return;
        }

        float lineHeight = GetLineHeight();
        if (lineHeight <= 0f)
        {
            _activeLineBar.style.display = DisplayStyle.None;
            return;
        }

        IResolvedStyle s = _textElement.resolvedStyle;
        float top = _textElement.layout.y + s.borderTopWidth + s.paddingTop + (_activeLine - 1) * lineHeight;
        _activeLineBar.style.top = top;
        _activeLineBar.style.height = lineHeight;
        _activeLineBar.style.backgroundColor = _context.Theme == null
            ? new Color(0.95f, 0.80f, 0.20f, 0.16f)
            : _activeLinePaused ? _context.Theme.pausedLineColor : _context.Theme.activeLineColor;
        _activeLineBar.style.display = DisplayStyle.Flex;
    }

    /// <summary>Высота строки текста: разница высот текста из двух строк и из одной; 0 — ещё не измерить.</summary>
    private float GetLineHeight()
    {
        float oneLine = _textElement.MeasureTextSize("X", 0, VisualElement.MeasureMode.Undefined, 0,
            VisualElement.MeasureMode.Undefined).y;
        float twoLines = _textElement.MeasureTextSize("X\nX", 0, VisualElement.MeasureMode.Undefined, 0,
            VisualElement.MeasureMode.Undefined).y;
        float lineHeight = twoLines - oneLine;
        return float.IsNaN(lineHeight) || lineHeight <= 0f ? 0f : lineHeight;
    }

    // ─── Breakpoints ──────────────────────────────────────────

    private void HandleGutterPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0) return;
        float lineHeight = GetLineHeight();
        if (lineHeight <= 0f) return;

        // worldBound текста уже учитывает прокрутку (как у выравнивания колонок)
        float textTop = _textElement.worldBound.yMin + _textElement.resolvedStyle.borderTopWidth
                        + _textElement.resolvedStyle.paddingTop;
        int line = Mathf.FloorToInt((evt.position.y - textTop) / lineHeight) + 1;
        ToggleBreakpoint(line);
        evt.StopPropagation();
    }

    private void ToggleBreakpoint(int line)
    {
        string code = Code;
        if (line < 1 || line > CountLines(code)) return;

        if (!_breakpoints.Remove(line))
        {
            // Остановиться можно только там, где код платит энергию (там же, где цена строки в колонке справа)
            if (!_context.LineCosts(code).TryGetValue(line, out float cost) || cost <= 0f) return;
            _breakpoints.Add(line);
        }
        UpdateGutters(code);
    }

    private void PruneBreakpoints(string code)
    {
        if (_breakpoints.Count == 0) return;
        // Строки сдвигаются при правке: точка, оказавшаяся на строке без цены, никогда не сработает — снимаем
        Dictionary<int, float> costs = _context.LineCosts(code);
        _breakpoints.RemoveWhere(line => !costs.TryGetValue(line, out float cost) || cost <= 0f);
    }

    private static int CountLines(string code)
    {
        int lines = 1;
        if (code == null) return lines;
        foreach (char c in code)
            if (c == '\n') lines++;
        return lines;
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

    /// <summary>
    /// Перерисовывает подсветку и цены строк без изменения кода и курсора
    /// (например, после открытия команды или конструкции).
    /// </summary>
    public void RefreshView()
    {
        if (_codeField == null) return;
        Refresh();
    }

    /// <summary>
    /// Выделяет номер строки с ошибкой последнего запуска (0 — снять). Снимается сам при правке кода.
    /// </summary>
    public void SetErrorLine(int line)
    {
        if (_codeField == null) return;
        _errorLine = Mathf.Max(0, line);
        UpdateGutters(Code);
    }

    /// <summary>
    /// Выделяет строку, которая выполняется сейчас (0 — снять): полоса за текстом и цвет номера.
    /// </summary>
    public void SetActiveLine(int line, bool paused = false)
    {
        if (_codeField == null) return;
        line = Mathf.Max(0, line);
        if (line == _activeLine && paused == _activeLinePaused) return;
        _activeLine = line;
        _activeLinePaused = paused && line > 0;
        UpdateGutters(Code);
        PositionActiveLineBar();
    }

    /// <summary>Стоит ли точка останова на строке.</summary>
    public bool HasBreakpoint(int line) => _breakpoints.Contains(line);
}
