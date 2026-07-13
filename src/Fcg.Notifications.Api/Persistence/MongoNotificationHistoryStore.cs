using MongoDB.Driver;

namespace Fcg.Notifications.Persistence;

/// <summary>
/// Implementação em MongoDB do <see cref="INotificationHistoryStore"/>
/// (coleção <c>notifications</c> em <c>notificationsdb</c>).
/// </summary>
public sealed class MongoNotificationHistoryStore : INotificationHistoryStore
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    private readonly IMongoCollection<NotificationRecord> _notifications;

    public MongoNotificationHistoryStore(IMongoDatabase database)
    {
        _notifications = database.GetCollection<NotificationRecord>("notifications");
    }

    public Task SaveAsync(NotificationRecord record, CancellationToken ct = default) =>
        _notifications.InsertOneAsync(record, cancellationToken: ct);

    public async Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        var safeLimit = limit is > 0 and <= MaxLimit ? limit : DefaultLimit;
        return await _notifications
            .Find(FilterDefinition<NotificationRecord>.Empty)
            .SortByDescending(n => n.SentAtUtc)
            .Limit(safeLimit)
            .ToListAsync(ct);
    }

    public Task GarantirIndicesAsync(CancellationToken ct = default)
    {
        var indice = new CreateIndexModel<NotificationRecord>(
            Builders<NotificationRecord>.IndexKeys.Descending(n => n.SentAtUtc),
            new CreateIndexOptions { Name = "ix_sentAtUtc" });

        return _notifications.Indexes.CreateOneAsync(indice, cancellationToken: ct);
    }
}
