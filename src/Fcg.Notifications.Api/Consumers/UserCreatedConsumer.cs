using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using MassTransit;

namespace Fcg.Notifications.Consumers;

/// <summary>
/// Consome <see cref="UserCreatedEvent"/> e simula o envio de um e-mail de
/// boas-vindas registrando a mensagem no console.
/// </summary>
public sealed class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedConsumer> _logger;

    public UserCreatedConsumer(ILogger<UserCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<UserCreatedEvent> context)
    {
        var message = EmailMessageBuilder.BuildWelcomeMessage(context.Message);
        _logger.LogInformation("{Message}", message);
        return Task.CompletedTask;
    }
}
