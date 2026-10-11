using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// Инспектор рецептов: каждый рецепт — сворачиваемая карточка с картинкой предмета,
// названием/картинкой самого предмета и списком ресурсов с иконками.

/// <summary>
/// Строка ресурса: [иконка] [ресурс] × [количество].
/// </summary>
[CustomPropertyDrawer(typeof(RecipeIngredient))]
public class RecipeIngredientDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        SerializedProperty item = property.FindPropertyRelative(nameof(RecipeIngredient.item));
        SerializedProperty amount = property.FindPropertyRelative(nameof(RecipeIngredient.amount));

        VisualElement row = CraftingEditorUI.Row();

        VisualElement icon = CraftingEditorUI.CreateIcon(28);
        row.Add(icon);

        ObjectField itemField = new ObjectField { objectType = typeof(ResourceItem) };
        itemField.style.flexGrow = 1;
        itemField.BindProperty(item);
        row.Add(itemField);

        Label times = new Label("×");
        times.style.marginLeft = 4;
        times.style.marginRight = 2;
        row.Add(times);

        IntegerField amountField = new IntegerField { isDelayed = true };
        amountField.style.width = 48;
        amountField.BindProperty(amount);
        row.Add(amountField);

        CraftingEditorUI.ShowItem(icon, item.objectReferenceValue as ResourceItem);
        row.TrackPropertyValue(item, p =>
            CraftingEditorUI.ShowItem(icon, p.objectReferenceValue as ResourceItem));

        return row;
    }
}

/// <summary>
/// Карточка рецепта. Поля «Название» и «Картинка» редактируют сам ассет предмета.
/// </summary>
[CustomPropertyDrawer(typeof(CraftRecipe))]
public class CraftRecipeDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        // Копия: исходный итератор инспектор переиспользует
        property = property.Copy();

        SerializedProperty result = property.FindPropertyRelative(nameof(CraftRecipe.result));
        SerializedProperty resultAmount = property.FindPropertyRelative(nameof(CraftRecipe.resultAmount));
        SerializedProperty ingredients = property.FindPropertyRelative(nameof(CraftRecipe.ingredients));
        SerializedProperty station = property.FindPropertyRelative(nameof(CraftRecipe.station));
        SerializedProperty energyCost = property.FindPropertyRelative(nameof(CraftRecipe.energyCost));

        Foldout foldout = new Foldout { value = property.isExpanded };
        foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == foldout) property.isExpanded = evt.newValue;
        });
        foldout.style.marginBottom = 4;

        // ─── Предмет ──────────────────────────────────────────

        VisualElement header = CraftingEditorUI.Row();
        header.style.alignItems = Align.FlexStart;
        header.style.marginBottom = 4;

        VisualElement bigIcon = CraftingEditorUI.CreateIcon(64);
        bigIcon.style.marginTop = 2;
        header.Add(bigIcon);

        VisualElement column = new VisualElement();
        column.style.flexGrow = 1;
        header.Add(column);

        ObjectField resultField = new ObjectField("Предмет") { objectType = typeof(ResourceItem) };
        resultField.BindProperty(result);
        column.Add(resultField);

        // Свойства самого предмета — привязываются к его ассету
        TextField nameField = new TextField("Название") { isDelayed = true };
        ObjectField spriteField = new ObjectField("Запасная иконка") { objectType = typeof(Sprite) };
        column.Add(nameField);
        column.Add(spriteField);

        IntegerField amountField = new IntegerField("Количество") { isDelayed = true };
        amountField.BindProperty(resultAmount);
        column.Add(amountField);

        foldout.Add(header);

        HelpBox warning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
        foldout.Add(warning);

        // ─── Требования ───────────────────────────────────────

        PropertyField ingredientsField = new PropertyField(ingredients, "Ресурсы");
        ingredientsField.BindProperty(ingredients);
        foldout.Add(ingredientsField);

        EnumField stationField = new EnumField("Станция");
        stationField.BindProperty(station);
        foldout.Add(stationField);

        FloatField energyField = new FloatField("Энергия") { isDelayed = true };
        energyField.BindProperty(energyCost);
        foldout.Add(energyField);

        // ─── Обновление ───────────────────────────────────────

        void BindItem()
        {
            ResourceItem item = result.objectReferenceValue as ResourceItem;

            nameField.Unbind();
            spriteField.Unbind();
            nameField.SetEnabled(item != null);
            spriteField.SetEnabled(item != null);

            if (item != null)
            {
                SerializedObject itemObject = new SerializedObject(item);
                nameField.BindProperty(itemObject.FindProperty(nameof(ResourceItem.displayName)));
                spriteField.BindProperty(itemObject.FindProperty(nameof(ResourceItem.sprite)));
            }
            else
            {
                nameField.SetValueWithoutNotify(string.Empty);
                spriteField.SetValueWithoutNotify(null);
            }
        }

        void Refresh()
        {
            CraftRecipe recipe = CraftingEditorUI.ReadRecipe(property);
            ResourceItem item = recipe.result;

            CraftingEditorUI.ShowItem(bigIcon, item);
            foldout.text = CraftingEditorUI.Summary(recipe);

            string problem = item == null ? "Не выбран предмет"
                : item.Icon == null ? "У предмета нет иконки — задайте Icon Prefab или запасную картинку"
                : recipe.GetRequirements().Count == 0 ? "Нет ресурсов — предмет будет бесплатным"
                : CraftingEditorUI.IngredientProblem(recipe);
            warning.text = problem ?? string.Empty;
            warning.style.display = problem == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        BindItem();
        Refresh();

        foldout.TrackPropertyValue(result, _ => { BindItem(); Refresh(); });
        foldout.TrackPropertyValue(property, _ => Refresh());
        nameField.RegisterValueChangedCallback(_ => foldout.schedule.Execute(Refresh));
        spriteField.RegisterValueChangedCallback(_ => foldout.schedule.Execute(Refresh));

        return foldout;
    }
}

/// <summary>
/// Общие элементы инспектора крафта.
/// </summary>
public static class CraftingEditorUI
{
    public static VisualElement Row()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    public static VisualElement CreateIcon(float size)
    {
        VisualElement icon = new VisualElement();
        icon.style.width = size;
        icon.style.height = size;
        icon.style.flexShrink = 0;
        icon.style.marginRight = 6;
        icon.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
        icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius =
            icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = 3;
        icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
        icon.style.justifyContent = Justify.Center;

        Label placeholder = new Label("?");
        placeholder.name = "placeholder";
        placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
        placeholder.style.color = new Color(1f, 1f, 1f, 0.4f);
        icon.Add(placeholder);

        return icon;
    }

    public static void ShowItem(VisualElement icon, ResourceItem item)
    {
        Sprite sprite = item != null ? item.Icon : null;

        icon.style.backgroundImage = sprite != null
            ? new StyleBackground(sprite)
            : new StyleBackground(StyleKeyword.None);

        Label placeholder = icon.Q<Label>("placeholder");
        if (placeholder != null)
            placeholder.style.display = sprite == null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// Заголовок карточки: «Axe ×1 ← 2 Stick, 2 Stone · Верстак».
    /// </summary>
    public static string Summary(CraftRecipe recipe)
    {
        string text = recipe.resultAmount > 1
            ? $"{recipe.DisplayName} ×{recipe.resultAmount}"
            : recipe.DisplayName;

        var parts = new System.Collections.Generic.List<string>();
        // Заголовок показывает то, что реально потребуется в игре
        foreach (RecipeIngredient requirement in recipe.GetRequirements())
            parts.Add($"{requirement.amount} {Name(requirement.item)}");

        if (parts.Count > 0)
            text += "  ←  " + string.Join(", ", parts);

        if (recipe.station != CraftingStation.None)
            text += " · " + recipe.station.DisplayName();

        return text;
    }

    /// <summary>
    /// Пустые строки и повторы ресурсов: в игре они пропускаются / складываются.
    /// </summary>
    public static string IngredientProblem(CraftRecipe recipe)
    {
        var seen = new System.Collections.Generic.HashSet<ResourceItem>();

        foreach (RecipeIngredient ingredient in recipe.ingredients)
        {
            if (ingredient.item == null)
                return "Есть пустая строка ресурса — она будет пропущена";
            if (!seen.Add(ingredient.item))
                return $"{Name(ingredient.item)} указан несколько раз — количества будут сложены";
        }

        return null;
    }

    /// <summary>
    /// Копия рецепта из SerializedProperty (boxedValue работает для [Serializable]-классов).
    /// </summary>
    public static CraftRecipe ReadRecipe(SerializedProperty property)
    {
        return property.boxedValue as CraftRecipe ?? new CraftRecipe();
    }

    private static string Name(ResourceItem item)
    {
        return string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
    }
}
