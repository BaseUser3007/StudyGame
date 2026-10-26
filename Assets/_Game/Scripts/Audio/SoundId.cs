/// <summary>
/// Звук обратной связи на игровое событие (AudioFeedback, T09). Значения сохраняются в ассетах
/// числом — новые добавляйте только в конец.
/// </summary>
public enum SoundId
{
    /// <summary>Подбор предмета (harvest / obtain) — GameEventBus Harvested.</summary>
    Pickup,
    /// <summary>Добыча инструментом (mine) — Mined.</summary>
    Mine,
    /// <summary>Крафт — Crafted.</summary>
    Craft,
    /// <summary>Постройка в забеге (build) и в лагере — Built, HubBuildingBuilt, HubBuildingUpgraded.</summary>
    Build,
    /// <summary>Враг побеждён — EnemyKilled.</summary>
    EnemyKilled,
    /// <summary>Враг ударил игрока — EnemyManager.OnPlayerAttacked.</summary>
    PlayerHurt,
    /// <summary>Съедена еда — FoodEaten.</summary>
    Eat,
    /// <summary>Запуск кода закончился без ошибок (RunFinished, итог "ok").</summary>
    RunSuccess,
    /// <summary>Запуск кода закончился ошибкой или отказом (итог "error" / "locked").</summary>
    RunError,
    /// <summary>Запуск кода остановлен нехваткой энергии (итог "energy").</summary>
    RunOutOfEnergy,
    /// <summary>Выполнено достижение — AchievementTracker.OnAchievementCompleted.</summary>
    Achievement
}
