using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using FluentAssertions;
using Xunit;

namespace Fcg.Notifications.UnitTests;

public class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void RenderWelcome_deve_enderecar_ao_email_e_conter_nome()
    {
        var evt = new UserCreatedEvent
        {
            UserId = "u-1",
            Nome = "Maria",
            Email = "maria@exemplo.com"
        };

        var email = _renderer.RenderWelcome(evt);

        email.To.Should().Be("maria@exemplo.com");
        email.Subject.Should().NotBeNullOrWhiteSpace();
        email.Body.Should().Contain("Maria");
        email.Body.Should().Contain("maria@exemplo.com");
        email.Body.Should().NotContain("{{", "todos os placeholders devem ser substituídos");
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("approved")]
    public void RenderPurchaseConfirmation_quando_aprovado_deve_gerar_email(string status)
    {
        var orderId = Guid.NewGuid();
        var evt = new PaymentProcessedEvent
        {
            OrderId = orderId,
            UserId = "u-1",
            GameId = "game-42",
            Price = 99.90m,
            Status = status
        };

        var email = _renderer.RenderPurchaseConfirmation(evt);

        email.Should().NotBeNull();
        email!.To.Should().Be("u-1");
        email.Body.Should().Contain("game-42");
        email.Body.Should().Contain(orderId.ToString());
        email.Body.Should().Contain("99,90");
        email.Body.Should().NotContain("{{", "todos os placeholders devem ser substituídos");
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("")]
    [InlineData("Pending")]
    public void RenderPurchaseConfirmation_quando_nao_aprovado_deve_retornar_null(string status)
    {
        var evt = new PaymentProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = "u-1",
            GameId = "game-42",
            Price = 99.90m,
            Status = status
        };

        _renderer.RenderPurchaseConfirmation(evt).Should().BeNull();
    }
}
