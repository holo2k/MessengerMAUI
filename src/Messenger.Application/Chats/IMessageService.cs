using Messenger.Domain.Chats;
using Messenger.Domain.Music;
using Messenger.Domain.Media;

namespace Messenger.Application.Chats;

public sealed record MessageView(Message Message, string? Body, bool IsPinned, MusicTrack? MusicTrack = null,
    IReadOnlyList<StoredObject>? Attachments = null);

public interface IMessageStore
{
    Task<bool> IsHiddenAsync(Guid messageId, Guid userId, CancellationToken cancellationToken);
    Task AddAttachmentsAsync(Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<StoredObject>>> ListAttachmentsAsync(
        IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
    Task<Message?> FindByClientIdAsync(Guid senderId, Guid clientMessageId, CancellationToken cancellationToken);
    Task<Message?> FindMessageAsync(Guid messageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> ListMessagesAsync(
        Guid chatId,
        Guid userId,
        long afterSequence,
        int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> ListSearchCandidatesAsync(
        Guid chatId,
        Guid userId,
        long beforeSequence,
        int take,
        CancellationToken cancellationToken);
    Task AddMessageAsync(
        Message message,
        IReadOnlyCollection<MessageSearchToken> tokens,
        CancellationToken cancellationToken);
    Task ReplaceSearchTokensAsync(
        Guid messageId,
        IReadOnlyCollection<MessageSearchToken> tokens,
        CancellationToken cancellationToken);
    Task AddHiddenAsync(HiddenMessage hidden, CancellationToken cancellationToken);
    Task AddPinAsync(PinnedMessage pin, CancellationToken cancellationToken);
    Task<PinnedMessage?> FindPinAsync(Guid chatId, Guid messageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PinnedMessage>> ListPinsAsync(Guid chatId, CancellationToken cancellationToken);
    void RemovePin(PinnedMessage pin);
}

public interface IMessageService
{
    Task<MessageView> ForwardAsync(Guid senderUserId, Guid messageId, Guid targetChatId, Guid clientMessageId,
        CancellationToken cancellationToken = default);
    Task<MessageView> GetAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default);
    Task<MessageView> SendMusicAsync(Guid senderUserId, Guid chatId, Guid clientMessageId, Guid trackId,
        CancellationToken cancellationToken = default);
    Task<MessageView> SendAsync(
        Guid senderUserId,
        Guid chatId,
        Guid clientMessageId,
        MessageKind kind,
        string? body,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageView>> ListAsync(
        Guid requesterUserId,
        Guid chatId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
    Task<MessageView> EditAsync(
        Guid requesterUserId,
        Guid messageId,
        string body,
        CancellationToken cancellationToken = default);
    Task<Message> DeleteAsync(
        Guid requesterUserId,
        Guid messageId,
        bool globally,
        CancellationToken cancellationToken = default);
    Task HideAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default);
    Task PinAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default);
    Task UnpinAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageView>> ListPinsAsync(Guid requesterUserId, Guid chatId, CancellationToken cancellationToken = default);
}
