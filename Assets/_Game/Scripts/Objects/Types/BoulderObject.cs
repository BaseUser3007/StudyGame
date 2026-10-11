/// <summary>
/// Валун — большой камень, добывается только киркой. Даёт несколько камней.
/// </summary>
public class BoulderObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Pickaxe;
}
