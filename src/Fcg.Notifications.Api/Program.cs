using System.Globalization;
using Fcg.Notifications.Consumers;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using RabbitMQ.Client;

// O driver 3.x não assume representação de Guid — sem isso, serializar payloads
// com Guid (ex.: OrderId do PaymentProcessedEvent) falha com
// "GuidRepresentation is Unspecified". Padrão UUID (Standard) é o recomendado.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

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

// MongoDB — histórico de notificações e chaves de idempotência. Config por
// env vars (MongoDb__ConnectionString / MongoDb__Database), com default local.
var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
var mongoDatabase = builder.Configuration["MongoDb:Database"] ?? "notifications";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabase));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<NotificationRecord>("notifications"));
builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();

// Store de idempotência dos consumers (dedup por chave natural do evento),
// agora DURÁVEL: MongoDB com índice único (messageType, naturalKey) —
// sobrevive a restart e vale entre réplicas. A versão em memória
// (InMemoryProcessedMessageStore) permanece disponível para testes.
builder.Services.AddSingleton<MongoProcessedMessageStore>();
builder.Services.AddSingleton<IProcessedMessageStore>(sp => sp.GetRequiredService<MongoProcessedMessageStore>());

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

        // Scheduler de mensagens atrasadas — usa o plugin rabbitmq_delayed_message_exchange do
        // broker (imagem custom do orchestration, v0.7.0). Necessário para o delayed redelivery.
        cfg.UseDelayedMessageScheduler();

        // Redelivery atrasado (second-level retry): esgotado o retry imediato, a mensagem é
        // devolvida ao broker com intervalos CRESCENTES (default 60/300/900s) antes de ir para a
        // _error. Configurável via RabbitMq:DelayedRedeliverySeconds (curto em testes).
        cfg.UseDelayedRedelivery(r => r.Intervals(ParseDelayedIntervals(
            builder.Configuration["RabbitMq:DelayedRedeliverySeconds"])));

        // Retry imediato (first-level), EXPONENCIAL com limite. Esgotados retry + redelivery, a
        // mensagem vai para a fila _error do endpoint (dead-letter), sem ser perdida.
        // Só aceita um inteiro >= 0; negativo/inválido cai no default (3) — não derruba o startup.
        var immediateRetries = int.TryParse(builder.Configuration["RabbitMq:ImmediateRetryCount"], out var ir) && ir >= 0 ? ir : 3;
        cfg.UseMessageRetry(r => r.Exponential(immediateRetries,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(3)));

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

// Garante o índice único de idempotência no startup (operação idempotente).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<MongoProcessedMessageStore>().GarantirIndicesAsync();
}

app.Run();

// Parse tolerante de RabbitMq:DelayedRedeliverySeconds. Ignora entradas inválidas/não-positivas e
// faz fallback para os defaults (60/300/900s) se ausente/vazia/toda inválida — config ruim não
// pode derrubar o serviço no startup.
static TimeSpan[] ParseDelayedIntervals(string? raw)
{
    var defaults = new[] { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(900) };
    if (string.IsNullOrWhiteSpace(raw))
    {
        return defaults;
    }

    var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                && v > 0 && double.IsFinite(v) && v <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(v)
            : (TimeSpan?)null)
        .Where(t => t.HasValue)
        .Select(t => t!.Value)
        .ToArray();

    return parsed.Length > 0 ? parsed : defaults;
}

// Necessário para expor a classe Program ao projeto de testes (WebApplicationFactory).
public partial class Program;
