# NotificationsAPI — FIAP Cloud Games

Microsserviço de notificações da plataforma **FIAP Cloud Games (FCG) — Fase 2**: consome eventos de outros serviços e simula o envio de e-mails (boas-vindas e confirmação de compra) registrando as mensagens no console.

![CI](https://github.com/fcg-grupo-16/notifications-api/actions/workflows/ci.yml/badge.svg)

---

## 1. Visão geral

O NotificationsAPI não tem interface própria nem banco de dados. Ele apenas **escuta eventos** publicados por outros microsserviços do FCG via RabbitMQ e reage a eles. O "envio" de e-mail é **simulado**: em vez de disparar um e-mail real, a mensagem é gravada no log do console (`ILogger`). Isso é suficiente para a Fase 2 e fácil de auditar.

Há **dois consumidores** (consumers):

| Consumer | Evento consumido | O que faz |
|----------|------------------|-----------|
| `UserCreatedConsumer` | `UserCreatedEvent` | Registra no log um e-mail de **boas-vindas** (pt-BR) para o novo usuário. |
| `PaymentProcessedConsumer` | `PaymentProcessedEvent` | Se `Status == "Approved"`, registra um e-mail de **confirmação de compra** (pt-BR). Caso contrário (`Rejected` ou qualquer outro status), registra que **nenhum** e-mail de confirmação foi enviado. |

> **Regra importante:** o e-mail de **confirmação de compra só é enviado quando o pagamento foi aprovado** (`Approved`, comparação sem distinção de maiúsculas/minúsculas). Para pagamentos rejeitados, apenas uma linha informativa é registrada — nenhuma confirmação sai.

### Exemplos de mensagens registradas

```text
[E-mail] Boas-vindas enviado para maria@exemplo.com (usuário Maria)
[E-mail] Confirmação de compra enviada para o usuário u-1: jogo game-42 adquirido por R$ 99,90 (pedido 7c9e...).
[E-mail] Pagamento Rejected para o pedido 7c9e... (usuário u-1): nenhum e-mail de confirmação enviado.
```

### Campos dos eventos consumidos

**`UserCreatedEvent`** (publicado pelo UsersAPI):

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `UserId` | `string` | Identificador do usuário |
| `Nome` | `string` | Nome do usuário |
| `Email` | `string` | E-mail do usuário (destinatário das boas-vindas) |

**`PaymentProcessedEvent`** (publicado pelo PaymentsAPI):

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `OrderId` | `Guid` | Identificador do pedido |
| `UserId` | `string` | Identificador do usuário comprador |
| `GameId` | `string` | Identificador do jogo adquirido |
| `Price` | `decimal` | Valor da compra (formatado como `R$` na confirmação) |
| `Status` | `string` | `"Approved"` ou `"Rejected"` |

Os contratos vivem em `src/Fcg.Notifications.Api/Contracts/Events.cs`, no namespace **`Fcg.Contracts.Events`**. Esse namespace e os nomes dos tipos **devem ser idênticos em todos os serviços FCG** — o MassTransit identifica a mensagem pela URN derivada de `namespace:NomeDoTipo` (ex.: `urn:message:Fcg.Contracts.Events:UserCreatedEvent`). Qualquer divergência quebra a interoperabilidade entre serviços.

---

## 2. Stack

- **.NET 10** (`net10.0`)
- **ASP.NET Core** (minimal hosting) — projeto único que hospeda os consumers e expõe apenas `/health`
- **MassTransit 8.x** + **RabbitMQ** (mensageria pub/sub)
- **xUnit** + **FluentAssertions** (testes de unidade)
- **Sem banco de dados** — o serviço é stateless

---

## 3. Arquitetura

```text
notifications-api/
├── src/
│   └── Fcg.Notifications.Api/
│       ├── Consumers/
│       │   ├── UserCreatedConsumer.cs        # consome UserCreatedEvent → boas-vindas
│       │   └── PaymentProcessedConsumer.cs   # consome PaymentProcessedEvent → confirmação (se Approved)
│       ├── Contracts/
│       │   └── Events.cs                      # contratos compartilhados (Fcg.Contracts.Events)
│       ├── Email/
│       │   └── EmailMessageBuilder.cs         # helper puro que monta as mensagens (pt-BR)
│       ├── Program.cs                         # bootstrap: MassTransit + RabbitMQ + /health
│       ├── appsettings.json
│       └── appsettings.Development.json
├── tests/
│   └── Fcg.Notifications.UnitTests/
│       └── EmailMessageBuilderTests.cs        # testa o EmailMessageBuilder
├── k8s/                                       # manifests Kubernetes
│   ├── configmap.yaml
│   ├── secret.yaml
│   ├── deployment.yaml
│   └── service.yaml
├── Dockerfile
├── global.json
└── NotificationsApi.sln
```

**Como as peças se conectam:**

- **`Program.cs`** registra os dois consumers no MassTransit e configura a conexão com o RabbitMQ. Usa `KebabCaseEndpointNameFormatter("notifications", false)`, ou seja, as filas recebem o prefixo `notifications-` — isso garante filas **distintas por serviço**, então cada microsserviço recebe sua própria cópia do evento (fanout pub/sub, e não competing consumers).
- **`UserCreatedConsumer`** e **`PaymentProcessedConsumer`** são finos de propósito: cada um recebe o evento e delega a construção da mensagem ao `EmailMessageBuilder`, escrevendo o resultado no log.
- **`EmailMessageBuilder`** é uma classe estática **pura** (sem dependências, sem efeitos colaterais), o que a torna fácil de testar isoladamente. Ela expõe:
  - `BuildWelcomeMessage(UserCreatedEvent)` — mensagem de boas-vindas.
  - `BuildPurchaseConfirmationMessage(PaymentProcessedEvent)` — confirmação quando aprovado; retorna `null` quando o status não é `Approved`.
  - `BuildRejectedMessage(PaymentProcessedEvent)` — mensagem informando que nenhuma confirmação foi enviada.

---

## 4. Pré-requisitos

- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download))
- **Docker** (para subir o RabbitMQ localmente e/ou rodar o serviço em contêiner)
- **RabbitMQ** acessível. A forma mais simples é subir via Docker:

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

- A porta **5672** é a do protocolo AMQP (usada pelo serviço).
- A porta **15672** é a do painel de administração web (acesse http://localhost:15672, usuário/senha `guest`/`guest`).

---

## 5. Variáveis de ambiente

A configuração usa o separador de **duplo sublinhado** (`__`) para mapear seções aninhadas (ex.: `RabbitMq__Host` → seção `RabbitMq`, chave `Host`).

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `RabbitMq__Host` | Host do RabbitMQ | `localhost` |
| `RabbitMq__Username` | Usuário do RabbitMQ | `guest` |
| `RabbitMq__Password` | Senha do RabbitMQ | `guest` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente de execução (`Development` / `Production`) | — |
| `ASPNETCORE_URLS` | URLs de escuta (definida no Dockerfile) | `http://+:8080` |

---

## 6. Como rodar localmente (dotnet)

1. **Suba o RabbitMQ** (se ainda não estiver rodando):

   ```bash
   docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
   ```

2. **Rode o serviço:**

   ```bash
   dotnet run --project src/Fcg.Notifications.Api
   ```

   O serviço escuta em `http://localhost:8080` e expõe o health check em `/health`. Se o RabbitMQ não estiver em `localhost`, defina `RabbitMq__Host` antes de rodar.

3. **Verifique a saúde:**

   ```bash
   curl http://localhost:8080/health
   ```

4. **Veja os e-mails simulados.** Quando um `UserCreatedEvent` ou `PaymentProcessedEvent` chegar, o console exibirá linhas como:

   ```text
   info: Fcg.Notifications.Consumers.UserCreatedConsumer[0]
         [E-mail] Boas-vindas enviado para maria@exemplo.com (usuário Maria)
   ```

   Para ver isso de ponta a ponta, é preciso que outro serviço publique os eventos — veja a seção [Rodar o ecossistema completo](#9-rodar-o-ecossistema-completo-end-to-end).

---

## 7. Como rodar com Docker

```bash
# Build da imagem
docker build -t notifications-api:local .

# Run (mapeia a porta 8080 do contêiner para 8084 no host)
docker run --rm -p 8084:8080 \
  -e RabbitMq__Host=host.docker.internal \
  notifications-api:local
```

- O contêiner escuta na **8080** internamente (`ASPNETCORE_URLS=http://+:8080`); o exemplo acima a publica em `http://localhost:8084`.
- `host.docker.internal` permite que o contêiner alcance um RabbitMQ rodando na máquina host.

---

## 9. Rodar o ecossistema completo (end-to-end)

O NotificationsAPI só reage a eventos publicados por outros serviços. Para vê-lo funcionando de verdade, use o repositório de **orquestração**, que sobe todo o FCG (RabbitMQ + todos os microsserviços) com um único comando:

- Repositório: **https://github.com/fcg-grupo-16/orchestration**

```bash
git clone https://github.com/fcg-grupo-16/orchestration
cd orchestration
docker compose up
```

**Como disparar cada notificação:**

1. **E-mail de boas-vindas** → **cadastre um usuário** no UsersAPI. O UsersAPI publica `UserCreatedEvent`, o NotificationsAPI consome e registra a mensagem de boas-vindas.
2. **E-mail de confirmação de compra** → **conclua uma compra aprovada** (faça o pedido no CatalogAPI e deixe o PaymentsAPI processá-lo como `Approved`). O PaymentsAPI publica `PaymentProcessedEvent` com `Status = "Approved"`, e o NotificationsAPI registra a confirmação. Se o pagamento for `Rejected`, apenas a linha informativa é registrada (nenhuma confirmação).

**Onde ver os logs:**

```bash
docker compose logs -f notifications-api
```

As mensagens `[E-mail] ...` aparecerão nesse log. Você também pode inspecionar as filas e mensagens pelo painel do RabbitMQ em http://localhost:15672 (`guest`/`guest`).

---

## 10. Eventos consumidos

| Evento | Publicado por | Quando | Reação do NotificationsAPI | Campos |
|--------|---------------|--------|----------------------------|--------|
| `UserCreatedEvent` | UsersAPI | Novo usuário cadastrado | Registra e-mail de boas-vindas | `UserId` (`string`), `Nome` (`string`), `Email` (`string`) |
| `PaymentProcessedEvent` | PaymentsAPI | Pagamento processado | Registra confirmação **somente se `Status == "Approved"`**; caso contrário registra "nenhum e-mail enviado" | `OrderId` (`Guid`), `UserId` (`string`), `GameId` (`string`), `Price` (`decimal`), `Status` (`string`) |

---

## 11. Testes

```bash
dotnet test
```

A suíte de testes de unidade cobre o **`EmailMessageBuilder`**, validando:

- O texto da mensagem de boas-vindas.
- Que a confirmação de compra é gerada quando o status é `Approved` (incluindo variações de caixa, ex.: `approved`).
- Que nenhuma confirmação é gerada para outros status.
- O texto da mensagem de pagamento rejeitado.

Para build e teste em modo Release (como na CI):

```bash
dotnet build -c Release
dotnet test -c Release --no-build
```

---

## 12. Como contribuir

1. Abra (ou pegue) uma **issue** descrevendo a mudança.
2. Crie uma **branch** a partir da `main` seguindo o padrão:
   - `feat/<n>-descricao-curta` para novas funcionalidades
   - `fix/<n>-descricao-curta` para correções

   (onde `<n>` é o número da issue)
3. Faça commits no padrão **Conventional Commits** (ex.: `feat: adiciona consumer X`, `fix: corrige formatação do preço`).
4. Abra um **Pull Request** para a `main` referenciando a issue com `Closes #n`.
5. Garanta que a **CI esteja verde** (build + testes).
6. Após aprovação, faça o **merge**.

> **Nunca** comite segredos (senhas, tokens, credenciais) no repositório. Use variáveis de ambiente, Secrets do Kubernetes ou Secrets do GitHub Actions.

---

## 13. Deploy de versão

O versionamento segue **SemVer** por tag (`vX.Y.Z`).

1. **Build da imagem** com a tag de versão:

   ```bash
   docker build -t ghcr.io/fcg-grupo-16/notifications-api:vX.Y.Z .
   ```

2. **Push para o GHCR** (GitHub Container Registry):

   ```bash
   docker push ghcr.io/fcg-grupo-16/notifications-api:vX.Y.Z
   ```

3. **Atualize a imagem no Kubernetes**, editando `image:` em `k8s/deployment.yaml`:

   ```yaml
   image: ghcr.io/fcg-grupo-16/notifications-api:vX.Y.Z
   ```

   ou diretamente via `kubectl`:

   ```bash
   kubectl set image deployment/notifications-api \
     notifications-api=ghcr.io/fcg-grupo-16/notifications-api:vX.Y.Z -n fcg
   ```

4. **Aplique** (se editou os manifests):

   ```bash
   kubectl apply -f k8s/ -n fcg
   ```

---

## 14. Kubernetes

Os manifests ficam em `k8s/`:

| Arquivo | Recurso | Função |
|---------|---------|--------|
| `configmap.yaml` | ConfigMap | Config não sensível: `RabbitMq__Host`, `ASPNETCORE_ENVIRONMENT` |
| `secret.yaml` | Secret | Credenciais: `RabbitMq__Username`, `RabbitMq__Password` |
| `deployment.yaml` | Deployment | 1 réplica; injeta ConfigMap/Secret via `envFrom`; liveness/readiness probes em `/health` na porta `8080` |
| `service.yaml` | Service (ClusterIP) | Expõe a porta `80` internamente, encaminhando para a `8080` do contêiner |

Aplicação isolada (para testes pontuais):

```bash
kubectl apply -f k8s/ -n fcg
```

> Para o deploy **agregado** de todo o ecossistema FCG, use o repositório **[orchestration](https://github.com/fcg-grupo-16/orchestration)**, que orquestra o deploy de todos os serviços em conjunto.

---

## 15. Troubleshooting

**O serviço não inicia / fica reiniciando — RabbitMQ indisponível.**
O NotificationsAPI depende do RabbitMQ. Confirme que ele está rodando e acessível:

```bash
docker ps | grep rabbitmq
curl -s http://localhost:15672 >/dev/null && echo "painel OK"
```

Verifique se `RabbitMq__Host`, `RabbitMq__Username` e `RabbitMq__Password` apontam para o broker correto. Em Docker, lembre de usar `host.docker.internal` (ou o nome do serviço no compose) em vez de `localhost`.

**O serviço sobe mas não recebe eventos.**
- Confirme que os outros serviços (UsersAPI, PaymentsAPI) estão **publicando** os eventos no **mesmo** RabbitMQ.
- O namespace/nome dos contratos deve ser **idêntico** ao dos publicadores (`Fcg.Contracts.Events`). Divergência impede o casamento das mensagens.
- Inspecione as filas no painel do RabbitMQ (http://localhost:15672). Devem existir filas com prefixo `notifications-`.
- Para confirmação de compra, lembre que ela **só sai com `Status == "Approved"`** — um pagamento `Rejected` não gera e-mail de confirmação (apenas a linha informativa).

**Porta 8080 já em uso.**
Outro processo pode estar ocupando a `8080`. Rode em outra porta:

```bash
ASPNETCORE_URLS=http://+:8085 dotnet run --project src/Fcg.Notifications.Api
```

Ou, no Docker, mapeie outra porta no host (ex.: `-p 8084:8080`).
