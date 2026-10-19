/// <summary>
/// Станция, рядом с которой выполняется рецепт. None — крафт где угодно.
/// </summary>
public enum CraftingStation
{
    None,
    Workbench,
    Furnace,
    // Новые значения — только в конец: enum сериализуется числом
    Campfire
}

public static class CraftingStationExtensions
{
    /// <summary>
    /// Название станции для интерфейса.
    /// </summary>
    public static string DisplayName(this CraftingStation station)
    {
        switch (station)
        {
            case CraftingStation.None: return "нет";
            case CraftingStation.Workbench: return "Верстак";
            case CraftingStation.Furnace: return "Печь";
            case CraftingStation.Campfire: return "Костёр";
            default: return station.ToString();
        }
    }
}
