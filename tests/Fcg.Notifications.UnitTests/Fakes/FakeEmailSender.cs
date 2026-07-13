using System.Collections.Concurrent;
using Fcg.Notifications.Email;

namespace Fcg.Notifications.UnitTests.Fakes;

/// <summary>Fake de <see cref="IEmailSender"/> que registra os envios para asserção.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
