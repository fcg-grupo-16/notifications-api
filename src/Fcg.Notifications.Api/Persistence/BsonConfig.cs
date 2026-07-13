using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Fcg.Notifications.Persistence;

/// <summary>Configuração global de serialização BSON, compartilhada entre o app e os testes.</summary>
public static class BsonConfig
{
    private static int _configured;

    /// <summary>
    /// O driver 3.x não assume representação de Guid — sem isso, serializar payloads
    /// com Guid (ex.: OrderId do PaymentProcessedEvent) falha com
    /// "GuidRepresentation is Unspecified". Idempotente e thread-safe: o registro
    /// global só pode acontecer uma vez por processo.
    /// </summary>
    public static void EnsureGuidSerialization()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}
