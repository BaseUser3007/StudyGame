using UnityEngine;

/// <summary>
/// Предмет-постройка: лежит в инвентаре, ставится на землю командой build.
/// </summary>
[CreateAssetMenu(fileName = "StructureItem", menuName = "CodeRogue/Resources/StructureItem")]
public class StructureItem : ResourceItem
{
    [Header("Structure")]
    [Tooltip("Что появляется на карте при установке")]
    public Structure prefab;

    // Иконка — со спрайта постройки, если отдельный iconPrefab не задан
    protected override GameObject IconSource =>
        iconPrefab != null ? iconPrefab : prefab != null ? prefab.gameObject : null;
}
