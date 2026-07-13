using Fcg.Contracts.Events;

namespace Fcg.Notifications.Email;

/// <summary>
/// Monta a <see cref="EmailMessage"/> de cada notificação a partir dos templates
/// em <c>Email/Templates/</c> ("o que envio"), separado do canal de envio
/// (<see cref="IEmailSender"/>, "como envio").
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>E-mail de boas-vindas para um novo usuário.</summary>
    EmailMessage RenderWelcome(UserCreatedEvent evt);

    /// <summary>
    /// E-mail de confirmação de compra quando o pagamento foi aprovado;
    /// retorna <c>null</c> quando o status não é "Approved" (nenhum e-mail sai).
    /// </summary>
    EmailMessage? RenderPurchaseConfirmation(PaymentProcessedEvent evt);
}
