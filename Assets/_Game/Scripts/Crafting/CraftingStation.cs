/// <summary>
/// Станция, рядом с которой выполняется рецепт. None — крафт где угодно.
/// </summary>
public enum CraftingStation
{
    None,
    Workbench,
    Furnace
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
            default: return station.ToString();
        }
    }
}
