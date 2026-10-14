using UnityEngine;

/// <summary>
/// Материальный ресурс — бревно, камень, палка и т.д. Переносится из забега в хаб.
/// </summary>
[CreateAssetMenu(fileName = "MaterialResource", menuName = "CodeRogue/Resources/MaterialResource")]
public class MaterialResource : ResourceItem
{
    public override bool IsMaterial => true;
}