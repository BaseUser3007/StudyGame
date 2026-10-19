using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Предмет и количество (стартовый инвентарь и т.п.).
/// </summary>
[System.Serializable]
public struct ItemStack
{
    public ResourceItem item;
    [Min(1)] public int amount;
}

/// <summary>
/// Базовые параметры игрока. То, что меняют постройки хаба (лимит энергии, слоты, запуски,
/// здоровье, день угрозы, стартовые предметы), читается не отсюда, а из RunModifiers.
/// </summary>
[CreateAssetMenu(fileName = "PlayerConfig", menuName = "CodeRogue/Player/PlayerConfig")]
public class PlayerConfig : ScriptableObject, IPlayerDataProvider
{
    [Header("Movement")]
    public float moveSpeed = 0.2f;
    public int visionRadius = 6;

    [Tooltip("Если персонаж упёрся и столько секунд не приближается к цели, команда движения завершается")]
    [Min(0.1f)] public float stuckTimeout = 2f;

    [Header("Health")]
    [Tooltip("Здоровье в начале забега; 0 — поражение, материалы забега теряются")]
    [Min(1f)] public float maxHealth = 100f;

    [Header("Run")]
    [Tooltip("С этого дня на карте появляются патрули врагов (мировая угроза)")]
    [Min(1)] public int threatDay = 3;

    [Header("Day")]
    [Tooltip("Сколько раз за день можно запустить код; после последнего запуска день заканчивается")]
    [Min(1)] public int runsPerDay = 3;

    [Header("Energy")]
    [Tooltip("Запас энергии на день без бонусов и штрафов")]
    public float baseEnergyLimit = 100f;
    [Tooltip("Ниже этого максимум энергии не опускается, даже со штрафом")]
    public float minEnergyLimit = 40f;
    [Tooltip("Наибольшая прибавка к максимуму за ночи на спальном месте")]
    public float maxEnergyBonus = 25f;
    [Tooltip("Прибавка к максимуму за каждую ночь подряд на спальном месте")]
    public float homeBonusPerDay = 5f;
    [Tooltip("Сколько здоровья восстанавливает ночь у спального места")]
    [Min(0f)] public float sleepHealAmount = 15f;

    [Tooltip("Штраф к максимуму на следующий день, если день закончился без энергии (доля от базового запаса)")]
    [Range(0f, 1f)]
    public float energyPenaltyMultiplier = 0.15f;

    [Header("Interaction")]
    public float interactionRadius = 1.5f;
    public float structureInteractionRadius = 1.5f;

    [Header("Inventory")]
    public int inventorySlots = 10;
    [Tooltip("С чем игрок начинает каждый забег")]
    public List<ItemStack> startingItems = new List<ItemStack>();

    // ─── IPlayerDataProvider ──────────────────────────────────
    int IPlayerDataProvider.VisionRadius => visionRadius;
}