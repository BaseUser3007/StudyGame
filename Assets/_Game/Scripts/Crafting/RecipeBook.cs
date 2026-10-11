using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Все рецепты игры. Каждый рецепт — предмет, ресурсы с количеством, станция и энергия.
/// Панель крафта показывает рецепты в порядке этого списка.
/// </summary>
[CreateAssetMenu(fileName = "RecipeBook", menuName = "CodeRogue/Crafting/RecipeBook")]
public class RecipeBook : ScriptableObject
{
    [SerializeField] private List<CraftRecipe> recipes = new List<CraftRecipe>();

    public IReadOnlyList<CraftRecipe> Recipes => recipes;

    public CraftRecipe Find(string query)
    {
        foreach (CraftRecipe recipe in recipes)
            if (recipe != null && recipe.Matches(query))
                return recipe;
        return null;
    }

    // ─── Validation ───────────────────────────────────────────

    private void OnValidate()
    {
        // Новые элементы списка в инспекторе приходят с нулями — чиним количества
        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (CraftRecipe recipe in recipes)
        {
            if (recipe == null) continue;
            recipe.resultAmount = Mathf.Max(1, recipe.resultAmount);

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                RecipeIngredient ingredient = recipe.ingredients[i];
                if (ingredient.amount >= 1) continue;
                ingredient.amount = 1;
                recipe.ingredients[i] = ingredient;
            }

            if (recipe.result != null && !names.Add(recipe.DisplayName))
                Debug.LogWarning($"RecipeBook: два рецепта с названием '{recipe.DisplayName}' — craft() найдёт только первый", this);
        }
    }
}
