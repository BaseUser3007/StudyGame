/// <summary>
/// Стоит ли персонаж у спального места (для бонуса к энергии в конце дня).
/// </summary>
public interface ISleepingPlaceProvider
{
    bool IsSleepingPlaceNearPlayer();
}
