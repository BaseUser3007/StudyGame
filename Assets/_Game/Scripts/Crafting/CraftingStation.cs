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
    /// Название станции для интерфейса (английский исходник; в UI — через Loc.T).
    /// </summary>
    public static string DisplayName(this CraftingStation station)
    {
        switch (station)
        {
            case CraftingStation.None: return "none";
            case CraftingStation.Workbench: return "Workbench";
            case CraftingStation.Furnace: return "Furnace";
            case CraftingStation.Campfire: return "Campfire";
            default: return station.ToString();
        }
    }
}
