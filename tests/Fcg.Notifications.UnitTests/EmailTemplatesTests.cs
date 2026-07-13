using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using FluentAssertions;
using Xunit;

namespace Fcg.Notifications.UnitTests;

public class EmailTemplatesTests
{
    [Fact]
    public void Welcome_deve_enderecar_o_email_do_usuario_e_conter_o_nome()
    {
        var evt = new UserCreatedEvent
        {
            UserId = "u-1",
            Nome = "Maria",
            Email = "maria@exemplo.com"
        };

        var email = EmailTemplates.Welcome(evt);

        email.To.Should().Be("maria@exemplo.com");
        email.Subject.Should().NotBeNullOrWhiteSpace();
        email.Body.Should().Contain("Maria");
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("approved")]
    public void PurchaseConfirmation_quando_aprovado_deve_gerar_email(string status)
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

        var email = EmailTemplates.PurchaseConfirmation(evt);

        email.Should().NotBeNull();
        email!.To.Should().Be("u-1");
        email.Body.Should().Contain("game-42");
        email.Body.Should().Contain(orderId.ToString());
        email.Body.Should().Contain("99,90");
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("")]
    [InlineData("Pending")]
    public void PurchaseConfirmation_quando_nao_aprovado_deve_retornar_null(string status)
    {
        var evt = new PaymentProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = "u-1",
            GameId = "game-42",
            Price = 99.90m,
            Status = status
        };

        var email = EmailTemplates.PurchaseConfirmation(evt);

        email.Should().BeNull();
    }

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("approved", true)]
    [InlineData("Rejected", false)]
    [InlineData("", false)]
    public void IsApproved_deve_reconhecer_o_status_aprovado(string status, bool esperado)
    {
        EmailTemplates.IsApproved(status).Should().Be(esperado);
    }
}
