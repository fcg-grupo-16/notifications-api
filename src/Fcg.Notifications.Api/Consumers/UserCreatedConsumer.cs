using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;
using MongoDB.Bson;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="UserCreatedEvent"/> e envia o e-mail de boas-vindas pelo
/// <see cref="IEmailSender"/> configurado (Console por default). Idempotente por
/// <c>UserId</c>: reentregas do mesmo evento não geram e-mail duplicado.
/// Cada envio é persistido como <see cref="NotificationRecord"/> para auditoria.
/// </summary>
public sealed class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedConsumer> _logger;
    private readonly IProcessedMessageStore _store;
    private readonly INotificationRepository _repository;
    private readonly ITemplateRenderer _renderer;
    private readonly IEmailSender _sender;

    public UserCreatedConsumer(
        ILogger<UserCreatedConsumer> logger,
        IProcessedMessageStore store,
        INotificationRepository repository,
        ITemplateRenderer renderer,
        IEmailSender sender)
    {
        _logger = logger;
        _store = store;
        _repository = repository;
        _renderer = renderer;
        _sender = sender;
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

        var email = _renderer.RenderWelcome(context.Message);
        await _sender.SendAsync(email, context.CancellationToken);

        await SalvarHistoricoAsync(context);
    }

    // Best-effort: a falha na auditoria não deve reprocessar a mensagem — a chave
    // de idempotência já foi consumida, então um retry não reenviaria o e-mail
    // (só perderia o histórico do mesmo jeito). Logamos o erro e seguimos.
    private async Task SalvarHistoricoAsync(ConsumeContext<UserCreatedEvent> context)
    {
        try
        {
            await _repository.SaveAsync(new NotificationRecord
            {
                Type = "Welcome",
                Recipient = context.Message.Email,
                Payload = context.Message.ToBsonDocument(),
                Status = "Sent"
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falha ao persistir histórico da notificação Welcome para UserId={UserId}.",
                context.Message.UserId);
        }
    }
}
