using System.Globalization;
using Fcg.Contracts.Events;

namespace Fcg.Notifications.Email;

/// <summary>
/// Renderer baseado em arquivos de template com placeholders <c>{{Chave}}</c>.
/// Convenção: a PRIMEIRA linha do template é o assunto; o restante é o corpo.
/// Os arquivos são lidos uma única vez (cache) — mudá-los exige novo deploy.
/// </summary>
public sealed class TemplateRenderer : ITemplateRenderer
{
    private static readonly string TemplatesPath =
        Path.Combine(AppContext.BaseDirectory, "Email", "Templates");

    private readonly Lazy<string> _welcome =
        new(() => File.ReadAllText(Path.Combine(TemplatesPath, "welcome.txt")));

    private readonly Lazy<string> _purchaseConfirmation =
        new(() => File.ReadAllText(Path.Combine(TemplatesPath, "purchase-confirmation.txt")));

    public EmailMessage RenderWelcome(UserCreatedEvent evt) =>
        Render(_welcome.Value, to: evt.Email, new Dictionary<string, string>
        {
            ["Nome"] = evt.Nome,
            ["Email"] = evt.Email
        });

    public EmailMessage? RenderPurchaseConfirmation(PaymentProcessedEvent evt)
    {
        if (!EmailMessageBuilder.IsApproved(evt.Status))
        {
            return null;
        }

        return Render(_purchaseConfirmation.Value, to: evt.UserId, new Dictionary<string, string>
        {
            ["GameId"] = evt.GameId,
            ["Price"] = evt.Price.ToString("C", new CultureInfo("pt-BR")),
            ["OrderId"] = evt.OrderId.ToString()
        });
    }

    private static EmailMessage Render(string template, string to, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            template = template.Replace("{{" + key + "}}", value);
        }

        var firstNewline = template.IndexOf('\n');
        var subject = (firstNewline < 0 ? template : template[..firstNewline]).TrimEnd('\r').Trim();
        var body = firstNewline < 0 ? string.Empty : template[(firstNewline + 1)..].Trim();

        return new EmailMessage(to, subject, body);
    }
}
