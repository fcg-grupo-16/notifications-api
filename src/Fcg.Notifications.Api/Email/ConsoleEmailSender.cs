namespace Fcg.Notifications.Email;

/// <summary>
/// Implementação default: "envia" o e-mail registrando-o no log do console,
/// preservando o comportamento simulado da Fase 2 (linhas "[E-mail] ...").
/// </summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("[E-mail] Para={To} | Assunto={Subject} | {Body}",
            message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
