using System.Globalization;
using Fcg.Contracts.Events;

namespace Fcg.Notifications.Email;

/// <summary>
/// Constrói as mensagens de e-mail (simuladas) em pt-BR a partir dos eventos.
/// Extraído como helper puro para facilitar os testes de unidade.
/// </summary>
public static class EmailMessageBuilder
{
    /// <summary>
    /// Mensagem de boas-vindas para um novo usuário.
    /// </summary>
    public static string BuildWelcomeMessage(UserCreatedEvent message) =>
        $"[E-mail] Boas-vindas enviado para {message.Email} (usuário {message.Nome})";

    /// <summary>
    /// Mensagem de confirmação de compra quando o pagamento foi aprovado;
    /// retorna <c>null</c> quando o status não é "Approved" (nenhum e-mail é enviado).
    /// </summary>
    public static string? BuildPurchaseConfirmationMessage(PaymentProcessedEvent message)
    {
        if (!IsApproved(message.Status))
        {
            return null;
        }

        var price = message.Price.ToString("C", new CultureInfo("pt-BR"));
        return $"[E-mail] Confirmação de compra enviada para o usuário {message.UserId}: " +
               $"jogo {message.GameId} adquirido por {price} (pedido {message.OrderId}).";
    }

    /// <summary>
    /// Mensagem informando que nenhum e-mail de confirmação será enviado
    /// porque o pagamento foi rejeitado.
    /// </summary>
    public static string BuildRejectedMessage(PaymentProcessedEvent message) =>
        $"[E-mail] Pagamento {message.Status} para o pedido {message.OrderId} " +
        $"(usuário {message.UserId}): nenhum e-mail de confirmação enviado.";

    private static bool IsApproved(string status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase);
}
