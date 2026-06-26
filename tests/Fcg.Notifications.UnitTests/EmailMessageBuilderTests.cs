using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using FluentAssertions;
using Xunit;

namespace Fcg.Notifications.UnitTests;

public class EmailMessageBuilderTests
{
    [Fact]
    public void BuildWelcomeMessage_deve_conter_email_e_nome()
    {
        var evt = new UserCreatedEvent
        {
            UserId = "u-1",
            Nome = "Maria",
            Email = "maria@exemplo.com"
        };

        var message = EmailMessageBuilder.BuildWelcomeMessage(evt);

        message.Should().Be("[E-mail] Boas-vindas enviado para maria@exemplo.com (usuário Maria)");
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("approved")]
    public void BuildPurchaseConfirmationMessage_quando_aprovado_deve_gerar_mensagem(string status)
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

        var message = EmailMessageBuilder.BuildPurchaseConfirmationMessage(evt);

        message.Should().NotBeNull();
        message.Should().Contain("u-1");
        message.Should().Contain("game-42");
        message.Should().Contain(orderId.ToString());
        message.Should().Contain("99,90");
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("")]
    [InlineData("Pending")]
    public void BuildPurchaseConfirmationMessage_quando_nao_aprovado_deve_retornar_null(string status)
    {
        var evt = new PaymentProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = "u-1",
            GameId = "game-42",
            Price = 99.90m,
            Status = status
        };

        var message = EmailMessageBuilder.BuildPurchaseConfirmationMessage(evt);

        message.Should().BeNull();
    }

    [Fact]
    public void BuildRejectedMessage_deve_indicar_que_nenhum_email_e_enviado()
    {
        var evt = new PaymentProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = "u-9",
            GameId = "game-7",
            Price = 10m,
            Status = "Rejected"
        };

        var message = EmailMessageBuilder.BuildRejectedMessage(evt);

        message.Should().Contain("Rejected");
        message.Should().Contain("nenhum e-mail de confirmação enviado");
        message.Should().Contain("u-9");
    }
}
