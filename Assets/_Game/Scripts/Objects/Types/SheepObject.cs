/// <summary>
/// Овца — добывается через mine с топором, оставляет сырое мясо. Пока выполняется код, медленно бродит
/// около точки появления (SheepWanderSystem); findObjects отдаёт её текущие x/y.
/// </summary>
public class SheepObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}
