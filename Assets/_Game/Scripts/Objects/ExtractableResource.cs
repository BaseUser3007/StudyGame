public abstract class ExtractableResource : ResourceObject
{
    public override float InteractionRadius => 1.5f;

    protected override (ResourceItem item, int amount)? OnInteract()
    {
        if (variant == null) return null;
        var result = (variant.drop, variant.dropAmount);
        Consume();
        return result;
    }
}