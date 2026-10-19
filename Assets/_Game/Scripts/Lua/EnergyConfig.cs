using UnityEngine;

/// <summary>
/// Цены конструкций языка. Списывает EnergyInstrumenter во время выполнения, показывает EnergyCounter
/// (цена одного прохода) — оба читают одни и те же поля. Цены команд — в самих командах (EnergyCost).
/// </summary>
[CreateAssetMenu(fileName = "EnergyConfig", menuName = "CodeRogue/Lua/EnergyConfig")]
public class EnergyConfig : ScriptableObject
{
    [Header("Keyword Costs")]
    [Tooltip("При каждой проверке условия if")]
    public float ifCost = 3f;
    [Tooltip("При каждой проверке условия elseif")]
    public float elseifCost = 3f;
    [Tooltip("При каждом входе в ветку else")]
    public float elseCost = 1f;
    [Tooltip("На каждой итерации for")]
    public float forCost = 5f;
    [Tooltip("При каждой проверке условия while")]
    public float whileCost = 5f;
    [Tooltip("При каждой проверке until")]
    public float repeatCost = 5f;
    [Tooltip("При каждом вызове функции (и окна-«класса»). Не 0: иначе рекурсия через and — бесплатный цикл")]
    public float functionCost = 0.5f;
    [Tooltip("При каждом выполнении local")]
    public float localCost = 1f;
    [Tooltip("При каждом выполнении return")]
    public float returnCost = 1f;
}
