/// <summary>
/// Элемент UI, который показывает ошибку запуска кода с объяснением (UIEventBus.OnCodeErrorReported).
/// Ошибку ему передаёт UIManager — единственный подписчик шины.
/// </summary>
public interface ICodeErrorDisplay
{
    void ShowCodeError(LuaErrorExplanation error);
}
