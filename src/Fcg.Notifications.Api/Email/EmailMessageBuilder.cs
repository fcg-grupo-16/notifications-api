using Fcg.Contracts.Events;

namespace Fcg.Notifications.Email;

/// <summary>
/// Helpers puros de mensagens que NÃO são e-mail. O conteúdo dos e-mails vive
/// em templates (Email/Templates) renderizados pelo <see cref="ITemplateRenderer"/>;
/// o envio é responsabilidade do <see cref="IEmailSender"/>.
/// </summary>
public static class EmailMessageBuilder
{
    /// <summary>
    /// Linha informativa registrada quando o pagamento não foi aprovado —
    /// nenhum e-mail de confirmação é enviado nesse caso.
    /// </summary>
    public static string BuildRejectedMessage(PaymentProcessedEvent message) =>
        $"[E-mail] Pagamento {message.Status} para o pedido {message.OrderId} " +
        $"(usuário {message.UserId}): nenhum e-mail de confirmação enviado.";

    /// <summary>Regra de negócio: confirmação só sai com status "Approved" (case-insensitive).</summary>
    public static bool IsApproved(string status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase);
}
