using Messenger.Domain.Chats;

namespace Messenger.Application.Chats;

public interface IChatStore
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<DirectChatPair?> FindDirectPairAsync(Guid lowerUserId, Guid higherUserId, CancellationToken cancellationToken);
    Task<Chat?> FindChatAsync(Guid chatId, CancellationToken cancellationToken);
    Task<ChatMember?> FindMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMember>> ListMembersAsync(Guid chatId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListActiveChatIdsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<(Chat Chat, ChatMember Member)>> ListChatsAsync(
        Guid userId,
        DateTimeOffset? afterUpdatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken);
    Task AddDirectAsync(Chat chat, DirectChatPair pair, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken);
    Task AddGroupAsync(Chat chat, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken);
    Task AddMemberAsync(ChatMember member, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ChatView(Chat Chat, ChatMember Member);
public sealed record ChatPage(IReadOnlyList<ChatView> Items, string? NextCursor);

public interface IChatService
{
    Task<Chat> CreateDirectAsync(Guid requesterUserId, Guid targetUserId, CancellationToken cancellationToken = default);
    Task<Chat> CreateGroupAsync(
        Guid ownerUserId,
        string title,
        IReadOnlyCollection<Guid> memberUserIds,
        CancellationToken cancellationToken = default);
    Task<ChatView> GetAsync(Guid requesterUserId, Guid chatId, CancellationToken cancellationToken = default);
    Task<ChatPage> ListAsync(Guid requesterUserId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task UpdateGroupAsync(
        Guid requesterUserId,
        Guid chatId,
        string title,
        string? avatarObjectId,
        CancellationToken cancellationToken = default);
    Task DeleteOrLeaveAsync(Guid requesterUserId, Guid chatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMember>> ListMembersAsync(Guid requesterUserId, Guid chatId, CancellationToken cancellationToken = default);
    Task AddMemberAsync(Guid requesterUserId, Guid chatId, Guid userId, CancellationToken cancellationToken = default);
    Task ChangeMemberRoleAsync(
        Guid requesterUserId,
        Guid chatId,
        Guid userId,
        ChatMemberRole role,
        CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid requesterUserId, Guid chatId, Guid userId, CancellationToken cancellationToken = default);
    Task SetArchivedAsync(Guid requesterUserId, Guid chatId, bool value, CancellationToken cancellationToken = default);
    Task SetMutedAsync(Guid requesterUserId, Guid chatId, bool value, CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid requesterUserId, Guid chatId, long sequence, CancellationToken cancellationToken = default);
    Task ReactivateDirectMembersAsync(Guid chatId, Guid senderUserId, CancellationToken cancellationToken = default);
}
