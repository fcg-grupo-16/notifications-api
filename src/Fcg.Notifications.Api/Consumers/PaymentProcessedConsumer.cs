using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="PaymentProcessedEvent"/>. Se o pagamento foi aprovado,
/// simula o envio de um e-mail de confirmação de compra registrando a mensagem
/// no console; caso contrário, registra que nenhum e-mail será enviado.
/// Idempotente por <c>OrderId</c> no caminho aprovado: reentregas do mesmo
/// evento não geram confirmação duplicada.
/// </summary>
public sealed class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedConsumer> _logger;
    private readonly IProcessedMessageStore _store;

    public PaymentProcessedConsumer(ILogger<PaymentProcessedConsumer> logger, IProcessedMessageStore store)
    {
        _logger = logger;
        _store = store;
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
        }
        else
        {
            _logger.LogInformation("{Message}", EmailMessageBuilder.BuildRejectedMessage(context.Message));
        }
    }
}
