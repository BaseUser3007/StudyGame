using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Нижняя панель инвентаря: ряд ячеек с иконкой ресурса и количеством.
/// Ячейки создаются по числу слотов из первого снимка инвентаря.
/// При наведении мыши на непустую ячейку над ней показывается подсказка о предмете.
/// </summary>
public class InventoryBar : VisualElement
{
    private const float TooltipGap = 6f;
    private const float ScreenMargin = 4f;

    private readonly VisualElement _barRoot;
    private readonly VisualElement _bar;
    private readonly List<SlotView> _slots = new List<SlotView>();
    private readonly ItemTooltip _tooltip;

    // Ячейка под мышью (null — ни одной)
    private SlotView _hovered;

    public InventoryBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
        LocUxml.Translate(root);
        _barRoot = root.Q<VisualElement>("inventory-bar-root");
        _bar = root.Q<VisualElement>("inventory-bar");

        Debug.Assert(_barRoot != null, "InventoryBar: inventory-bar-root not found");
        Debug.Assert(_bar != null, "InventoryBar: inventory-bar not found");

        // Подсказка живёт внутри шаблона, чтобы к ней применялся InventoryBar.uss
        _tooltip = new ItemTooltip();
        _tooltip.Root.RegisterCallback<GeometryChangedEvent>(OnTooltipGeometryChanged);
        _barRoot?.Add(_tooltip.Root);

        // Корень растянут на всю ширину — клики мимо панели должны проходить в игру
        pickingMode = PickingMode.Ignore;
        root.pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = 0;
        style.right = 0;
        style.bottom = 0;

        Add(root);
    }

    // ─── Public API ───────────────────────────────────────────

    public void UpdateSlots(IReadOnlyList<InventorySlot> slots)
    {
        if (_bar == null) return;

        if (_slots.Count != slots.Count)
            RebuildSlots(slots.Count);

        for (int i = 0; i < slots.Count; i++)
            _slots[i].Show(slots[i]);

        // Содержимое ячейки под мышью могло измениться (код работает, пока на неё наведено)
        if (_hovered != null)
            ShowTooltip(_hovered);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void RebuildSlots(int count)
    {
        HideTooltip();
        _bar.Clear();
        _slots.Clear();

        for (int i = 0; i < count; i++)
        {
            SlotView slot = new SlotView(i + 1, OnSlotPointerEnter, OnSlotPointerLeave);
            _slots.Add(slot);
            _bar.Add(slot.Root);
        }
    }

    // ─── Tooltip ──────────────────────────────────────────────

    private void OnSlotPointerEnter(SlotView slot)
    {
        _hovered = slot;
        ShowTooltip(slot);
    }

    private void OnSlotPointerLeave(SlotView slot)
    {
        if (_hovered != slot) return;
        HideTooltip();
    }

    private void ShowTooltip(SlotView slot)
    {
        if (slot.Data.IsEmpty)
        {
            // Пустая ячейка — без подсказки (мышь остаётся на ней: подсказка вернётся, когда появится предмет)
            _tooltip.Hide();
            return;
        }

        _tooltip.SetContent(slot.Data, slot.Number);
        _tooltip.Show();
        PositionTooltip();
        // Размер подсказки известен только после раскладки — уточняем позицию кадром позже
        _tooltip.Root.schedule.Execute(PositionTooltip);
    }

    private void HideTooltip()
    {
        _hovered = null;
        _tooltip.Hide();
    }

    private void OnTooltipGeometryChanged(GeometryChangedEvent evt)
    {
        PositionTooltip();
    }

    /// <summary>
    /// Ставит подсказку над ячейкой под мышью по центру и прижимает к краям экрана.
    /// Координаты — в системе inventory-bar-root (растянут на всю ширину панели).
    /// </summary>
    private void PositionTooltip()
    {
        if (_hovered == null || _barRoot == null || !_tooltip.IsShown || _barRoot.panel == null) return;

        Rect slotRect = _barRoot.WorldToLocal(_hovered.Root.worldBound);
        Rect screen = _barRoot.WorldToLocal(_barRoot.panel.visualTree.worldBound);
        if (float.IsNaN(slotRect.width) || float.IsNaN(screen.width)) return;

        float width = _tooltip.Root.resolvedStyle.width;
        float height = _tooltip.Root.resolvedStyle.height;
        if (float.IsNaN(width)) width = 0f;
        if (float.IsNaN(height)) height = 0f;

        float left = slotRect.center.x - width * 0.5f;
        left = Mathf.Max(screen.xMin + ScreenMargin, Mathf.Min(left, screen.xMax - ScreenMargin - width));

        // Над ячейкой; если сверху не хватает места — прижимаем к верхнему краю
        float top = Mathf.Max(screen.yMin + ScreenMargin, slotRect.yMin - TooltipGap - height);

        _tooltip.Root.style.left = left;
        _tooltip.Root.style.top = top;
    }

    /// <summary>
    /// Подсказка о предмете: имя, имя для кода, количество, прочность, еда, номер ячейки.
    /// Не перехватывает мышь и не берёт фокус.
    /// </summary>
    private class ItemTooltip
    {
        public readonly VisualElement Root;
        public bool IsShown { get; private set; }

        private readonly Label _title;
        private readonly Label _codeCaption;
        private readonly Label _code;
        private readonly Label _count;
        private readonly Label _durability;
        private readonly Label _energy;
        private readonly Label _health;
        private readonly Label _slot;

        public ItemTooltip()
        {
            Root = new VisualElement();
            Root.AddToClassList("inventory-tooltip");
            Root.AddToClassList("hidden");

            _title = AddLabel(Root, "inventory-tooltip-title");

            VisualElement codeRow = new VisualElement();
            codeRow.AddToClassList("inventory-tooltip-row");
            _codeCaption = AddLabel(codeRow, "inventory-tooltip-caption");
            _code = AddLabel(codeRow, "inventory-tooltip-code");
            Root.Add(codeRow);

            _count = AddLabel(Root, "inventory-tooltip-line");
            _durability = AddLabel(Root, "inventory-tooltip-line");
            _energy = AddLabel(Root, "inventory-tooltip-food");
            _health = AddLabel(Root, "inventory-tooltip-food");
            _slot = AddLabel(Root, "inventory-tooltip-slot");

            // Только для чтения: мышь проходит насквозь, фокус не берётся
            SetPassive(Root);
        }

        public void SetContent(InventorySlot slot, int number)
        {
            ResourceItem item = slot.Item;

            _title.text = Loc.T(item.DisplayName);
            // Подпись переводится при каждом показе — язык мог смениться
            _codeCaption.text = Loc.T("In code:");
            // Имя для кода не переводится: так его принимают craft/drop/eat
            _code.text = string.IsNullOrEmpty(item.itemId) ? item.name : item.itemId;

            _count.text = Loc.F("Count: {0}", slot.Count);

            bool tool = slot.HasDurability;
            SetLine(_durability, tool,
                tool ? Loc.F("Durability: {0} / {1}", slot.Durability, slot.MaxDurability) : null);

            FoodItem food = item as FoodItem;
            float energy = food != null ? food.EnergyRestore : 0f;
            float health = food != null ? food.HealthRestore : 0f;
            SetLine(_energy, energy > 0f, energy > 0f ? Loc.F("+{0} energy", FormatAmount(energy)) : null);
            SetLine(_health, health > 0f, health > 0f ? Loc.F("+{0} health", FormatAmount(health)) : null);

            _slot.text = Loc.F("Slot {0}", number);
        }

        public void Show()
        {
            IsShown = true;
            Root.RemoveFromClassList("hidden");
            Root.BringToFront();
        }

        public void Hide()
        {
            IsShown = false;
            Root.AddToClassList("hidden");
        }

        private static Label AddLabel(VisualElement parent, string className)
        {
            Label label = new Label();
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static void SetLine(Label label, bool visible, string text)
        {
            label.text = visible ? text : string.Empty;
            label.EnableInClassList("hidden", !visible);
        }

        private static void SetPassive(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            element.focusable = false;
            foreach (VisualElement child in element.Children())
                SetPassive(child);
        }

        private static string FormatAmount(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    // ─── Slot ─────────────────────────────────────────────────

    private class SlotView
    {
        public readonly VisualElement Root;
        public readonly int Number;
        /// <summary>Последний показанный снимок ячейки (для подсказки).</summary>
        public InventorySlot Data { get; private set; }

        private readonly VisualElement _icon;
        private readonly Label _name;
        private readonly Label _count;
        private readonly VisualElement _durability;
        private readonly VisualElement _durabilityFill;

        public SlotView(int number, System.Action<SlotView> onEnter, System.Action<SlotView> onLeave)
        {
            Number = number;
            Root = new VisualElement();
            Root.AddToClassList("inventory-slot");
            Root.RegisterCallback<PointerEnterEvent>(_ => onEnter(this));
            Root.RegisterCallback<PointerLeaveEvent>(_ => onLeave(this));

            _icon = new VisualElement();
            _icon.AddToClassList("inventory-slot-icon");

            _name = new Label();
            _name.AddToClassList("inventory-slot-name");

            Label index = new Label(number.ToString());
            index.AddToClassList("inventory-slot-index");

            _count = new Label();
            _count.AddToClassList("inventory-slot-count");

            // Полоска прочности — только у инструментов
            _durability = new VisualElement();
            _durability.AddToClassList("inventory-slot-durability");
            _durabilityFill = new VisualElement();
            _durabilityFill.AddToClassList("inventory-slot-durability-fill");
            _durability.Add(_durabilityFill);

            Root.Add(_icon);
            Root.Add(_name);
            Root.Add(index);
            Root.Add(_count);
            Root.Add(_durability);
        }

        public void Show(InventorySlot slot)
        {
            Data = slot;
            bool filled = !slot.IsEmpty;
            Sprite sprite = filled ? slot.Item.Icon : null;

            Root.EnableInClassList("inventory-slot--filled", filled);

            _icon.style.backgroundImage = sprite != null
                ? new StyleBackground(sprite)
                : new StyleBackground(StyleKeyword.None);
            _icon.EnableInClassList("hidden", sprite == null);

            // Нет спрайта — показываем имя ресурса
            _name.text = filled && sprite == null ? GetDisplayName(slot.Item) : string.Empty;
            _name.EnableInClassList("hidden", !filled || sprite != null);

            _count.text = filled && slot.Count > 1 ? slot.Count.ToString() : string.Empty;

            bool showDurability = filled && slot.HasDurability;
            _durability.EnableInClassList("hidden", !showDurability);
            if (showDurability)
            {
                float ratio = Mathf.Clamp01((float)slot.Durability / slot.MaxDurability);
                _durabilityFill.style.width = Length.Percent(ratio * 100f);
                // Зелёный → жёлтый → красный по мере износа
                _durabilityFill.style.backgroundColor = Color.Lerp(
                    new Color(0.85f, 0.3f, 0.25f), new Color(0.4f, 0.8f, 0.35f), ratio);
            }
        }

        private static string GetDisplayName(ResourceItem item)
        {
            return Loc.T(string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName);
        }
    }
}
