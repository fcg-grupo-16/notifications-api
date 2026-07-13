using Fcg.Notifications.Consumers;
using MassTransit;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Config do RabbitMQ (reutilizada pela mensageria e pelo health check).
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

// Conexão RabbitMQ ÚNICA e reutilizada pelo health check. Uma factory que abre conexão
// nova a cada readiness sem fechá-la vaza conexões e satura o broker (fix v0.9.0 da
// plataforma — mesmo padrão de users/catalog/payments). A factory cria a conexão UMA vez
// e a reusa em todas as checagens — com auto-recovery para reconectar quando o broker
// volta. O lock (double-checked) evita a criação concorrente se dois probes chegarem
// simultaneamente; se a conexão estiver fechada (recovery esgotado) ela é descartada e
// RECRIADA na próxima check — por isso não usamos Lazy<Task<IConnection>>, que cachearia
// uma Task falhada (broker fora no 1º check) e deixaria o readiness preso em 503 mesmo
// após o broker voltar. Lazy e assíncrona (sem sync-over-async, sem bloquear o startup).
var healthRabbitLock = new SemaphoreSlim(1, 1);
IConnection? healthRabbitConnection = null;

// Health check da DEPENDÊNCIA (RabbitMQ), tagueado "ready" (entra no /health/ready).
builder.Services.AddHealthChecks()
    .AddRabbitMQ(
        factory: async sp =>
        {
            if (healthRabbitConnection?.IsOpen == true)
                return healthRabbitConnection;

            await healthRabbitLock.WaitAsync();
            try
            {
                if (healthRabbitConnection?.IsOpen == true)
                    return healthRabbitConnection;

                // A conexão anterior está fechada (recovery esgotado) — descarta antes de recriar.
                if (healthRabbitConnection is not null)
                {
                    await healthRabbitConnection.DisposeAsync();
                    healthRabbitConnection = null;
                }

                healthRabbitConnection = await new ConnectionFactory
                {
                    HostName = rabbitHost,
                    UserName = rabbitUser,
                    Password = rabbitPass,
                    Port = 5672,
                    AutomaticRecoveryEnabled = true
                }.CreateConnectionAsync();
                return healthRabbitConnection;
            }
            finally
            {
                healthRabbitLock.Release();
            }
        },
        name: "rabbitmq",
        tags: ["ready"]);

builder.Services.AddMassTransit(x =>
{
    // Prefixo por serviço garante filas distintas entre microsserviços que
    // consomem o mesmo evento (pub/sub fanout, não competing consumers).
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notifications", false));
    x.AddConsumer<UserCreatedConsumer>();
    x.AddConsumer<PaymentProcessedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h => { h.Username(rabbitUser); h.Password(rabbitPass); });
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();


app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health");

app.Run();

// Necessário para expor a classe Program ao projeto de testes (WebApplicationFactory).
public partial class Program;
