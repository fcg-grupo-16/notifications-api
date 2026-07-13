using MongoDB.Driver;

namespace Fcg.Notifications.Persistence;

/// <summary>Repositório fino sobre a coleção "notifications" do MongoDB.</summary>
public sealed class NotificationRepository(IMongoCollection<NotificationRecord> collection)
    : INotificationRepository
{
    public Task SaveAsync(NotificationRecord record, CancellationToken ct = default) =>
        collection.InsertOneAsync(record, cancellationToken: ct);
}
