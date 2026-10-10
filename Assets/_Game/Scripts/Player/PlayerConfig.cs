using UnityEngine;

[CreateAssetMenu(fileName = "PlayerConfig", menuName = "CodeRogue/Player/PlayerConfig")]
public class PlayerConfig : ScriptableObject, IPlayerDataProvider
{
    [Header("Movement")]
    public float moveSpeed = 0.2f;
    public int visionRadius = 4;

    [Header("Energy")]
    public float baseEnergyLimit = 100f;
    public float minEnergyLimit = 40f;
    public float maxEnergyBonus = 25f;
    public float homeBonusPerDay = 5f;

    [Range(0f, 1f)]
    public float energyPenaltyMultiplier = 0.15f;

    [Header("Interaction")]
    public float interactionRadius = 1.5f;
    public float structureInteractionRadius = 1.5f;

    [Header("Inventory")]
    public int inventorySlots = 16;

    // ─── IPlayerDataProvider ──────────────────────────────────
    int IPlayerDataProvider.VisionRadius => visionRadius;
    float IPlayerDataProvider.BaseEnergyLimit => baseEnergyLimit;
}