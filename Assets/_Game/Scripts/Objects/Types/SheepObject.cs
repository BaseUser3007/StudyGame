/// <summary>
/// Овца — добывается через mine с топором, оставляет сырое мясо. Стоит на месте (в срезе не бродит).
/// </summary>
public class SheepObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Axe;
}
