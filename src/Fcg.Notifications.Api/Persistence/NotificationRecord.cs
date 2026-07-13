using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Fcg.Notifications.Persistence;

/// <summary>Registro persistido de uma notificação enviada.</summary>
public sealed class NotificationRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Tipo da notificação: "Welcome" ou "PurchaseConfirmation".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Destinatário (e-mail ou UserId, conforme disponível no evento).</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Payload original do evento, para auditoria.</summary>
    public BsonDocument Payload { get; set; } = new();

    /// <summary>Status do envio: "Sent" (simulado) ou "Skipped" (pagamento não aprovado).</summary>
    public string Status { get; set; } = "Sent";

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
