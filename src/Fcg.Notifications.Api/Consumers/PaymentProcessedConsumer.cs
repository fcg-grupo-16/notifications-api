using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="PaymentProcessedEvent"/>. Se o pagamento foi aprovado,
/// simula o envio de um e-mail de confirmação de compra registrando a mensagem
/// no console; caso contrário, registra que nenhum e-mail será enviado.
/// </summary>
public sealed class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedConsumer> _logger;

    public PaymentProcessedConsumer(ILogger<PaymentProcessedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var confirmation = EmailMessageBuilder.BuildPurchaseConfirmationMessage(context.Message);

        if (confirmation is not null)
        {
            _logger.LogInformation("{Message}", confirmation);
        }
        else
        {
            _logger.LogInformation("{Message}", EmailMessageBuilder.BuildRejectedMessage(context.Message));
        }

        return Task.CompletedTask;
    }
}
