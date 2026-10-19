using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lua;

public class CraftCommand : LuaCommandBase
{
    public override string Name => "craft";
    // Плата за вызов; сам крафт дополнительно тратит энергию рецепта
    public override float EnergyCost => 1f;
    public override string Signature => "craft(name)";
    public override string Description => "Скрафтить предмет по названию (рецепты — в панели крафта). " +
                                 "Тратит ингредиенты и энергию рецепта. Возвращает true или false и причину";

    private readonly RecipeBook _recipeBook;
    private readonly PlayerEnergy _playerEnergy;
    private readonly PlayerInventory _inventory;
    private readonly IStationProvider _stations;
    private readonly UnlockRegistry _unlocks;

    public CraftCommand(
        RecipeBook recipeBook, PlayerEnergy playerEnergy, PlayerInventory inventory, IStationProvider stations,
        UnlockRegistry unlocks)
    {
        _recipeBook = recipeBook;
        _playerEnergy = playerEnergy;
        _inventory = inventory;
        _stations = stations;
        _unlocks = unlocks;
    }

    // Команда мгновенная — без async, чтобы не было пустого конечного автомата
    protected override ValueTask<int> ExecuteAsync(LuaFunctionExecutionContext context, CancellationToken ct)
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

        // Закрытые рецепты открываются достижениями
        if (_unlocks != null && !_unlocks.IsRecipeAvailable(recipe))
            return $"recipe `{recipe.DisplayName}` is not unlocked yet";

        // Станция — постройка рядом с персонажем (верстак и т.д.)
        if (recipe.NeedsStation && !_stations.IsStationNearPlayer(recipe.station))
            return $"station required: {recipe.station.ToString().ToLower()} nearby (build one with build())";

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

        // Энергия — до обмена: обмен публикует ItemObtained, к этому моменту крафт уже оплачен
        if (!_playerEnergy.TrySpend(recipe.energyCost))
            return $"not enough energy: need {recipe.energyCost:0.#}, have {_playerEnergy.Current:0.#}";

        if (!_inventory.TryExchange(requirements, recipe.result, recipe.resultAmount))
        {
            _playerEnergy.Refund(recipe.energyCost);
            return "inventory is full";
        }

        GameEventBus.Publish(GameEventType.Crafted, recipe.Key, recipe.resultAmount);
        return null;
    }

    private static string GetName(ResourceItem item)
    {
        if (item == null) return "?";
        return string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
    }
}
