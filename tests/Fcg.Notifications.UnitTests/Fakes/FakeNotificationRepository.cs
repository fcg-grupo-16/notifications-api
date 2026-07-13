using System.Collections.Concurrent;
using Fcg.Notifications.Persistence;

namespace Fcg.Notifications.UnitTests.Fakes;

/// <summary>Fake de <see cref="INotificationRepository"/> que guarda os registros em memória.</summary>
public sealed class FakeNotificationRepository : INotificationRepository
{
    public ConcurrentQueue<NotificationRecord> Saved { get; } = new();

    public Task SaveAsync(NotificationRecord record, CancellationToken ct = default)
    {
        Saved.Enqueue(record);
        return Task.CompletedTask;
    }
}
