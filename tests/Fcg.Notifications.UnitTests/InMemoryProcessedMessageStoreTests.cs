using Fcg.Notifications.Idempotency;
using FluentAssertions;
using Xunit;

namespace Fcg.Notifications.UnitTests;

public class InMemoryProcessedMessageStoreTests
{
    [Fact]
    public async Task TryMarkAsProcessedAsync_primeira_chamada_deve_retornar_inedito()
    {
        var store = new InMemoryProcessedMessageStore();

        var isNew = await store.TryMarkAsProcessedAsync("UserCreatedEvent", "u-1");

        isNew.Should().BeTrue();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_segunda_chamada_com_mesma_chave_deve_retornar_duplicado()
    {
        var store = new InMemoryProcessedMessageStore();

        await store.TryMarkAsProcessedAsync("UserCreatedEvent", "u-1");
        var isNew = await store.TryMarkAsProcessedAsync("UserCreatedEvent", "u-1");

        isNew.Should().BeFalse();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_chaves_diferentes_devem_ser_independentes()
    {
        var store = new InMemoryProcessedMessageStore();

        await store.TryMarkAsProcessedAsync("UserCreatedEvent", "u-1");
        var isNew = await store.TryMarkAsProcessedAsync("UserCreatedEvent", "u-2");

        isNew.Should().BeTrue();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_mesma_chave_em_tipos_diferentes_deve_ser_independente()
    {
        var store = new InMemoryProcessedMessageStore();

        await store.TryMarkAsProcessedAsync("UserCreatedEvent", "abc");
        var isNew = await store.TryMarkAsProcessedAsync("PaymentProcessedEvent", "abc");

        isNew.Should().BeTrue();
    }

    [Fact]
    public async Task TryMarkAsProcessedAsync_chamadas_concorrentes_apenas_uma_deve_ser_inedita()
    {
        var store = new InMemoryProcessedMessageStore();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => store.TryMarkAsProcessedAsync("PaymentProcessedEvent", "pedido-1"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
    }
}
