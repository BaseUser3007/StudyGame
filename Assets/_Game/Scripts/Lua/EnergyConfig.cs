using UnityEngine;

[CreateAssetMenu(fileName = "EnergyConfig", menuName = "CodeRogue/Lua/EnergyConfig")]
public class EnergyConfig : ScriptableObject
{
    [Header("Keyword Costs")]
    public float ifCost = 3f;
    public float elseifCost = 3f;
    public float elseCost = 1f;
    public float forCost = 5f;
    public float whileCost = 5f;
    public float repeatCost = 5f;
    public float functionCost = 2f;
    public float localCost = 1f;
    public float returnCost = 1f;

    [Header("API Call Costs")]
    public float apiCallCost = 1f;

    [Header("Other Costs")]
    public float variableCost = 1f;
}