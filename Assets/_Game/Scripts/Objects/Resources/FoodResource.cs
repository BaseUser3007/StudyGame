using UnityEngine;

/// <summary>
/// Пищевой ресурс — ягоды, гриб, мясо и т.д.
/// </summary>
[CreateAssetMenu(fileName = "FoodResource", menuName = "CodeRogue/Resources/FoodResource")]
public class FoodResource : ResourceItem
{
    [Header("Food")]
    public float energyRestore = 10f;
}