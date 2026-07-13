namespace Fcg.Notifications.Persistence;

/// <summary>Persistência do histórico de notificações enviadas.</summary>
public interface INotificationRepository
{
    Task SaveAsync(NotificationRecord record, CancellationToken ct = default);
}
