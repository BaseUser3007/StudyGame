using UnityEngine;

/// <summary>
/// Оружие (меч и т.д.) — инструмент с типом Sword: не складывается, у каждого своя прочность,
/// каждый удар изнашивает его на wearPerUse, при нулевой прочности ломается и исчезает.
/// Крафтится, как инструменты. Удар наносит CombatService (команда attack).
/// </summary>
[CreateAssetMenu(fileName = "WeaponItem", menuName = "CodeRogue/Resources/WeaponItem")]
public class WeaponItem : ToolItem
{
    [Header("Weapon")]
    [Tooltip("Урон за один удар")]
    [Min(1)] public int damage = 15;

    [Tooltip("Дальность удара от персонажа до врага (в тайлах)")]
    [Min(0.5f)] public float attackRadius = 1.5f;

    public int Damage => Mathf.Max(1, damage);
    public float AttackRadius => Mathf.Max(0.5f, attackRadius);

    private void Reset()
    {
        toolType = ToolType.Sword;
    }

    private void OnValidate()
    {
        // Оружие — всегда Sword, иначе FindToolSlot(Axe) принял бы меч за топор
        toolType = ToolType.Sword;
    }
}
