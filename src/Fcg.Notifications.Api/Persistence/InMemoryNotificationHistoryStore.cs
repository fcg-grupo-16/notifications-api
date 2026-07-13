using System.Collections.Concurrent;

namespace Fcg.Notifications.Persistence;

/// <summary>
/// Implementação em memória do <see cref="INotificationHistoryStore"/> —
/// usada nos testes (e como fallback). Perde o conteúdo ao reiniciar e não é
/// compartilhada entre réplicas.
/// </summary>
public sealed class InMemoryNotificationHistoryStore : INotificationHistoryStore
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    private readonly ConcurrentQueue<NotificationRecord> _records = new();

    public Task SaveAsync(NotificationRecord record, CancellationToken ct = default)
    {
        _records.Enqueue(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        var safeLimit = limit is > 0 and <= MaxLimit ? limit : DefaultLimit;
        // A fila preserva a ordem de inserção (mais antigo -> mais novo); invertemos para desc.
        IReadOnlyList<NotificationRecord> recentes = _records.Reverse().Take(safeLimit).ToList();
        return Task.FromResult(recentes);
    }

    public Task GarantirIndicesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
