/// <summary>
/// Есть ли рядом с персонажем постройка-станция (для рецептов со станцией).
/// </summary>
public interface IStationProvider
{
    bool IsStationNearPlayer(CraftingStation station);
}
