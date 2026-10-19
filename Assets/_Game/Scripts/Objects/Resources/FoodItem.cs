using UnityEngine;

/// <summary>
/// Еда — ягоды, сырое и жареное мясо и т.д. Съедается командой eat: восстанавливает энергию
/// и/или здоровье (не выше максимума). Не материал — в хаб не переносится.
/// </summary>
[CreateAssetMenu(fileName = "FoodItem", menuName = "CodeRogue/Resources/FoodItem")]
public class FoodItem : ResourceItem
{
    [Header("Food")]
    [Tooltip("Сколько энергии восстанавливает одна штука (не выше максимума)")]
    [Min(0f)] public float energyRestore;

    [Tooltip("Сколько здоровья восстанавливает одна штука (не выше максимума)")]
    [Min(0f)] public float healthRestore;

    public float EnergyRestore => Mathf.Max(0f, energyRestore);
    public float HealthRestore => Mathf.Max(0f, healthRestore);
}
