using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="UserCreatedEvent"/> e envia (via <see cref="IEmailSender"/>)
/// um e-mail de boas-vindas, persistindo o histórico. Idempotente por <c>UserId</c>:
/// reentregas do mesmo evento não geram e-mail duplicado.
/// </summary>
public sealed class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly IEmailSender _emailSender;
    private readonly INotificationHistoryStore _history;

    public UserCreatedConsumer(
        ILogger<UserCreatedConsumer> logger,
        IProcessedMessageStore store,
        IEmailSender emailSender,
        INotificationHistoryStore history)
    {
        _logger = logger;
        _store = store;
        _emailSender = emailSender;
        _history = history;
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

        var email = EmailTemplates.Welcome(context.Message);
        await _emailSender.SendAsync(email, context.CancellationToken);

        // Persiste o histórico best-effort: o e-mail já foi enviado e o evento já está marcado como
        // processado (idempotência), então uma falha aqui não deve reprocessar/reenviar — apenas log.
        try
        {
            await _history.SaveAsync(new NotificationRecord
            {
                Type = nameof(UserCreatedEvent),
                Recipient = email.To,
                Subject = email.Subject,
                Body = email.Body,
                NaturalKey = context.Message.UserId,
                SentAtUtc = DateTime.UtcNow
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Falha ao persistir o histórico da notificação (UserId={UserId}); e-mail já enviado.",
                context.Message.UserId);
        }
    }
}
