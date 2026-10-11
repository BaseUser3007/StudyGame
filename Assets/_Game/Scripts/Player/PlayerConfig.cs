using UnityEngine;

[CreateAssetMenu(fileName = "PlayerConfig", menuName = "CodeRogue/Player/PlayerConfig")]
public class PlayerConfig : ScriptableObject, IPlayerDataProvider
{
    [Header("Movement")]
    public float moveSpeed = 0.2f;
    public int visionRadius = 4;

    [Tooltip("Если персонаж упёрся и столько секунд не приближается к цели, команда движения завершается")]
    [Min(0.1f)] public float stuckTimeout = 2f;

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

    [Tooltip("Штраф к максимуму на следующий день, если день закончился без энергии (доля от базового запаса)")]
    [Range(0f, 1f)]
    public float energyPenaltyMultiplier = 0.15f;

    [Header("Interaction")]
    public float interactionRadius = 1.5f;
    public float structureInteractionRadius = 1.5f;

    [Header("Inventory")]
    public int inventorySlots = 10;

    // ─── IPlayerDataProvider ──────────────────────────────────
    int IPlayerDataProvider.VisionRadius => visionRadius;
    float IPlayerDataProvider.BaseEnergyLimit => baseEnergyLimit;
}