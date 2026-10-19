/// <summary>
/// Элемент UI, который показывает уведомления UIEventBus (PublishNotification).
/// Уведомления ему передаёт UIManager — единственный подписчик шины.
/// </summary>
public interface INotificationDisplay
{
    void ShowNotification(string message, LogEntryType type);
}
