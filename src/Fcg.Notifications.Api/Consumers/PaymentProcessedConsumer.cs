using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="PaymentProcessedEvent"/>. Se o pagamento foi aprovado,
/// envia (via <see cref="IEmailSender"/>) um e-mail de confirmação de compra;
/// caso contrário, apenas registra que nenhum e-mail será enviado. Idempotente
/// por <c>OrderId</c> no caminho aprovado: reentregas do mesmo evento não geram
/// confirmação duplicada.
/// </summary>
public sealed class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly IEmailSender _emailSender;

    public PaymentProcessedConsumer(
        ILogger<PaymentProcessedConsumer> logger,
        IProcessedMessageStore store,
        IEmailSender emailSender)
    {
        _logger = logger;
        _store = store;
        _emailSender = emailSender;
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
        }
        else
        {
            _logger.LogInformation(
                "[E-mail] Pagamento {Status} para o pedido {OrderId} (usuário {UserId}): nenhum e-mail de confirmação enviado.",
                context.Message.Status, context.Message.OrderId, context.Message.UserId);
        }
    }
}
