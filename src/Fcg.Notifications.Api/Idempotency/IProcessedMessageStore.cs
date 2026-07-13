namespace Fcg.Notifications.Idempotency;

/// <summary>
/// Registra e consulta chaves naturais de eventos já processados,
/// garantindo que o mesmo evento não gere e-mails duplicados.
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Tenta marcar a chave como processada. Retorna <c>true</c> se a chave
    /// era inédita (PRIMEIRA vez) e <c>false</c> se já existia.
    /// A operação deve ser atômica para evitar corrida entre reentregas concorrentes.
    /// </summary>
    Task<bool> TryMarkAsProcessedAsync(string messageType, string naturalKey, CancellationToken ct = default);
}
