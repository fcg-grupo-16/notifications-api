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
/// Testes de integração do <see cref="UserCreatedConsumer"/> com o Test Harness
/// do MassTransit (broker em memória — não exige RabbitMQ real).
/// </summary>
public class UserCreatedConsumerTests
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
        .AddMassTransitTestHarness(x => x.AddConsumer<UserCreatedConsumer>())
        .BuildServiceProvider(true);
    }

    [Fact]
    public async Task UserCreatedEvent_deve_ser_consumido_e_enviar_boas_vindas()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new UserCreatedEvent
        {
            UserId = "u-1",
            Nome = "Maria",
            Email = "maria@exemplo.com"
        });

        (await harness.Consumed.Any<UserCreatedEvent>()).Should().BeTrue();
        var consumerHarness = harness.GetConsumerHarness<UserCreatedConsumer>();
        (await consumerHarness.Consumed.Any<UserCreatedEvent>()).Should().BeTrue();

        await harness.InactivityTask;

        var sender = provider.GetRequiredService<FakeEmailSender>();
        sender.Sent.Should().HaveCount(1);
        sender.Sent.Single().To.Should().Be("maria@exemplo.com");
        sender.Sent.Single().Body.Should().Contain("Maria");

        var repository = provider.GetRequiredService<FakeNotificationRepository>();
        repository.Saved.Should().ContainSingle(r => r.Type == "Welcome" && r.Status == "Sent");
    }

    [Fact]
    public async Task UserCreatedEvent_duplicado_deve_enviar_boas_vindas_apenas_uma_vez()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evento = new UserCreatedEvent
        {
            UserId = "u-dup",
            Nome = "Maria",
            Email = "maria@exemplo.com"
        };

        await harness.Bus.Publish(evento);
        await harness.Bus.Publish(evento);
        await harness.InactivityTask;

        var consumerHarness = harness.GetConsumerHarness<UserCreatedConsumer>();
        consumerHarness.Consumed.Select<UserCreatedEvent>().Count().Should().Be(2,
            "as duas entregas devem ser consumidas");

        provider.GetRequiredService<FakeEmailSender>().Sent.Should().HaveCount(1,
            "a idempotência deve descartar a reentrega");
    }
}
