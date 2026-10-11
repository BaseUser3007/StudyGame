using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Что выпадает при добыче командой mine: предмет и количество.
/// </summary>
[Serializable]
public struct LootEntry
{
    public ResourceItem item;
    [Min(1)] public int amount;
}

[CreateAssetMenu(fileName = "ResourceVariant", menuName = "CodeRogue/Resources/ResourceVariant")]
public class ResourceVariant : ScriptableObject
{
    [Header("Base")]
    public string variantId;
    public string displayName;

    [Header("Drop")]
    [Tooltip("Что попадает в инвентарь, когда объект подбирают командой harvest")]
    public ResourceItem drop;
    public int dropAmount = 1;

    [Header("Mining")]
    [Tooltip("Что выпадает на землю, когда объект добывают командой mine (дерево, валун). " +
             "Пусто — выпадает Drop × Drop Amount")]
    public List<LootEntry> loot = new List<LootEntry>();

    /// <summary>
    /// Что выпадает при mine: loot, а если он пуст — drop × dropAmount.
    /// </summary>
    public IEnumerable<LootEntry> MiningLoot
    {
        get
        {
            bool any = false;
            foreach (LootEntry entry in loot)
            {
                if (entry.item == null || entry.amount <= 0) continue;
                any = true;
                yield return entry;
            }

            if (!any && drop != null && dropAmount > 0)
                yield return new LootEntry { item = drop, amount = dropAmount };
        }
    }

    /// <summary>
    /// Энергия на добычу: сумма «прочность × количество» всего, что выпадает
    /// (для mine — по loot, для harvest — по drop).
    /// </summary>
    public float HarvestEnergyCost
    {
        get
        {
            float total = 0f;
            foreach (LootEntry entry in MiningLoot)
                total += entry.item.durability * entry.amount;
            return total;
        }
    }

    [Header("Visual")]
    public AnimationClip idleAnimation;
    public AnimationClip harvestAnimation; // анимация при сборе/рубке
    public AnimationClip destroyAnimation; // анимация уничтожения
}
