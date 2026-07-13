namespace Fcg.Notifications.Persistence;

/// <summary>
/// Persiste e consulta o histórico de notificações enviadas (auditoria/relatórios).
/// </summary>
public interface INotificationHistoryStore
{
    /// <summary>Persiste um registro de notificação enviada.</summary>
    Task SaveAsync(NotificationRecord record, CancellationToken ct = default);

    /// <summary>Retorna as notificações mais recentes (ordenadas por data de envio desc.).</summary>
    Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit, CancellationToken ct = default);

    /// <summary>Garante os índices da coleção (idempotente). No-op para implementações sem banco.</summary>
    Task GarantirIndicesAsync(CancellationToken ct = default);
}
