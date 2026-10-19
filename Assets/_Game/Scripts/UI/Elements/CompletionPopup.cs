using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Список вариантов автодополнения рядом с курсором.
/// Сам ввод не обрабатывает — им управляет CodeEditor.
/// </summary>
public class CompletionPopup : VisualElement
{
    public event Action<CompletionItem> OnItemChosen;

    private readonly VisualElement _list;
    private readonly Label _description;
    private readonly List<VisualElement> _rows = new List<VisualElement>();
    private List<CompletionItem> _items = new List<CompletionItem>();
    private int _selected;

    public bool IsOpen => !ClassListContains("hidden");
    public CompletionItem SelectedItem =>
        _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public CompletionPopup()
    {
        AddToClassList("completion-popup");
        AddToClassList("hidden");
        pickingMode = PickingMode.Position;
        focusable = false;

        _list = new VisualElement();
        _list.AddToClassList("completion-list");
        Add(_list);

        _description = new Label();
        _description.AddToClassList("completion-description");
        Add(_description);
    }

    // ─── Public API ───────────────────────────────────────────

    public void Show(List<CompletionItem> items, Vector2 position)
    {
        _items = items;
        _selected = 0;
        Rebuild();

        style.left = position.x;
        style.top = position.y;
        RemoveFromClassList("hidden");
        BringToFront();
    }

    public void Hide()
    {
        AddToClassList("hidden");
        _items = new List<CompletionItem>();
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        _selected = (_selected + delta + _items.Count) % _items.Count;
        UpdateSelection();
    }

    // ─── Build ────────────────────────────────────────────────

    private void Rebuild()
    {
        _list.Clear();
        _rows.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            CompletionItem item = _items[i];
            int index = i;

            VisualElement row = new VisualElement();
            row.AddToClassList("completion-row");
            row.AddToClassList("completion-kind-" + item.Kind.ToString().ToLower());

            Label kind = new Label(KindIcon(item.Kind));
            kind.AddToClassList("completion-kind");
            row.Add(kind);

            Label label = new Label(item.Label);
            label.AddToClassList("completion-label");
            row.Add(label);

            Label detail = new Label(item.Detail ?? string.Empty);
            detail.AddToClassList("completion-detail");
            row.Add(detail);

            if (item.Kind == CompletionKind.Command)
            {
                Label cost = new Label(FormatCost(item.EnergyCost));
                cost.AddToClassList("completion-cost");
                row.Add(cost);
            }

            // PointerDown, а не Click — иначе поле успеет потерять фокус
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                OnItemChosen?.Invoke(_items[index]);
            });

            _list.Add(row);
            _rows.Add(row);
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].EnableInClassList("completion-row--selected", i == _selected);

        CompletionItem item = SelectedItem;
        string text = item?.Description ?? string.Empty;
        _description.text = text;
        _description.EnableInClassList("hidden", string.IsNullOrEmpty(text));
    }

    private static string KindIcon(CompletionKind kind)
    {
        switch (kind)
        {
            case CompletionKind.Command: return "C";
            case CompletionKind.Function: return "F";
            case CompletionKind.Keyword: return "K";
            case CompletionKind.Snippet: return "S";
            case CompletionKind.Member: return "M";
            default: return "V";
        }
    }

    public static string FormatCost(float cost)
    {
        return cost <= 0f ? Loc.T("free") : Loc.F("{0} en", cost.ToString("0.#"));
    }
}
