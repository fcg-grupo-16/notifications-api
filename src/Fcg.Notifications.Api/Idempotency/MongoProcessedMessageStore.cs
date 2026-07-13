using MongoDB.Bson;
using MongoDB.Driver;

namespace Fcg.Notifications.Idempotency;

/// <summary>
/// Implementação durável do <see cref="IProcessedMessageStore"/> no MongoDB.
/// A corrida entre reentregas concorrentes é resolvida pelo banco: o índice
/// ÚNICO sobre (messageType, naturalKey) faz a segunda inserção falhar com
/// DuplicateKey. Sobrevive a restart e vale entre réplicas — substitui a
/// versão em memória (que fica para testes).
/// </summary>
public sealed class MongoProcessedMessageStore : IProcessedMessageStore
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoProcessedMessageStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<BsonDocument>("processed_messages");
    }

    /// <summary>Cria o índice único (idempotente — pode rodar em todo startup).</summary>
    public async Task GarantirIndicesAsync(CancellationToken ct = default)
    {
        var keys = Builders<BsonDocument>.IndexKeys
            .Ascending("messageType")
            .Ascending("naturalKey");
        var options = new CreateIndexOptions { Unique = true, Name = "ux_messageType_naturalKey" };
        await _collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: ct);
    }

    public async Task<bool> TryMarkAsProcessedAsync(string messageType, string naturalKey, CancellationToken ct = default)
    {
        var doc = new BsonDocument
        {
            { "messageType", messageType },
            { "naturalKey", naturalKey },
            { "processedAt", DateTime.UtcNow }
        };

        try
        {
            await _collection.InsertOneAsync(doc, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
