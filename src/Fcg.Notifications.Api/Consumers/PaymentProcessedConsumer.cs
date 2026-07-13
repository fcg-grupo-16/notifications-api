using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="PaymentProcessedEvent"/>. Se o pagamento foi aprovado,
/// envia (via <see cref="IEmailSender"/>) um e-mail de confirmação de compra e
/// persiste o histórico; caso contrário, apenas registra que nenhum e-mail será
/// enviado. Idempotente por <c>OrderId</c> no caminho aprovado: reentregas do
/// mesmo evento não geram confirmação duplicada.
/// </summary>
public sealed class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly IEmailSender _emailSender;
    private readonly INotificationHistoryStore _history;

    public PaymentProcessedConsumer(
        ILogger<PaymentProcessedConsumer> logger,
        IProcessedMessageStore store,
        IEmailSender emailSender,
        INotificationHistoryStore history)
    {
        _logger = logger;
        _store = store;
        _emailSender = emailSender;
        _history = history;
    }

    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var confirmation = EmailTemplates.PurchaseConfirmation(context.Message);

        if (confirmation is not null)
        {
            // A chave só é consumida no caminho APROVADO: um evento "Rejected" não
            // marca o OrderId como processado, para não bloquear a confirmação
            // legítima caso o mesmo pedido seja aprovado num reprocessamento.
            var isNew = await _store.TryMarkAsProcessedAsync(
                nameof(PaymentProcessedEvent), context.Message.OrderId.ToString(), context.CancellationToken);

            if (!isNew)
            {
                _logger.LogInformation(
                    "PaymentProcessedEvent duplicado para OrderId={OrderId} ignorado (idempotência).",
                    context.Message.OrderId);
                return;
            }

            await _emailSender.SendAsync(confirmation, context.CancellationToken);

            // Persiste o histórico best-effort (e-mail já enviado + evento já marcado): falha aqui
            // não deve reprocessar/reenviar — apenas log.
            try
            {
                await _history.SaveAsync(new NotificationRecord
                {
                    Type = nameof(PaymentProcessedEvent),
                    Recipient = confirmation.To,
                    Subject = confirmation.Subject,
                    Body = confirmation.Body,
                    NaturalKey = context.Message.OrderId.ToString(),
                    SentAtUtc = DateTime.UtcNow
                }, context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Falha ao persistir o histórico da notificação (OrderId={OrderId}); e-mail já enviado.",
                    context.Message.OrderId);
            }
        }
        else
        {
            _logger.LogInformation(
                "[E-mail] Pagamento {Status} para o pedido {OrderId} (usuário {UserId}): nenhum e-mail de confirmação enviado.",
                context.Message.Status, context.Message.OrderId, context.Message.UserId);
        }
    }
}
