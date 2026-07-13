using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="UserCreatedEvent"/> e envia (via <see cref="IEmailSender"/>)
/// um e-mail de boas-vindas. Idempotente por <c>UserId</c>: reentregas do mesmo
/// evento não geram e-mail duplicado.
/// </summary>
public sealed class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly IEmailSender _emailSender;

    public UserCreatedConsumer(
        ILogger<UserCreatedConsumer> logger,
        IProcessedMessageStore store,
        IEmailSender emailSender)
    {
        _logger = logger;
        _store = store;
        _emailSender = emailSender;
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

        await _emailSender.SendAsync(EmailTemplates.Welcome(context.Message), context.CancellationToken);
    }
}
