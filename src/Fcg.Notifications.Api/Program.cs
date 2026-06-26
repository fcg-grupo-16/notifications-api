using Fcg.Notifications.Consumers;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

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

app.Run();

// Necessário para expor a classe Program ao projeto de testes (WebApplicationFactory).
public partial class Program;
