using static Unity.VisualScripting.Dependencies.Sqlite.SQLite3;

public abstract class HarvestableResource : ResourceObject
{
    public override ToolType? RequiredTool => null;
    public override float InteractionRadius => 1.5f;

    protected override (ResourceItem item, int amount)? OnInteract()
    {
        if (variant == null) return null;
        var result = (variant.drop, variant.dropAmount);
        Consume();
        return result;
    }
}