/// <summary>
/// Туман войны для команд: видно ли тайл прямо сейчас (тумана на нём нет совсем).
/// </summary>
public interface IFogProvider
{
    bool IsVisible(int x, int y);
}
