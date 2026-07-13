namespace Fcg.Notifications.Email;

/// <summary>Mensagem de e-mail pronta para envio.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Abstrai o canal de envio de e-mail (console, SMTP, SendGrid...).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
