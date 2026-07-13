namespace Fcg.Notifications.Email;

/// <summary>
/// Abstração de envio de e-mail. A implementação é PLUGÁVEL: hoje um sender que
/// apenas registra no log (simulação), amanhã um provedor SMTP/HTTP real — sem
/// mudar os consumers, que dependem só desta interface.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
