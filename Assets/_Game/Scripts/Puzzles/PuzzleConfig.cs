using UnityEngine;

/// <summary>
/// Головоломки на карте (этап 6, GameDesign.md §10.4): префабы, сколько и где ставить, награды.
/// Генерируются только открытые типы (UnlockRegistry, события на карте code_task / maze / plates).
/// </summary>
[CreateAssetMenu(fileName = "PuzzleConfig", menuName = "CodeRogue/Puzzles/Puzzle Config")]
public class PuzzleConfig : ScriptableObject
{
    [Header("Общие правила")]
    [Tooltip("Не ближе этого (в тайлах) к центру лагеря врагов — до края области головоломки")]
    [Min(0f)] public float minDistanceFromCamps = 8f;
    [Tooltip("Зазор между областями разных головоломок (тайлы)")]
    [Min(0f)] public float minDistanceBetweenAreas = 3f;

    [Header("code_task (обелиск)")]
    [Tooltip("Обелиск: спрайт с опорой у основания и Collider2D (не триггер). CodeTaskPuzzle добавится сам, если его нет")]
    public GameObject obeliskPrefab;
    [Min(0)] public int codeTaskCount = 2;
    [Min(0f)] public float codeTaskMinDistanceFromSpawn = 12f;
    [Tooltip("Между обелисками (тайлы)")]
    [Min(0f)] public float codeTaskMinSpacing = 15f;

    [Header("code_task: награды (до конца забега)")]
    [Tooltip("sum (и count после начала угрозы): +к BaseEnergyLimit и столько же энергии сейчас")]
    [Min(0f)] public float energyLimitBonus = 10f;
    [Tooltip("max: +к MaxHealth, здоровье — до нового максимума")]
    [Min(0f)] public float maxHealthBonus = 20f;
    [Tooltip("count до начала угрозы: на сколько дней позже угроза")]
    [Min(0)] public int threatDelayDays = 1;

    [Header("maze (лабиринт 7×7)")]
    [Tooltip("Тайл-стена: опора в центре тайла, Collider2D ≈ 1×1 тайл (не триггер), спрайт камня/куста")]
    public GameObject mazeWallPrefab;
    [Tooltip("Сундук в центре (maze_chest). Коллайдеры-не-триггеры на нём станут триггерами — игрок должен встать на центр")]
    public GameObject mazeChestPrefab;
    [Tooltip("Спрайт открытого сундука после решения (необязательно)")]
    public Sprite mazeChestOpenedSprite;
    [Min(0)] public int mazeCount = 1;
    [Min(0f)] public float mazeMinDistanceFromSpawn = 20f;
    [Tooltip("Минимальная длина пути от входа до центра (шаги). 7 = хотя бы 3 перехода между клетками")]
    [Range(3, 15)] public int mazeMinPathLength = 7;
    [Tooltip("«Густая изгородь»: туман внутри лабиринта снимается только в этом радиусе от игрока")]
    [Min(0)] public int mazeVisionRadius = 1;

    [Header("maze: награда")]
    [Tooltip("Предмет crystal (MaterialResource)")]
    public ResourceItem crystalItem;
    [Min(0)] public int crystalAmount = 2;

    [Header("plates (плиты по порядку, площадка 5×5)")]
    [Tooltip("Плита: плоский спрайт с опорой в центре тайла, без сплошного коллайдера (по плите ходят) и без " +
             "DepthSortedSprite — рисуется как пол (PlatesPuzzle.SortingOrder). Пусто — серый квадрат из кода")]
    public GameObject platePrefab;
    [Tooltip("Сколько площадок с плитами на карте (0 — ни одной, даже если тип открыт)")]
    [Min(0)] public int platesCount = 0;
    [Min(0f)] public float platesMinDistanceFromSpawn = 15f;

    [Header("plates: награда")]
    [Tooltip("crystalItem за решённые плиты (в инвентарь, не влезло — на землю в центре площадки)")]
    [Min(0)] public int platesCrystalAmount = 1;
}
