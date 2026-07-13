using Fcg.Contracts.Events;
using Fcg.Notifications.Consumers;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using Fcg.Notifications.UnitTests.Fakes;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Notifications.UnitTests;

/// <summary>
/// Testes de integração do <see cref="PaymentProcessedConsumer"/> com o Test
/// Harness do MassTransit, cobrindo a regra crítica de negócio: confirmação
/// de compra só sai quando <c>Status == "Approved"</c>.
/// </summary>
public class PaymentProcessedConsumerTests
{
    private static ServiceProvider BuildProvider()
    {
        BsonConfig.EnsureGuidSerialization();
        return new ServiceCollection()
        .AddLogging()
        .AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>()
        .AddSingleton<FakeNotificationRepository>()
        .AddSingleton<INotificationRepository>(sp => sp.GetRequiredService<FakeNotificationRepository>())
        .AddSingleton<ITemplateRenderer, TemplateRenderer>()
        .AddSingleton<FakeEmailSender>()
        .AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>())
        .AddMassTransitTestHarness(x => x.AddConsumer<PaymentProcessedConsumer>())
        .BuildServiceProvider(true);
    }

    private static PaymentProcessedEvent NovoEvento(string status, Guid? orderId = null) => new()
    {
        OrderId = orderId ?? Guid.NewGuid(),
        UserId = "u-1",
        GameId = "game-42",
        Price = 99.90m,
        Status = status
    };

    [Fact]
    public async Task Approved_deve_enviar_confirmacao_e_persistir_Sent()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(NovoEvento("Approved", orderId));

        var consumerHarness = harness.GetConsumerHarness<PaymentProcessedConsumer>();
        (await consumerHarness.Consumed.Any<PaymentProcessedEvent>()).Should().BeTrue();
        await harness.InactivityTask;

        var sender = provider.GetRequiredService<FakeEmailSender>();
        sender.Sent.Should().HaveCount(1);
        sender.Sent.Single().To.Should().Be("u-1");
        sender.Sent.Single().Body.Should().Contain(orderId.ToString());

        provider.GetRequiredService<FakeNotificationRepository>().Saved
            .Should().ContainSingle(r => r.Type == "PurchaseConfirmation" && r.Status == "Sent");
    }

    [Fact]
    public async Task Rejected_nao_deve_enviar_confirmacao_e_persistir_Skipped()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NovoEvento("Rejected"));

        var consumerHarness = harness.GetConsumerHarness<PaymentProcessedConsumer>();
        (await consumerHarness.Consumed.Any<PaymentProcessedEvent>()).Should().BeTrue();
        await harness.InactivityTask;

        provider.GetRequiredService<FakeEmailSender>().Sent.Should().BeEmpty(
            "pagamento não aprovado não gera e-mail de confirmação");

        provider.GetRequiredService<FakeNotificationRepository>().Saved
            .Should().ContainSingle(r => r.Type == "PurchaseConfirmation" && r.Status == "Skipped");
    }

    [Fact]
    public async Task Approved_duplicado_deve_enviar_confirmacao_apenas_uma_vez()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evento = NovoEvento("Approved");
        await harness.Bus.Publish(evento);
        await harness.Bus.Publish(evento);
        await harness.InactivityTask;

        var consumerHarness = harness.GetConsumerHarness<PaymentProcessedConsumer>();
        consumerHarness.Consumed.Select<PaymentProcessedEvent>().Count().Should().Be(2,
            "as duas entregas devem ser consumidas");

        provider.GetRequiredService<FakeEmailSender>().Sent.Should().HaveCount(1,
            "a idempotência por OrderId deve descartar a reentrega");
    }

    [Fact]
    public async Task Rejected_seguido_de_Approved_do_mesmo_pedido_deve_enviar_confirmacao()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(NovoEvento("Rejected", orderId));
        await harness.Bus.Publish(NovoEvento("Approved", orderId));
        await harness.InactivityTask;

        provider.GetRequiredService<FakeEmailSender>().Sent.Should().HaveCount(1,
            "o caminho Rejected não consome a chave de idempotência do pedido");
    }
}
