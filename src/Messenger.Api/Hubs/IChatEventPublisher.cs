using Messenger.Contracts.Realtime;

namespace Messenger.Api.Hubs;

public interface IChatEventPublisher
{
    Task MessageCreatedAsync(MessageCreatedEvent message, CancellationToken cancellationToken = default);
    Task MessageUpdatedAsync(MessageUpdatedEvent message, CancellationToken cancellationToken = default);
    Task MessageDeletedAsync(MessageDeletedEvent message, CancellationToken cancellationToken = default);
    Task ReadPositionChangedAsync(ReadPositionChangedEvent message, CancellationToken cancellationToken = default);
    Task ChatChangedAsync(ChatChangedEvent message, CancellationToken cancellationToken = default);
    Task MemberChangedAsync(MemberChangedEvent message, CancellationToken cancellationToken = default);
}
