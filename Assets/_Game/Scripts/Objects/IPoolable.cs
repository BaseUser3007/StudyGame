/// <summary>
/// Интерфейс для объектов которые могут быть помещены в пул.
/// </summary>
public interface IPoolable
{
    /// <summary>
    /// Вызывается когда объект берётся из пула.
    /// </summary>
    void OnGetFromPool();

    /// <summary>
    /// Вызывается когда объект возвращается в пул.
    /// </summary>
    void OnReturnToPool();
}