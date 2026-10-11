using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct RecipeIngredient
{
    public ResourceItem item;
    [Min(1)] public int amount;
}

/// <summary>
/// Рецепт: набор ресурсов (+ станция) → предмет.
/// Настраивается прямо в списке <see cref="RecipeBook"/>.
/// </summary>
[Serializable]
public class CraftRecipe
{
    [Tooltip("Предмет, который получается")]
    public ResourceItem result;
    [Min(1)] public int resultAmount = 1;

    [Tooltip("Ресурсы, которые тратятся")]
    public List<RecipeIngredient> ingredients = new List<RecipeIngredient>();

    [Tooltip("Станция, рядом с которой выполняется крафт")]
    public CraftingStation station = CraftingStation.None;

    [Tooltip("Энергия, которая тратится при крафте")]
    [Min(0f)] public float energyCost = 1f;

    /// <summary>
    /// Имя, по которому рецепт вызывается из Lua: craft("...").
    /// </summary>
    public string DisplayName => result == null
        ? "(нет предмета)"
        : string.IsNullOrEmpty(result.displayName) ? result.name : result.displayName;

    /// <summary>
    /// Подходит ли название из craft("...") к этому рецепту (без учёта регистра).
    /// </summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || result == null) return false;
        query = query.Trim();

        return string.Equals(query, DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, result.itemId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, result.name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Нужна ли станция. Станции пока не реализованы — такой рецепт выполнить нельзя.
    /// </summary>
    public bool NeedsStation => station != CraftingStation.None;

    /// <summary>
    /// Итоговые требования: пустые строки пропущены, одинаковые ресурсы сложены
    /// (порядок — по первому упоминанию). Всё, что проверяет и тратит ресурсы, работает через этот список.
    /// </summary>
    public List<RecipeIngredient> GetRequirements()
    {
        var result = new List<RecipeIngredient>();

        foreach (RecipeIngredient ingredient in ingredients)
        {
            if (ingredient.item == null) continue;
            int amount = Math.Max(1, ingredient.amount);

            int index = result.FindIndex(r => r.item == ingredient.item);
            if (index < 0)
            {
                result.Add(new RecipeIngredient { item = ingredient.item, amount = amount });
            }
            else
            {
                RecipeIngredient merged = result[index];
                merged.amount += amount;
                result[index] = merged;
            }
        }

        return result;
    }

    /// <summary>
    /// Хватает ли ресурсов. getCount — сколько такого ресурса есть у игрока.
    /// </summary>
    public bool HasIngredients(Func<ResourceItem, int> getCount)
    {
        foreach (RecipeIngredient requirement in GetRequirements())
            if (getCount(requirement.item) < requirement.amount)
                return false;
        return true;
    }

    /// <summary>
    /// Можно ли выполнить рецепт прямо сейчас (без учёта энергии и места в инвентаре).
    /// </summary>
    public bool CanCraft(Func<ResourceItem, int> getCount)
    {
        return result != null && !NeedsStation && HasIngredients(getCount);
    }
}
