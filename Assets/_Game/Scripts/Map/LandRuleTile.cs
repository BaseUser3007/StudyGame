using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Rule Tile земли биома: соседом «This» считается любой LandRuleTile, «NotThis» — всё остальное
/// (пусто, другие тайлы). Поэтому между биомами разного цвета нет береговой кромки — только жёсткий шов
/// цвета, а кромка рисуется лишь у воды.
/// </summary>
[CreateAssetMenu(fileName = "LandRuleTile", menuName = "CodeRogue/Map/LandRuleTile")]
public class LandRuleTile : RuleTile
{
    public override bool RuleMatch(int neighbor, TileBase other)
    {
        if (other is RuleOverrideTile overrideTile)
            other = overrideTile.m_InstanceTile;

        switch (neighbor)
        {
            case TilingRuleOutput.Neighbor.This: return other is LandRuleTile;
            case TilingRuleOutput.Neighbor.NotThis: return !(other is LandRuleTile);
        }

        return true;
    }
}
