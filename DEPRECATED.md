# Deprecação — notifications-api

**Data:** 2026-09-14
**Substituto:** https://github.com/fcg-grupo-16/notifications-function
**Issue de origem:** https://github.com/fcg-grupo-16/notifications-api/issues/15
**Issue de deploy do substituto:** https://github.com/fcg-grupo-16/orchestration/issues/29

## Motivação

Requisito da Fase 3 do Tech Challenge: migrar a NotificationsAPI para uma arquitetura de Funções como
Serviço, acionada diretamente por mensagens da fila, substituindo o container que rodava
continuamente.

## O que mudou tecnicamente

1. **Host.** De `WebApplication` + `MassTransit.AddConsumer<>` para **Azure Functions** (isolated
   worker) com o binding `[RabbitMQTrigger]`.
2. **Topologia da fila.** O MassTransit criava exchanges/filas/bindings no startup. O binding
   `RabbitMQTrigger` **não cria** nada — só consome de uma fila existente. A topologia passou a ser
   declarativa, em `docker/rabbitmq/definitions.json` no repo `orchestration`
   (ver [`orchestration#28`](https://github.com/fcg-grupo-16/orchestration/issues/28)), com
   dead-letter queue explícita.
3. **Idempotência.** `InMemoryProcessedMessageStore` (um `ConcurrentDictionary`) **não funciona** numa
   função que escala a zero: o estado morre a cada ciclo, e a próxima entrega do mesmo evento
   reenviaria o e-mail. Virou store durável em **Redis** (`SET NX` + TTL), numa instância **dedicada**
   com `noeviction` e AOF — um Redis de cache, com `allkeys-lru`, despejaria chave de idempotência sob
   pressão de memória.
4. **Retry / dead-letter.** Saiu do `UseMessageRetry`/`UseDelayedRedelivery` do MassTransit e passou
   para `host.json` (retry do host de Functions) + `x-dead-letter-exchange` na fila. O orçamento ficou
   mais curto: **5 tentativas em ~20 s**, limite fixo da extensão.
5. **Escala.** De `replicas: 1` fixo para `minReplicaCount: 0` / `maxReplicaCount: 5` via **KEDA**, com
   o tamanho da fila como métrica. Medido no cluster: o pod nasce em ~10 s do evento e volta a zero
   ~70 s depois.

## O que NÃO mudou

- Os contratos de evento (`Fcg.Contracts.Events`) são **byte-idênticos** — nenhum outro serviço da
  plataforma foi afetado pela migração.
- O `notificationsdb` no MongoDB é o mesmo, com o mesmo formato de documento: o histórico de
  notificações da Fase 2 continua legível.
- Os templates de e-mail e a abstração `IEmailSender` foram portados sem mudança de comportamento.

## O que mudou DEPOIS do port

Registrado aqui porque quem comparar os dois códigos vai notar a diferença, e ela não veio do port:

- **A confirmação de compra era endereçada ao `UserId`**, não a um e-mail — comportamento herdado
  deste repositório, já que o `PaymentProcessedEvent` não carrega endereço. Corrigido na
  [`notifications-function#9`](https://github.com/fcg-grupo-16/notifications-function/issues/9): a
  Function passou a resolver o contato num endpoint interno do `users-api`, com credencial de serviço
  própria. Se você estiver lendo este código como referência, **não porte esse comportamento**.

## Nota sobre o CI

O gatilho de `push` foi removido; `pull_request` e `workflow_dispatch` ficaram. A issue #15 pedia
"só `workflow_dispatch`", mas isso é incompatível com a proteção da `main` deste próprio repositório:
`build-and-test` é check **obrigatório**, com `enforce_admins` ligado — sem ele reportando, nenhum PR
pode ser mergeado, inclusive o que deprecou o repo. Como repositório arquivado não aceita PR, manter
o gatilho custa zero e preserva a proteção como ela foi configurada.

## Por que o repositório não foi deletado

Ele é o registro histórico de como a plataforma era na Fase 2, e a `notifications-function` foi
portada a partir daqui. Apagar destruiria a rastreabilidade da refatoração — que é justamente o que o
desafio pede para demonstrar. O caminho é o mesmo que o grupo usou com o monólito `fiap-cloud-games`:
deprecar e arquivar.
