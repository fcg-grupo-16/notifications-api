using Fcg.Contracts.Events;
using Fcg.Notifications.Email;
using FluentAssertions;
using Xunit;

namespace Fcg.Notifications.UnitTests;

public class EmailMessageBuilderTests
{
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

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("approved", true)]
    [InlineData("APPROVED", true)]
    [InlineData("Rejected", false)]
    [InlineData("", false)]
    public void IsApproved_deve_comparar_sem_distincao_de_maiusculas(string status, bool esperado)
    {
        EmailMessageBuilder.IsApproved(status).Should().Be(esperado);
    }
}
