using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Один лагерь статических врагов: сколько врагов, как далеко от спавна и есть ли ценный объект в центре.
/// </summary>
[Serializable]
public struct EnemyCampSpec
{
    [Min(1)] public int enemyCount;
    [Tooltip("Центр лагеря не ближе этого к точке появления игрока (в тайлах)")]
    [Min(0f)] public float minDistanceFromSpawn;
    [Tooltip("Поставить в центр лагеря EnemyConfig.campValuablePrefab (gold_ore) — «охраняют ценное место»")]
    public bool valuableInCenter;
    [Tooltip("Необязательный вожак: ставится первым вместо одного из обычных врагов (например, goblin_guard)")]
    public EnemyDefinition leader;
}

/// <summary>
/// Общие настройки врагов (GameDesign §10.1): какие виды где появляются, лагеря статических врагов,
/// патрули после угрозы, значения характеристик по умолчанию.
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "CodeRogue/Enemies/EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("Kinds")]
    [Tooltip("Статические враги (охрана лагерей) — появляются в начале забега")]
    public List<EnemyDefinition> guardEnemies = new List<EnemyDefinition>();
    [Tooltip("Патрули — появляются из тумана в начале каждого дня, начиная с дня угрозы")]
    public List<EnemyDefinition> patrolEnemies = new List<EnemyDefinition>();

    [Header("Camps (статические враги)")]
    [Tooltip("Лагеря на карту. Ставятся от самого дальнего к ближнему")]
    public List<EnemyCampSpec> camps = new List<EnemyCampSpec>
    {
        new EnemyCampSpec { enemyCount = 1, minDistanceFromSpawn = 15f, valuableInCenter = false },
        new EnemyCampSpec { enemyCount = 2, minDistanceFromSpawn = 20f, valuableInCenter = true },
        new EnemyCampSpec { enemyCount = 3, minDistanceFromSpawn = 25f, valuableInCenter = true }
    };
    [Tooltip("Минимальное расстояние между центрами лагерей (в тайлах)")]
    [Min(0f)] public float minCampSpacing = 20f;
    [Tooltip("Враги лагеря стоят в этом радиусе от его центра (в тайлах)")]
    [Min(0f)] public float campRadius = 2f;
    [Tooltip("Ценный объект в центре лагеря (gold_ore, ExtractableResource). Пусто — без него")]
    public ResourceObject campValuablePrefab;

    [Header("Placement")]
    [Tooltip("Не ближе этого к ресурсам на карте (в тайлах)")]
    [Min(0f)] public float objectSpacing = 0.75f;
    [Tooltip("Не ближе этого к другим врагам (в тайлах)")]
    [Min(0f)] public float enemySpacing = 0.8f;

    [Header("Patrols (с дня угрозы)")]
    [Tooltip("Новых патрулей в день угрозы; дальше — на patrolsPerDayGrowth больше каждый день")]
    [Min(0)] public int patrolsOnThreatDay = 1;
    [Min(0)] public int patrolsPerDayGrowth = 1;
    [Tooltip("Больше этого новых патрулей за день не появляется")]
    [Min(0)] public int maxNewPatrolsPerDay = 4;
    [Tooltip("Предел живых патрулей одновременно (0 — без предела)")]
    [Min(0)] public int maxPatrols = 0;
    [Tooltip("Патруль появляется на суше в тумане не ближе этого к игроку (в тайлах)")]
    [Min(0f)] public float patrolSpawnMinDistance = 12f;
    [Tooltip("…и не дальше этого (в тайлах)")]
    [Min(0f)] public float patrolSpawnMaxDistance = 20f;
    [Tooltip("Следующая точка маршрута — случайная суша не дальше этого от места появления патруля (в тайлах)")]
    [Min(1f)] public float patrolPointRadius = 8f;
    [Tooltip("Пауза в точке маршрута (секунды игрового времени)")]
    [Min(0f)] public float patrolWaitSeconds = 1f;

    [Header("Defaults (если в EnemyDefinition 0)")]
    [Min(0.1f)] public float defaultAggroRadius = 3f;
    [Tooltip("Центр–центр")]
    [Min(0.1f)] public float defaultAttackRadius = 1f;
    [Min(0f)] public float defaultAttackWindup = 0.4f;
    [Min(0.1f)] public float defaultAttackCooldown = 1.5f;
    [Tooltip("Тайлов в секунду (игрок — 1 / PlayerConfig.moveSpeed = 5): убежать можно всегда")]
    [Min(0.1f)] public float defaultChaseSpeed = 3f;
    [Min(0.1f)] public float defaultPatrolSpeed = 2f;
    [Min(0.1f)] public float defaultLoseChaseDistance = 6f;
    [Min(0.1f)] public float defaultLeashRadius = 8f;

    [Header("Time")]
    [Tooltip("В начале каждого запуска кода враги столько секунд не бьют (игрок успевает уйти)")]
    [Min(0f)] public float runStartGrace = 0.5f;
}
