using System;
using UnityEngine;

public abstract class ResourceObject : MonoBehaviour
{
    public event Action<ResourceObject> OnConsumed;

    [Header("Variant")]
    [SerializeField] protected ResourceVariant variant;

    public bool IsAvailable { get; private set; } = true;
    public ResourceVariant Variant => variant;

    public abstract ToolType? RequiredTool { get; }
    public abstract float InteractionRadius { get; }

    protected virtual void Awake()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && variant != null)
            sr.sprite = variant.idleSprite;
    }

    public void SetVariant(ResourceVariant newVariant)
    {
        variant = newVariant;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && variant != null)
            sr.sprite = variant.idleSprite;
    }

    public void ResetState()
    {
        IsAvailable = true;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && variant != null)
            sr.sprite = variant.idleSprite;
    }

    public (ResourceItem item, int amount)? Interact()
    {
        if (!IsAvailable) return null;
        return OnInteract();
    }

    protected abstract (ResourceItem item, int amount)? OnInteract();

    protected void Consume()
    {
        IsAvailable = false;
        OnConsumed?.Invoke(this);
    }
}