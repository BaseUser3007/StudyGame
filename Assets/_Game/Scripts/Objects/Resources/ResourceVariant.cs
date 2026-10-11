using UnityEngine;

[CreateAssetMenu(fileName = "ResourceVariant", menuName = "CodeRogue/Resources/ResourceVariant")]
public class ResourceVariant : ScriptableObject
{
    [Header("Base")]
    public string variantId;
    public string displayName;

    [Header("Drop")]
    public ResourceItem drop;
    public int dropAmount = 1;

    /// <summary>
    /// Энергия на добычу: прочность материала × количество.
    /// </summary>
    public float HarvestEnergyCost => drop != null ? drop.durability * dropAmount : 0f;

    [Header("Visual")]
    public AnimationClip idleAnimation;
    public AnimationClip harvestAnimation; // анимация при сборе/рубке
    public AnimationClip destroyAnimation; // анимация уничтожения
}