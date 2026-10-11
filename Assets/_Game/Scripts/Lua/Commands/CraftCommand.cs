using System.Collections.Generic;
using System.Threading.Tasks;
using Lua;

public class CraftCommand : ILuaCommand
{
    public string Name => "craft";
    // Реальная стоимость задаётся в рецепте и списывается при крафте
    public float EnergyCost => 0f;
    public string Signature => "craft(name)";
    public string Description => "Скрафтить предмет по названию (рецепты — в панели крафта). " +
                                 "Тратит ингредиенты и энергию рецепта. Возвращает true или false и причину";

    private readonly RecipeBook _recipeBook;
    private readonly PlayerEnergy _playerEnergy;
    private readonly PlayerInventory _inventory;

    public CraftCommand(RecipeBook recipeBook, PlayerEnergy playerEnergy, PlayerInventory inventory)
    {
        _recipeBook = recipeBook;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
    }

    public void Register(LuaState state)
    {
        // Команда мгновенная — без async, чтобы не было пустого конечного автомата
        state.Environment[Name] = new LuaFunction((context, ct) =>
        {
            string error = context.ArgumentCount > 0 && context.GetArgument(0).TryRead(out string itemName)
                ? TryCraft(itemName)
                : "usage: craft(\"item name\")";

            if (error == null)
            {
                context.Return(true);
                return new ValueTask<int>(1);
            }

            context.Return(false, error);
            return new ValueTask<int>(2);
        });
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Возвращает null при успехе, иначе причину неудачи.
    /// </summary>
    private string TryCraft(string itemName)
    {
        CraftRecipe recipe = _recipeBook.Find(itemName);
        if (recipe == null || recipe.result == null)
            return $"unknown recipe: {itemName}";

        // Станции пока не реализованы
        if (recipe.NeedsStation)
            return $"station required: {recipe.station.ToString().ToLower()}";

        // Пустые строки пропущены, одинаковые ресурсы сложены
        List<RecipeIngredient> requirements = recipe.GetRequirements();

        foreach (RecipeIngredient requirement in requirements)
        {
            int have = _inventory.GetCount(requirement.item);
            if (have < requirement.amount)
                return $"not enough {GetName(requirement.item)}: need {requirement.amount}, have {have}";
        }

        if (!_playerEnergy.CanSpend(recipe.energyCost))
            return $"not enough energy: need {recipe.energyCost:0.#}, have {_playerEnergy.Current:0.#}";

        if (!_inventory.TryExchange(requirements, recipe.result, recipe.resultAmount))
            return "inventory is full";

        _playerEnergy.TrySpend(recipe.energyCost);
        return null;
    }

    private static string GetName(ResourceItem item)
    {
        if (item == null) return "?";
        return string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
    }
}
