using System.Globalization;
using Fcg.Notifications.Consumers;
using Fcg.Notifications.Email;
using Fcg.Notifications.Idempotency;
using Fcg.Notifications.Persistence;
using MassTransit;
using MongoDB.Driver;
using RabbitMQ.Client;

BsonConfig.EnsureGuidSerialization();

var builder = WebApplication.CreateBuilder(args);

// Config do RabbitMQ (reutilizada pela mensageria e pelo health check).
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

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


var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
var mongoDatabase = builder.Configuration["MongoDb:Database"] ?? "notifications";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabase));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<NotificationRecord>("notifications"));
builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();

builder.Services.AddSingleton<MongoProcessedMessageStore>();
builder.Services.AddSingleton<IProcessedMessageStore>(sp => sp.GetRequiredService<MongoProcessedMessageStore>());

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
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notifications", false));
    x.AddConsumer<UserCreatedConsumer>();
    x.AddConsumer<PaymentProcessedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h => { h.Username(rabbitUser); h.Password(rabbitPass); });
       
        cfg.UseDelayedMessageScheduler();
  
        cfg.UseDelayedRedelivery(r => r.Intervals(ParseDelayedIntervals(
            builder.Configuration["RabbitMq:DelayedRedeliverySeconds"])));
    
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

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<MongoProcessedMessageStore>().GarantirIndicesAsync();
}
app.Run();


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

    return parsed.Length > 0 ? parsed : defaults;}


public partial class Program;
