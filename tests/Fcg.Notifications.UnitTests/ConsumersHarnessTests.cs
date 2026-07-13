using Fcg.Contracts.Events;
using Fcg.Notifications.Consumers;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fcg.Notifications.UnitTests;

/// <summary>
/// Testes de integração dos consumers com o MassTransit Test Harness (transporte
/// EM MEMÓRIA, sem RabbitMQ real). Exercita o comportamento de ponta a ponta:
/// "o evento certo gera a notificação certa", a regra crítica "confirmação só
/// quando Approved", e a idempotência (reentrega não duplica o e-mail).
/// </summary>
public class ConsumersHarnessTests
{
    /// <summary>Spy do <see cref="IEmailSender"/>: registra os e-mails "enviados" para asserção.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sent = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<EmailMessage> Sent
        {
            get { lock (_gate) { return _sent.ToList(); } }
        }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (_gate) { _sent.Add(message); }
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildHarness(RecordingEmailSender emailSender) =>
        new ServiceCollection()
            .AddSingleton<IEmailSender>(emailSender)
            .AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>()
            .AddSingleton<INotificationHistoryStore, InMemoryNotificationHistoryStore>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<UserCreatedConsumer>();
                x.AddConsumer<PaymentProcessedConsumer>();
            })
            .BuildServiceProvider(true);

    // Aguarda o consumer processar `esperado` mensagens do tipo T (com timeout), sem depender
    // de Task.Delay. Evita a ambiguidade de Take entre System.Linq.Async e MassTransit.
    private static async Task AguardarConsumidas<TConsumer, TMessage>(ITestHarness harness, int esperado)
        where TConsumer : class, IConsumer
        where TMessage : class
    {
        var consumerHarness = harness.GetConsumerHarness<TConsumer>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var vistas = 0;
        await foreach (var _ in consumerHarness.Consumed.SelectAsync<TMessage>(cts.Token))
        {
            if (++vistas >= esperado)
            {
                break;
            }
        }
    }

    [Fact]
    public async Task UserCreated_deve_enviar_email_de_boas_vindas_ao_email_do_usuario()
    {
        var email = new RecordingEmailSender();
        await using var provider = BuildHarness(email);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new UserCreatedEvent
            {
                UserId = "u-1",
                Nome = "Maria",
                Email = "maria@exemplo.com"
            });

            (await harness.Consumed.Any<UserCreatedEvent>()).Should().BeTrue();
            email.Sent.Should().ContainSingle();
            email.Sent[0].To.Should().Be("maria@exemplo.com");
            email.Sent[0].Body.Should().Contain("Maria");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task UserCreated_reentregue_deve_enviar_email_apenas_uma_vez()
    {
        var email = new RecordingEmailSender();
        await using var provider = BuildHarness(email);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var evt = new UserCreatedEvent { UserId = "u-dup", Nome = "Ana", Email = "ana@exemplo.com" };

            await harness.Bus.Publish(evt);
            await harness.Bus.Publish(evt); // reentrega do MESMO evento

            // Garante que AMBAS as mensagens foram processadas antes de asserir a idempotência.
            await AguardarConsumidas<UserCreatedConsumer, UserCreatedEvent>(harness, 2);

            email.Sent.Should().ContainSingle("a idempotência deve suprimir a duplicata");
            email.Sent[0].To.Should().Be("ana@exemplo.com");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task PaymentProcessed_aprovado_deve_enviar_confirmacao()
    {
        var email = new RecordingEmailSender();
        await using var provider = BuildHarness(email);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            await harness.Bus.Publish(new PaymentProcessedEvent
            {
                OrderId = orderId,
                UserId = "u-1",
                GameId = "game-42",
                Price = 49.90m,
                Status = "Approved"
            });

            (await harness.Consumed.Any<PaymentProcessedEvent>()).Should().BeTrue();
            email.Sent.Should().ContainSingle();
            email.Sent[0].To.Should().Be("u-1");
            email.Sent[0].Body.Should().Contain(orderId.ToString());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task UserCreated_deve_persistir_o_historico_da_notificacao()
    {
        var email = new RecordingEmailSender();
        await using var provider = BuildHarness(email);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new UserCreatedEvent
            {
                UserId = "u-hist",
                Nome = "Bea",
                Email = "bea@exemplo.com"
            });

            await AguardarConsumidas<UserCreatedConsumer, UserCreatedEvent>(harness, 1);

            var history = provider.GetRequiredService<INotificationHistoryStore>();
            var registros = await history.GetRecentAsync(10);

            registros.Should().ContainSingle();
            registros[0].Type.Should().Be(nameof(UserCreatedEvent));
            registros[0].Recipient.Should().Be("bea@exemplo.com");
            registros[0].NaturalKey.Should().Be("u-hist");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task PaymentProcessed_rejeitado_nao_deve_enviar_email()
    {
        var email = new RecordingEmailSender();
        await using var provider = BuildHarness(email);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new PaymentProcessedEvent
            {
                OrderId = Guid.NewGuid(),
                UserId = "u-9",
                GameId = "game-7",
                Price = 10m,
                Status = "Rejected"
            });

            // Espera o consumer processar o evento; nenhum e-mail deve ter sido enviado.
            await AguardarConsumidas<PaymentProcessedConsumer, PaymentProcessedEvent>(harness, 1);
            email.Sent.Should().BeEmpty("nenhuma confirmação é enviada quando o pagamento não é aprovado");
        }
        finally
        {
            await harness.Stop();
        }
    }
}
