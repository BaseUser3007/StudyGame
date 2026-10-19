/// <summary>
/// Золотая руда — добывается только киркой, даёт золото (loot gold x3, GameDesign §10.1).
/// Стоит в центре лагерей гоблинов; в Lua — тип "gold_ore".
/// </summary>
public class GoldOreObject : ExtractableResource
{
    public override ToolType? RequiredTool => ToolType.Pickaxe;
}
