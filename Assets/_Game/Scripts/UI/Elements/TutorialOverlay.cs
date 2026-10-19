using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Карточка подсказки туториала, привязанная к элементу UI: рамка вокруг элемента, стрелка от карточки к нему.
/// Позиция пересчитывается по worldBound элемента на каждом UpdateLayout (элементы двигаются — окна кода).
/// Не модальная: слой, рамка и стрелка не ловят мышь (picking Ignore), только сама карточка;
/// кнопки не фокусируемые — клавиатура остаётся у поля кода/камеры.
/// Элементы — экземпляр шаблона TutorialOverlay.uxml (хост выдаёт RunScreens.GetOverlay). Логика — TutorialController.
/// </summary>
public class TutorialOverlay
{
    private const string HiddenClass = "hidden";
    private const string PulseClass = "tutorial-frame--pulse";
    private const float Margin = 8f;
    private const float Gap = 14f;
    private const float FramePadding = 4f;
    private const float ArrowSize = 14f;

    private enum Side { Below, Above, Right, Left }

    /// <summary>Что показать в карточке.</summary>
    public struct Content
    {
        public string Caption;
        /// <summary>Заголовок (понятия); пусто — без заголовка.</summary>
        public string Title;
        /// <summary>Текст; `код` в обратных кавычках подсвечивается.</summary>
        public string Text;
        /// <summary>Пример кода отдельным блоком; пусто — без него.</summary>
        public string Example;
        /// <summary>Строка «чего ждём» (для шагов с условием).</summary>
        public string Waiting;
        public bool ShowNext;
        public string NextText;
        public bool ShowSkip;
        public bool ShowHintsOff;
    }

    public event Action OnNextClicked;
    public event Action OnSkipClicked;
    public event Action OnHintsOffClicked;

    private readonly LuaSyntaxHighlighter _highlighter;

    private readonly VisualElement _layer;
    private readonly VisualElement _frame;
    private readonly VisualElement _arrow;
    private readonly VisualElement _card;
    private readonly Label _caption;
    private readonly Label _title;
    private readonly Label _text;
    private readonly Label _example;
    private readonly Label _waiting;
    private readonly Button _btnNext;
    private readonly Button _btnSkip;
    private readonly Button _btnHintsOff;

    private VisualElement _target;

    /// <summary>Элемент для планировщика (schedule) контроллера.</summary>
    public VisualElement Root => _layer;
    public bool IsVisible => !_card.ClassListContains(HiddenClass);

    /// <param name="root">Хост шаблона TutorialOverlay.uxml.</param>
    public TutorialOverlay(VisualElement root, LuaSyntaxHighlighter highlighter)
    {
        _highlighter = highlighter;

        _layer = root.Q<VisualElement>("tutorial-layer");
        _frame = root.Q<VisualElement>("tutorial-frame");
        _arrow = root.Q<VisualElement>("tutorial-arrow");
        _card = root.Q<VisualElement>("tutorial-card");
        _caption = root.Q<Label>("tutorial-caption");
        _title = root.Q<Label>("tutorial-title");
        _text = root.Q<Label>("tutorial-text");
        _example = root.Q<Label>("tutorial-example");
        _waiting = root.Q<Label>("tutorial-waiting");
        _btnNext = root.Q<Button>("btn-tutorial-next");
        _btnSkip = root.Q<Button>("btn-tutorial-skip");
        _btnHintsOff = root.Q<Button>("btn-tutorial-hints-off");

        Debug.Assert(_layer != null && _frame != null && _arrow != null && _card != null && _caption != null
                     && _title != null && _text != null && _example != null && _waiting != null
                     && _btnNext != null && _btnSkip != null && _btnHintsOff != null,
            "TutorialOverlay: elements not found");

        // Не мешаем остальному UI и карте: мышь ловит только карточка
        _layer.pickingMode = PickingMode.Ignore;
        _frame.pickingMode = PickingMode.Ignore;
        _arrow.pickingMode = PickingMode.Ignore;

        _caption.enableRichText = false;
        _title.enableRichText = false;
        _waiting.enableRichText = false;

        // UI управляется только мышью — кнопки не должны забирать фокус
        foreach (Button button in new[] { _btnNext, _btnSkip, _btnHintsOff })
            button.focusable = false;

        _btnNext.clicked += () => OnNextClicked?.Invoke();
        _btnSkip.clicked += () => OnSkipClicked?.Invoke();
        _btnHintsOff.clicked += () => OnHintsOffClicked?.Invoke();

        // Рамка мягко мигает, пока видна
        _frame.schedule.Execute(() => _frame.ToggleInClassList(PulseClass)).Every(700);
    }

    // ─── Public API ───────────────────────────────────────────

    public void Show(Content content)
    {
        _caption.text = content.Caption ?? string.Empty;

        _title.text = content.Title ?? string.Empty;
        _title.EnableInClassList(HiddenClass, string.IsNullOrEmpty(content.Title));

        SetRichText(_text, FormatInline(content.Text));

        bool hasExample = !string.IsNullOrEmpty(content.Example);
        _example.EnableInClassList(HiddenClass, !hasExample);
        if (hasExample) SetRichText(_example, Highlight(content.Example));

        _waiting.text = content.Waiting ?? string.Empty;
        _waiting.EnableInClassList(HiddenClass, string.IsNullOrEmpty(content.Waiting));

        _btnNext.text = string.IsNullOrEmpty(content.NextText) ? "Next" : content.NextText;
        _btnNext.EnableInClassList(HiddenClass, !content.ShowNext);
        _btnSkip.EnableInClassList(HiddenClass, !content.ShowSkip);
        _btnHintsOff.EnableInClassList(HiddenClass, !content.ShowHintsOff);

        _card.RemoveFromClassList(HiddenClass);
        // До первой раскладки размер карточки неизвестен — показываем, когда UpdateLayout её поставит
        _card.style.visibility = Visibility.Hidden;
        _arrow.AddToClassList(HiddenClass);
        _frame.AddToClassList(HiddenClass);
    }

    public void Hide()
    {
        _card.AddToClassList(HiddenClass);
        _arrow.AddToClassList(HiddenClass);
        _frame.AddToClassList(HiddenClass);
        _target = null;
    }

    /// <summary>Элемент, на который указывает подсказка; null или невидимый — карточка по центру сверху.</summary>
    public void SetTarget(VisualElement target) => _target = target;

    /// <summary>
    /// Ставит рамку вокруг элемента, карточку — с той стороны, где она помещается, и стрелку к элементу.
    /// </summary>
    public void UpdateLayout()
    {
        if (!IsVisible) return;

        Rect bounds = _layer.contentRect;
        Vector2 cardSize = new Vector2(_card.layout.width, _card.layout.height);
        if (bounds.width <= 0f || float.IsNaN(cardSize.x) || cardSize.x <= 0f || cardSize.y <= 0f) return;

        if (!TryGetTargetRect(out Rect target))
        {
            _frame.AddToClassList(HiddenClass);
            _arrow.AddToClassList(HiddenClass);
            SetPosition(_card, new Vector2((bounds.width - cardSize.x) * 0.5f, 60f));
            _card.style.visibility = Visibility.Visible;
            return;
        }

        // Рамка
        Rect frame = new Rect(
            target.x - FramePadding, target.y - FramePadding,
            target.width + FramePadding * 2f, target.height + FramePadding * 2f);
        SetPosition(_frame, frame.position);
        _frame.style.width = frame.width;
        _frame.style.height = frame.height;
        _frame.RemoveFromClassList(HiddenClass);

        // Карточка
        Side side = ChooseSide(frame, cardSize, bounds, out Vector2 cardPosition);
        SetPosition(_card, cardPosition);
        _card.style.visibility = Visibility.Visible;

        // Стрелка — ромб на краю карточки, обращённом к элементу (внутренняя половина под карточкой)
        Rect card = new Rect(cardPosition, cardSize);
        Vector2 arrow;
        float half = ArrowSize * 0.5f;
        switch (side)
        {
            case Side.Below:
                arrow = new Vector2(ClampAlong(frame.center.x, card.xMin, card.xMax), card.yMin);
                break;
            case Side.Above:
                arrow = new Vector2(ClampAlong(frame.center.x, card.xMin, card.xMax), card.yMax);
                break;
            case Side.Right:
                arrow = new Vector2(card.xMin, ClampAlong(frame.center.y, card.yMin, card.yMax));
                break;
            default:
                arrow = new Vector2(card.xMax, ClampAlong(frame.center.y, card.yMin, card.yMax));
                break;
        }
        SetPosition(_arrow, arrow - new Vector2(half, half));
        _arrow.RemoveFromClassList(HiddenClass);
    }

    // ─── Layout helpers ───────────────────────────────────────

    private bool TryGetTargetRect(out Rect rect)
    {
        rect = default;
        if (_target == null || _target.panel == null) return false;

        // Элемент (или его предок) скрыт — показывать не на что
        for (VisualElement element = _target; element != null; element = element.parent)
        {
            if (element.resolvedStyle.display == DisplayStyle.None) return false;
            if (element.resolvedStyle.visibility == Visibility.Hidden) return false;
        }

        Rect world = _target.worldBound;
        if (float.IsNaN(world.width) || world.width <= 0f || world.height <= 0f) return false;

        rect = _layer.WorldToLocal(world);
        return true;
    }

    /// <summary>
    /// Сторона, с которой карточка помещается: у высоких элементов (окно кода) — сбоку,
    /// у остальных — снизу/сверху ближе к центру экрана.
    /// </summary>
    private static Side ChooseSide(Rect target, Vector2 size, Rect bounds, out Vector2 position)
    {
        Side[] order;
        if (target.height > bounds.height * 0.35f)
            order = new[] { Side.Right, Side.Left, Side.Below, Side.Above };
        else if (target.center.y < bounds.height * 0.5f)
            order = new[] { Side.Below, Side.Above, Side.Left, Side.Right };
        else
            order = new[] { Side.Above, Side.Below, Side.Left, Side.Right };

        foreach (Side side in order)
        {
            if (TryPlace(side, target, size, bounds, out position))
                return side;
        }

        TryPlace(order[0], target, size, bounds, out position);
        position.x = Mathf.Clamp(position.x, Margin, Mathf.Max(Margin, bounds.width - size.x - Margin));
        position.y = Mathf.Clamp(position.y, Margin, Mathf.Max(Margin, bounds.height - size.y - Margin));
        return order[0];
    }

    private static bool TryPlace(Side side, Rect target, Vector2 size, Rect bounds, out Vector2 position)
    {
        float maxX = Mathf.Max(Margin, bounds.width - size.x - Margin);
        float maxY = Mathf.Max(Margin, bounds.height - size.y - Margin);

        switch (side)
        {
            case Side.Below:
                position = new Vector2(Mathf.Clamp(target.center.x - size.x * 0.5f, Margin, maxX), target.yMax + Gap);
                return position.y + size.y <= bounds.height - Margin;
            case Side.Above:
                position = new Vector2(Mathf.Clamp(target.center.x - size.x * 0.5f, Margin, maxX), target.yMin - Gap - size.y);
                return position.y >= Margin;
            case Side.Right:
                position = new Vector2(target.xMax + Gap, Mathf.Clamp(target.center.y - size.y * 0.5f, Margin, maxY));
                return position.x + size.x <= bounds.width - Margin;
            default:
                position = new Vector2(target.xMin - Gap - size.x, Mathf.Clamp(target.center.y - size.y * 0.5f, Margin, maxY));
                return position.x >= Margin;
        }
    }

    /// <summary>Точка на краю карточки напротив элемента, не ближе 12px к углам.</summary>
    private static float ClampAlong(float value, float min, float max)
    {
        const float inset = 12f;
        if (max - min <= inset * 2f) return (min + max) * 0.5f;
        return Mathf.Clamp(value, min + inset, max - inset);
    }

    private static void SetPosition(VisualElement element, Vector2 position)
    {
        element.style.left = position.x;
        element.style.top = position.y;
    }

    // ─── Text ─────────────────────────────────────────────────

    private static void SetRichText(Label label, string richText)
    {
        label.enableRichText = true;
        label.text = richText;
    }

    /// <summary>
    /// Текст с `кодом`: обычные части — в noparse (никаких тегов из текста), код — подсветкой редактора
    /// (она сама экранирует '&lt;', видимые символы совпадают с исходником).
    /// </summary>
    private string FormatInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var builder = new StringBuilder();
        string[] parts = text.Split('`');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            if (i % 2 == 1) builder.Append(Highlight(parts[i]));
            else builder.Append("<noparse>").Append(parts[i]).Append("</noparse>");
        }
        return builder.ToString();
    }

    private string Highlight(string code)
    {
        if (_highlighter != null) return _highlighter.Highlight(code);
        return "<noparse>" + code + "</noparse>";
    }
}
