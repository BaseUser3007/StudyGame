using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Переменные на точке останова: локальные переменные кадра и глобальные переменные игрока
/// (снимок <see cref="LuaDebugger"/> на момент паузы). Видна только на паузе, фокус не берёт.
/// Стили заданы в коде — панель не требует шаблона на сцене.
/// </summary>
public class DebugVariablesPanel : VisualElement
{
    private static readonly Color NameColor = new Color(0.61f, 0.86f, 1f);
    private static readonly Color ValueColor = new Color(0.92f, 0.92f, 0.92f);
    private static readonly Color HeaderColor = new Color(0.47f, 0.70f, 0.94f);
    private static readonly Color MutedColor = new Color(0.55f, 0.55f, 0.55f);

    private readonly Label _title;
    private readonly ScrollView _content;

    public DebugVariablesPanel()
    {
        name = "debug-variables";
        style.position = Position.Absolute;
        style.right = 8;
        style.top = 44;
        style.width = 300;
        style.maxHeight = Length.Percent(60);
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.paddingTop = 6;
        style.paddingBottom = 6;
        style.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 0.95f);
        style.borderTopLeftRadius = 4;
        style.borderTopRightRadius = 4;
        style.borderBottomLeftRadius = 4;
        style.borderBottomRightRadius = 4;
        SetBorder(new Color(0.24f, 0.36f, 0.52f));
        focusable = false;

        _title = new Label { focusable = false };
        _title.style.color = HeaderColor;
        _title.style.unityFontStyleAndWeight = FontStyle.Bold;
        _title.style.marginBottom = 4;
        Add(_title);

        _content = new ScrollView(ScrollViewMode.Vertical) { focusable = false };
        _content.style.flexShrink = 1;
        Add(_content);

        style.display = DisplayStyle.None;
    }

    // ─── Public API ───────────────────────────────────────────

    /// <summary>Показать переменные паузы в окне window на строке line.</summary>
    public void Show(string window, int line, IReadOnlyList<LuaVariable> locals, IReadOnlyList<LuaVariable> globals)
    {
        _title.text = Loc.F("Variables · {0}, line {1}", window, line);
        _content.Clear();

        bool any = false;
        any |= AddSection(Loc.T("Local"), locals);
        any |= AddSection(Loc.T("Global"), globals);
        if (!any)
            _content.Add(MakeLabel(Loc.T("No variables yet: create one with local x = …"), MutedColor));

        style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        style.display = DisplayStyle.None;
        _content.Clear();
    }

    // ─── Helpers ──────────────────────────────────────────────

    private bool AddSection(string header, IReadOnlyList<LuaVariable> variables)
    {
        if (variables == null || variables.Count == 0) return false;

        Label caption = MakeLabel(header, MutedColor);
        caption.style.marginTop = 2;
        _content.Add(caption);

        foreach (LuaVariable variable in variables)
        {
            var row = new VisualElement { focusable = false };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginLeft = 6;

            Label nameLabel = MakeLabel(variable.Name, NameColor);
            nameLabel.style.flexShrink = 0;
            Label valueLabel = MakeLabel(" = " + variable.Value, ValueColor);
            valueLabel.style.flexShrink = 1;
            valueLabel.style.whiteSpace = WhiteSpace.Normal;

            row.Add(nameLabel);
            row.Add(valueLabel);
            _content.Add(row);
        }
        return true;
    }

    private static Label MakeLabel(string text, Color color)
    {
        // Значения из Lua могут содержать '<' — без rich text они показываются как есть
        var label = new Label(text) { enableRichText = false, focusable = false };
        label.style.color = color;
        label.style.fontSize = 12;
        return label;
    }

    private void SetBorder(Color color)
    {
        style.borderTopWidth = 1;
        style.borderBottomWidth = 1;
        style.borderLeftWidth = 1;
        style.borderRightWidth = 1;
        style.borderTopColor = color;
        style.borderBottomColor = color;
        style.borderLeftColor = color;
        style.borderRightColor = color;
    }
}
