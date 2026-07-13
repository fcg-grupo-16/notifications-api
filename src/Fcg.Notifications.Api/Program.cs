using Fcg.Notifications.Consumers;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

// O driver 3.x não assume representação de Guid — sem isso, serializar payloads
// com Guid (ex.: OrderId do PaymentProcessedEvent) falha com
// "GuidRepresentation is Unspecified". Padrão UUID (Standard) é o recomendado.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

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

// Canal de e-mail plugável: o conteúdo vem de templates (ITemplateRenderer) e o
// envio da abstração IEmailSender — Console (default, loga "[E-mail] ...") ou
// SMTP real (MailKit), selecionável por Email__Provider sem tocar nos consumers.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
switch ((builder.Configuration["Email:Provider"] ?? "Console").ToLowerInvariant())
{
    case "smtp":
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        break;
    default:
        builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        break;
}

builder.Services.AddMassTransit(x =>
{
    // Prefixo por serviço garante filas distintas entre microsserviços que
    // consomem o mesmo evento (pub/sub fanout, não competing consumers).
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notifications", false));
    x.AddConsumer<UserCreatedConsumer>();
    x.AddConsumer<PaymentProcessedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var host = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var user = builder.Configuration["RabbitMq:Username"] ?? "guest";
        var pass = builder.Configuration["RabbitMq:Password"] ?? "guest";
        cfg.Host(host, "/", h => { h.Username(user); h.Password(pass); });
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.MapHealthChecks("/health");

// Garante o índice único de idempotência no startup (operação idempotente).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<MongoProcessedMessageStore>().GarantirIndicesAsync();
}

app.Run();

// Necessário para expor a classe Program ao projeto de testes (WebApplicationFactory).
public partial class Program;
