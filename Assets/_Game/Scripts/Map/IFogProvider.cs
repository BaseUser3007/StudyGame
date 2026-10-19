/// <summary>
/// Туман войны для команд: видно ли тайл прямо сейчас (тумана на нём нет совсем)
/// и открыт ли он вообще (игрок когда-либо его видел).
/// </summary>
public interface IFogProvider
{
    bool IsVisible(int x, int y);

    /// <summary>Тайл хоть раз был в поле зрения (Explored или Visible); за картой — false.</summary>
    bool IsExplored(int x, int y);
}
