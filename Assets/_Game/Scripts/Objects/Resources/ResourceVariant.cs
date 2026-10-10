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

    [Header("Spawn")]
    [Tooltip("Minimum distance from spawn point to place this variant")]
    public float minSpawnDistance = 0f;
    [Tooltip("Maximum distance from spawn point to place this variant")]
    public float maxSpawnDistance = 100f;

    [Header("Visual")]
    public Sprite idleSprite;
    public AnimationClip idleAnimation;
    public AnimationClip harvestAnimation; // анимация при сборе/рубке
    public AnimationClip destroyAnimation; // анимация уничтожения
}