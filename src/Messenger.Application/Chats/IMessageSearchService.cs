namespace Messenger.Application.Chats;

public interface IMessageSearchService
{
    Task<IReadOnlyList<MessageView>> SearchAsync(
        Guid requesterUserId,
        Guid chatId,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}
