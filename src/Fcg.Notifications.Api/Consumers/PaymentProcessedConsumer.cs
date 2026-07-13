using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;
using MongoDB.Bson;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="PaymentProcessedEvent"/>. Se o pagamento foi aprovado,
/// simula o envio de um e-mail de confirmação de compra registrando a mensagem
/// no console; caso contrário, registra que nenhum e-mail será enviado.
/// Idempotente por <c>OrderId</c> no caminho aprovado: reentregas do mesmo
/// evento não geram confirmação duplicada. Cada desfecho é persistido como
/// <see cref="NotificationRecord"/> para auditoria ("Sent" ou "Skipped").
/// </summary>
public sealed class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly INotificationRepository _repository;

    public PaymentProcessedConsumer(
        ILogger<PaymentProcessedConsumer> logger,
        IProcessedMessageStore store,
        INotificationRepository repository)
    {
        _logger = logger;
        _store = store;
        _repository = repository;
    }

    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var confirmation = EmailMessageBuilder.BuildPurchaseConfirmationMessage(context.Message);

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

            _logger.LogInformation("{Message}", confirmation);
            await SalvarHistoricoAsync(context, "Sent");
        }
        else
        {
            _logger.LogInformation("{Message}", EmailMessageBuilder.BuildRejectedMessage(context.Message));
            // Pagamento não aprovado: nenhuma confirmação sai, mas registramos o
            // desfecho como "Skipped" para auditoria.
            await SalvarHistoricoAsync(context, "Skipped");
        }
    }

    // Best-effort: a falha na auditoria não deve reprocessar a mensagem — no
    // caminho aprovado a chave de idempotência já foi consumida, então um retry
    // não reenviaria o e-mail (só perderia o histórico do mesmo jeito).
    private async Task SalvarHistoricoAsync(ConsumeContext<PaymentProcessedEvent> context, string status)
    {
        try
        {
            await _repository.SaveAsync(new NotificationRecord
            {
                Type = "PurchaseConfirmation",
                Recipient = context.Message.UserId,
                Payload = context.Message.ToBsonDocument(),
                Status = status
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falha ao persistir histórico da notificação PurchaseConfirmation para OrderId={OrderId}.",
                context.Message.OrderId);
        }
    }
}
