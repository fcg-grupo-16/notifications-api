using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="UserCreatedEvent"/> e simula o envio de um e-mail de
/// boas-vindas registrando a mensagem no console. Idempotente por
/// <c>UserId</c>: reentregas do mesmo evento não geram e-mail duplicado.
/// </summary>
public sealed class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedConsumer> _logger;
    private readonly IProcessedMessageStore _store;

    public UserCreatedConsumer(ILogger<UserCreatedConsumer> logger, IProcessedMessageStore store)
    {
        _logger = logger;
        _store = store;
    }

    public async Task Consume(ConsumeContext<UserCreatedEvent> context)
    {
        var isNew = await _store.TryMarkAsProcessedAsync(
            nameof(UserCreatedEvent), context.Message.UserId, context.CancellationToken);

        if (!isNew)
        {
            _logger.LogInformation(
                "UserCreatedEvent duplicado para UserId={UserId} ignorado (idempotência).",
                context.Message.UserId);
            return;
        }

        var message = EmailMessageBuilder.BuildWelcomeMessage(context.Message);
        _logger.LogInformation("{Message}", message);
    }
}
