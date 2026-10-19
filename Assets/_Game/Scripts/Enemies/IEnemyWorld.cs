using UnityEngine;

/// <summary>
/// Что врагу нужно знать о мире (реализует EnemyManager). Позиции — свободные координаты карты в тайлах.
/// </summary>
public interface IEnemyWorld
{
    /// <summary>Точная позиция игрока на карте.</summary>
    Vector2 PlayerPosition { get; }
    bool IsPlayerAlive { get; }
    /// <summary>Враги могут бить (false — «фора» в начале запуска кода).</summary>
    bool CanAttack { get; }

    void DamagePlayer(Enemy attacker, float damage);

    /// <summary>Тайл, содержащий точку, — суша.</summary>
    bool IsWalkable(Vector2 mapPosition);

    Vector3 MapToWorld(Vector2 mapPosition);

    /// <summary>Мировых единиц в одном тайле (размер клетки сетки, по X).</summary>
    float WorldScale { get; }

    /// <summary>Случайная точка на суше в радиусе от точки (маршрут патруля); не нашлась — сама точка.</summary>
    Vector2 PickPatrolPoint(Vector2 from, float radius);
}
