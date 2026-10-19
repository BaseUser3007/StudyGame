using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Кнопка «Крафт» в правом нижнем углу и боковая панель рецептов.
/// Панель только показывает рецепты — сам крафт выполняется из кода командой craft("...").
/// </summary>
public class CraftingPanel : VisualElement
{
    private readonly IReadOnlyList<CraftRecipe> _recipes;
    private readonly IStationProvider _stations;
    // Открыт ли рецепт (закрытые не показываются); null — показываются все
    private readonly Func<CraftRecipe, bool> _isAvailable;
    // Пока панель открыта, наличие станции рядом перепроверяется — персонаж может подойти к верстаку
    private const long StationRefreshMs = 500;
    private readonly Dictionary<ResourceItem, int> _counts = new Dictionary<ResourceItem, int>();
    private readonly List<VisualElement> _entries = new List<VisualElement>();

    private Button _btnToggle;
    private VisualElement _panel;
    private ScrollView _recipeList;
    private Label _detailsEmpty;
    private VisualElement _detailsContent;
    private VisualElement _detailsIcon;
    private Label _detailsName;
    private VisualElement _detailsIngredients;
    private Label _detailsStation;
    private Label _detailsEnergy;
    private Label _detailsCode;

    private CraftRecipe _selected;

    public event Action OnOpened;

    public CraftingPanel(
        VisualTreeAsset template, IReadOnlyList<CraftRecipe> recipes, IStationProvider stations,
        Func<CraftRecipe, bool> isAvailable = null)
    {
        _recipes = recipes;
        _stations = stations;
        _isAvailable = isAvailable;

        VisualElement root = template.Instantiate();
        QueryElements(root);
        ValidateReferences();

        // Корень растянут на весь экран — клики мимо панели должны проходить в игру
        pickingMode = PickingMode.Ignore;
        root.pickingMode = PickingMode.Ignore;
        // TemplateContainer без растяжения имеет нулевую высоту — кнопка и панель уезжают за верх экрана
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

        _detailsCode.enableRichText = false;

        schedule.Execute(RefreshStations).Every(StationRefreshMs);

        BuildRecipeList();
        Add(root);
    }

    private void QueryElements(VisualElement root)
    {
        _btnToggle = root.Q<Button>("btn-craft");
        _panel = root.Q<VisualElement>("crafting-panel");
        _recipeList = root.Q<ScrollView>("recipe-list");
        _detailsEmpty = root.Q<Label>("details-empty");
        _detailsContent = root.Q<VisualElement>("details-content");
        _detailsIcon = root.Q<VisualElement>("details-icon");
        _detailsName = root.Q<Label>("details-name");
        _detailsIngredients = root.Q<VisualElement>("details-ingredients");
        _detailsStation = root.Q<Label>("details-station");
        _detailsEnergy = root.Q<Label>("details-energy");
        _detailsCode = root.Q<Label>("details-code");
    }

    private void ValidateReferences()
    {
        Debug.Assert(_btnToggle != null, "CraftingPanel: btn-craft not found");
        Debug.Assert(_panel != null, "CraftingPanel: crafting-panel not found");
        Debug.Assert(_recipeList != null, "CraftingPanel: recipe-list not found");
        Debug.Assert(_detailsContent != null, "CraftingPanel: details-content not found");
        Debug.Assert(_detailsIngredients != null, "CraftingPanel: details-ingredients not found");
        Debug.Assert(_detailsIcon != null, "CraftingPanel: details-icon not found");
        Debug.Assert(_recipes != null, "CraftingPanel: recipes are null");
    }

    // ─── Public API ───────────────────────────────────────────

    public void Toggle()
    {
        bool open = _panel.ClassListContains("hidden");

        // Пересобираем при открытии — правки RecipeBook в Play Mode сразу видны
        if (open)
        {
            BuildRecipeList();
            ShowDetails(_selected);
        }

        _panel.EnableInClassList("hidden", !open);
        _btnToggle.EnableInClassList("craft-toggle-btn--active", open);

        if (open)
            OnOpened?.Invoke();
    }

    /// <summary>
    /// Пересобрать список (открылся рецепт).
    /// </summary>
    public void RefreshRecipes()
    {
        BuildRecipeList();
        ShowDetails(_selected);
    }

    public void Close()
    {
        _panel.AddToClassList("hidden");
        _btnToggle.RemoveFromClassList("craft-toggle-btn--active");
    }

    /// <summary>
    /// Пересчитывает, каких ресурсов сколько, и обновляет подсветку рецептов.
    /// </summary>
    public void UpdateInventory(IReadOnlyList<InventorySlot> slots)
    {
        _counts.Clear();
        foreach (InventorySlot slot in slots)
        {
            if (slot.IsEmpty) continue;
            _counts.TryGetValue(slot.Item, out int count);
            _counts[slot.Item] = count + slot.Count;
        }

        RefreshEntries();
        ShowDetails(_selected);
    }

    // ─── Recipe List ──────────────────────────────────────────

    private void BuildRecipeList()
    {
        _recipeList.Clear();
        _entries.Clear();

        // Инспектор при правке пересоздаёт объекты рецептов — старый выбор мог устареть
        if (_selected != null && (!Contains(_recipes, _selected) || !IsAvailable(_selected)))
            _selected = null;

        foreach (CraftRecipe recipe in _recipes)
        {
            if (recipe == null || recipe.result == null || !IsAvailable(recipe)) continue;

            VisualElement entry = new VisualElement();
            entry.AddToClassList("recipe-entry");
            entry.userData = recipe;

            entry.Add(CreateIcon(recipe.result, "craft-icon--entry"));

            VisualElement info = new VisualElement();
            info.AddToClassList("recipe-entry-info");

            Label name = new Label(recipe.resultAmount > 1
                ? $"{recipe.DisplayName} ×{recipe.resultAmount}"
                : recipe.DisplayName);
            name.AddToClassList("recipe-entry-name");
            info.Add(name);

            // Мини-иконки ресурсов: [палка]2 [камень]2
            VisualElement cost = new VisualElement();
            cost.AddToClassList("recipe-entry-cost");
            foreach (RecipeIngredient requirement in recipe.GetRequirements())
            {
                cost.Add(CreateIcon(requirement.item, "craft-icon--mini"));
                Label amount = new Label(requirement.amount.ToString());
                amount.AddToClassList("recipe-entry-cost-amount");
                cost.Add(amount);
            }
            info.Add(cost);

            entry.Add(info);

            entry.RegisterCallback<ClickEvent>(_ => Select(recipe));

            _recipeList.Add(entry);
            _entries.Add(entry);
        }

        RefreshEntries();
    }

    private void RefreshEntries()
    {
        foreach (VisualElement entry in _entries)
        {
            CraftRecipe recipe = (CraftRecipe)entry.userData;
            entry.EnableInClassList("recipe-entry--selected", recipe == _selected);
            // Зелёный — только если craft() действительно выполнится (станции пока нет)
            entry.EnableInClassList("recipe-entry--craftable", recipe.CanCraft(GetCount, IsStationNearby));
        }
    }

    private void Select(CraftRecipe recipe)
    {
        _selected = recipe;
        RefreshEntries();
        ShowDetails(recipe);
    }

    // ─── Details ──────────────────────────────────────────────

    private void ShowDetails(CraftRecipe recipe)
    {
        _detailsEmpty.EnableInClassList("hidden", recipe != null);
        _detailsContent.EnableInClassList("hidden", recipe == null);
        if (recipe == null) return;

        _detailsIcon.Clear();
        _detailsIcon.Add(CreateIcon(recipe.result, "craft-icon--large"));

        _detailsName.text = recipe.resultAmount > 1
            ? $"{recipe.DisplayName} ×{recipe.resultAmount}"
            : recipe.DisplayName;

        _detailsIngredients.Clear();
        foreach (RecipeIngredient requirement in recipe.GetRequirements())
            _detailsIngredients.Add(CreateIngredientTile(requirement));

        bool stationMissing = recipe.NeedsStation && !IsStationNearby(recipe.station);
        _detailsStation.text = !recipe.NeedsStation ? $"Станция: {recipe.station.DisplayName()}"
            : stationMissing ? $"Станция: {recipe.station.DisplayName()} (нужна рядом)"
            : $"Станция: {recipe.station.DisplayName()} (рядом)";
        _detailsStation.EnableInClassList("details-row--missing", stationMissing);
        _detailsEnergy.text = $"Энергия: {recipe.energyCost:0.#}";
        _detailsCode.text = $"craft(\"{recipe.DisplayName}\")";
    }

    /// <summary>
    /// Плитка ресурса: картинка, «есть / нужно» в углу, название снизу.
    /// </summary>
    private VisualElement CreateIngredientTile(RecipeIngredient ingredient)
    {
        int have = GetCount(ingredient.item);

        VisualElement tile = new VisualElement();
        tile.AddToClassList("ingredient-tile");
        tile.EnableInClassList("ingredient-tile--missing", have < ingredient.amount);

        VisualElement slot = new VisualElement();
        slot.AddToClassList("ingredient-slot");
        slot.Add(CreateIcon(ingredient.item, "craft-icon--slot"));

        Label count = new Label($"{have}/{ingredient.amount}");
        count.AddToClassList("ingredient-count");
        slot.Add(count);

        Label name = new Label(GetDisplayName(ingredient.item));
        name.AddToClassList("ingredient-name");

        tile.Add(slot);
        tile.Add(name);
        return tile;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static bool Contains(IReadOnlyList<CraftRecipe> recipes, CraftRecipe recipe)
    {
        foreach (CraftRecipe r in recipes)
            if (r == recipe) return true;
        return false;
    }

    private bool IsAvailable(CraftRecipe recipe) => _isAvailable == null || _isAvailable(recipe);

    private bool IsStationNearby(CraftingStation station)
    {
        return _stations != null && _stations.IsStationNearPlayer(station);
    }

    /// <summary>
    /// Подсветка и строка «Станция» зависят от того, где стоит персонаж — обновляем, пока панель открыта.
    /// </summary>
    private void RefreshStations()
    {
        if (_panel.ClassListContains("hidden")) return;
        RefreshEntries();
        ShowDetails(_selected);
    }

    private int GetCount(ResourceItem item)
    {
        return item != null && _counts.TryGetValue(item, out int count) ? count : 0;
    }

    /// <summary>
    /// Картинка ресурса; если спрайта нет — первые буквы названия.
    /// </summary>
    private static VisualElement CreateIcon(ResourceItem item, string sizeClass)
    {
        VisualElement icon = new VisualElement();
        icon.AddToClassList("craft-icon");
        icon.AddToClassList(sizeClass);
        icon.pickingMode = PickingMode.Ignore;

        Sprite sprite = item != null ? item.Icon : null;
        if (sprite != null)
        {
            icon.style.backgroundImage = new StyleBackground(sprite);
        }
        else
        {
            string text = item != null ? GetDisplayName(item) : "?";
            Label fallback = new Label(text.Length > 2 ? text.Substring(0, 2) : text);
            fallback.AddToClassList("craft-icon-fallback");
            fallback.pickingMode = PickingMode.Ignore;
            icon.AddToClassList("craft-icon--empty");
            icon.Add(fallback);
        }

        return icon;
    }

    private static string GetDisplayName(ResourceItem item)
    {
        return string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
    }
}
