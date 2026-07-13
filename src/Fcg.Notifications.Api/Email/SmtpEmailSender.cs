using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Fcg.Notifications.Email;

/// <summary>
/// Envio real de e-mail via SMTP (MailKit). Selecionável por Email__Provider=Smtp;
/// host/porta/credenciais vêm de Email__Smtp__* (env vars/Secret — nunca commitadas).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = options.Value.Smtp;

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.From));
        mime.To.Add(MailboxAddress.Parse(NormalizeAddress(message.To)));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrWhiteSpace(smtp.User))
        {
            await client.AuthenticateAsync(smtp.User, smtp.Password, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("[E-mail] SMTP enviado para {To} (assunto: {Subject}).",
            message.To, message.Subject);
    }

    // A confirmação de compra tem como destinatário o UserId (o evento não traz o
    // e-mail do comprador). Para o SMTP não rejeitar o endereço, anexamos um domínio
    // local de demonstração.
    private static string NormalizeAddress(string to) =>
        to.Contains('@') ? to : $"{to}@fcg.local";
}
