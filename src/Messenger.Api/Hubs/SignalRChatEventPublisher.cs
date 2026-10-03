using Messenger.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Hubs;

public sealed class SignalRChatEventPublisher(IHubContext<ChatHub> hub) : IChatEventPublisher
{
    public Task MessageCreatedAsync(MessageCreatedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "MessageCreated", message, cancellationToken);
    public Task MessageUpdatedAsync(MessageUpdatedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "MessageUpdated", message, cancellationToken);
    public Task MessageDeletedAsync(MessageDeletedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "MessageDeleted", message, cancellationToken);
    public Task ReadPositionChangedAsync(ReadPositionChangedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "ReadPositionChanged", message, cancellationToken);
    public Task ChatChangedAsync(ChatChangedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "ChatChanged", message, cancellationToken);
    public Task MemberChangedAsync(MemberChangedEvent message, CancellationToken cancellationToken = default) =>
        PublishAsync(message.ChatId, "MemberChanged", message, cancellationToken);

    private Task PublishAsync<T>(Guid chatId, string method, T payload, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChatHub.GroupName(chatId)).SendAsync(method, payload, cancellationToken);
}
