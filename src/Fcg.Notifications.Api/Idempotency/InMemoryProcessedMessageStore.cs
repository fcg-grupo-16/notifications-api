using System.Collections.Concurrent;

namespace Fcg.Notifications.Idempotency;

/// <summary>
/// Implementação em memória do <see cref="IProcessedMessageStore"/>.
/// O <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> é atômico: retorna
/// <c>true</c> apenas na primeira inserção da chave, resolvendo a corrida entre
/// reentregas concorrentes sem lock explícito.
/// ATENÇÃO: perde a memória ao reiniciar e não é compartilhada entre réplicas —
/// serve para MVP/testes. Para idempotência durável, a implementação deve ser no
/// MongoDB com índice único (messageType, naturalKey) — ver issue de persistência.
/// </summary>
public sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    public Task<bool> TryMarkAsProcessedAsync(string messageType, string naturalKey, CancellationToken ct = default)
    {
        var added = _seen.TryAdd($"{messageType}:{naturalKey}", 0);
        return Task.FromResult(added);
    }
}
