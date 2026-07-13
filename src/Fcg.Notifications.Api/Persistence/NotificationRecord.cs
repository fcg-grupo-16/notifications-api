using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Fcg.Notifications.Persistence;

/// <summary>
/// Registro de uma notificação enviada (coleção <c>notifications</c> em
/// <c>notificationsdb</c>), para auditoria/relatórios. Persistido "best-effort"
/// após o envio do e-mail.
/// </summary>
public sealed class NotificationRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Tipo do evento que originou a notificação (ex.: "UserCreatedEvent").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Destinatário (e-mail do usuário ou, quando não disponível, o UserId).</summary>
    public string Recipient { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Chave natural do evento (UserId/OrderId) — rastreia a origem.</summary>
    public string NaturalKey { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }
}
