# notifications-api

Microsserviço de notificações do **FIAP Cloud Games (FCG) — Fase 2**.

## Propósito

Consome eventos publicados pelos demais serviços e **simula o envio de e-mails registrando as mensagens no console** (`ILogger`):

- `UserCreatedEvent` → registra um e-mail de **boas-vindas** (pt-BR) para o novo usuário.
- `PaymentProcessedEvent` → se `Status == "Approved"`, registra um e-mail de **confirmação de compra** (pt-BR) com `GameId`, `Price` e `UserId`. Se `Rejected` (ou qualquer outro status), registra que **nenhum** e-mail de confirmação é enviado.

A mensageria usa **MassTransit + RabbitMQ**. Os contratos de evento ficam em `src/Fcg.Notifications.Api/Contracts/Events.cs`, no namespace `Fcg.Contracts.Events`, idêntico em todos os serviços FCG (a identidade do tipo é o que permite o MassTransit casar as mensagens entre serviços).

## Stack

- .NET 10 (`net10.0`)
- ASP.NET Core (minimal hosting) — expõe apenas `/health` e hospeda os consumidores
- MassTransit 8.x + RabbitMQ
- xUnit + FluentAssertions (testes)

## Variáveis de ambiente

| Variável                 | Descrição                          | Padrão      |
|--------------------------|------------------------------------|-------------|
| `RabbitMq__Host`         | Host do RabbitMQ                    | `localhost` |
| `RabbitMq__Username`     | Usuário do RabbitMQ                 | `guest`     |
| `RabbitMq__Password`     | Senha do RabbitMQ                   | `guest`     |
| `ASPNETCORE_ENVIRONMENT` | Ambiente (`Development`/`Production`) | —         |
| `ASPNETCORE_URLS`        | URLs de escuta (definida no Dockerfile) | `http://+:8080` |

As variáveis usam o separador de duplo sublinhado (`__`) para mapear seções aninhadas da configuração.

## Como executar

### Local (dotnet)

```bash
dotnet run --project src/Fcg.Notifications.Api
```

O serviço escuta em `http://localhost:8080` (ou na porta configurada localmente) e expõe o health check em `/health`. É necessário um RabbitMQ acessível em `RabbitMq__Host`.

### Build e testes

```bash
dotnet build -c Release
dotnet test -c Release
```

### Docker

```bash
docker build -t notifications-api:local .
docker run -p 8080:8080 -e RabbitMq__Host=host.docker.internal notifications-api:local
```

### Kubernetes

```bash
kubectl apply -f k8s/
```

Aplica `configmap.yaml`, `secret.yaml`, `deployment.yaml` e `service.yaml`. O serviço expõe a porta `80` internamente (ClusterIP) encaminhando para a `8080` do contêiner.

## Health check

```bash
curl http://localhost:8080/health
```
