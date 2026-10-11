using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Нижняя панель инвентаря: ряд ячеек с иконкой ресурса и количеством.
/// Ячейки создаются по числу слотов из первого снимка инвентаря.
/// </summary>
public class InventoryBar : VisualElement
{
    private readonly VisualElement _bar;
    private readonly List<SlotView> _slots = new List<SlotView>();

    public InventoryBar(VisualTreeAsset template)
    {
        VisualElement root = template.Instantiate();
        _bar = root.Q<VisualElement>("inventory-bar");

        Debug.Assert(_bar != null, "InventoryBar: inventory-bar not found");

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
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void RebuildSlots(int count)
    {
        _bar.Clear();
        _slots.Clear();

        for (int i = 0; i < count; i++)
        {
            SlotView slot = new SlotView(i + 1);
            _slots.Add(slot);
            _bar.Add(slot.Root);
        }
    }

    // ─── Slot ─────────────────────────────────────────────────

    private class SlotView
    {
        public readonly VisualElement Root;

        private readonly VisualElement _icon;
        private readonly Label _name;
        private readonly Label _count;
        private readonly VisualElement _durability;
        private readonly VisualElement _durabilityFill;

        public SlotView(int number)
        {
            Root = new VisualElement();
            Root.AddToClassList("inventory-slot");

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
            return string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
        }
    }
}
