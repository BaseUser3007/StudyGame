using UnityEngine;

/// <summary>
/// ѕищевой ресурс Ч €годы, гриб, м€со и т.д.
/// </summary>
[CreateAssetMenu(fileName = "FoodResource", menuName = "CodeRogue/Resources/FoodResource")]
public class FoodResource : ResourceItem
{
    [Header("Food")]
    public float energyRestore = 10f;
}