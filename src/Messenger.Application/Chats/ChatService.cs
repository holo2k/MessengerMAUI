using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Chats;

public sealed class ChatService(IChatStore store, IClock clock) : IChatService
{
    public async Task<Chat> CreateDirectAsync(
        Guid requesterUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (requesterUserId == targetUserId ||
            !await store.UserExistsAsync(targetUserId, cancellationToken))
        {
            throw new ChatRuleException(ChatRuleError.NotFound);
        }

        var candidate = DirectChatPair.Create(Guid.Empty, requesterUserId, targetUserId);
        var existingPair = await store.FindDirectPairAsync(
            candidate.LowerUserId, candidate.HigherUserId, cancellationToken);
        if (existingPair is not null)
        {
            var existingChat = await store.FindChatAsync(existingPair.ChatId, cancellationToken)
                ?? throw new ChatRuleException(ChatRuleError.NotFound);
            var member = await store.FindMemberAsync(existingPair.ChatId, requesterUserId, cancellationToken)
                ?? throw new ChatRuleException(ChatRuleError.NotFound);
            member.Reactivate();
            await store.SaveChangesAsync(cancellationToken);
            return existingChat;
        }

        var chat = new Chat(Guid.NewGuid(), ChatType.Direct, requesterUserId, null, clock.UtcNow);
        var pair = DirectChatPair.Create(chat.Id, requesterUserId, targetUserId);
        ChatMember[] members =
        [
            new(chat.Id, requesterUserId, ChatMemberRole.Member, clock.UtcNow),
            new(chat.Id, targetUserId, ChatMemberRole.Member, clock.UtcNow)
        ];
        await store.AddDirectAsync(chat, pair, members, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return chat;
    }

    public async Task<Chat> CreateGroupAsync(
        Guid ownerUserId,
        string title,
        IReadOnlyCollection<Guid> memberUserIds,
        CancellationToken cancellationToken = default)
    {
        var userIds = memberUserIds.Where(id => id != ownerUserId).Distinct().ToArray();
        foreach (var userId in userIds)
        {
            if (!await store.UserExistsAsync(userId, cancellationToken))
            {
                throw new ChatRuleException(ChatRuleError.NotFound);
            }
        }
        var chat = new Chat(Guid.NewGuid(), ChatType.Group, ownerUserId, title, clock.UtcNow);
        var members = new List<ChatMember>
        {
            new(chat.Id, ownerUserId, ChatMemberRole.Owner, clock.UtcNow)
        };
        members.AddRange(userIds.Select(userId =>
            new ChatMember(chat.Id, userId, ChatMemberRole.Member, clock.UtcNow)));
        await store.AddGroupAsync(chat, members, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return chat;
    }

    public async Task<ChatView> GetAsync(
        Guid requesterUserId,
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        var chat = await store.FindChatAsync(chatId, cancellationToken);
        var member = await store.FindMemberAsync(chatId, requesterUserId, cancellationToken);
        if (chat is null || chat.DeletedAt is not null || member is null || !member.IsActive)
        {
            throw new ChatRuleException(ChatRuleError.NotFound);
        }
        return new ChatView(chat, member);
    }

    public async Task<ChatPage> ListAsync(
        Guid requesterUserId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var (updatedAt, id) = DecodeCursor(cursor);
        var rows = await store.ListChatsAsync(requesterUserId, updatedAt, id, limit + 1, cancellationToken);
        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).Select(row => new ChatView(row.Chat, row.Member)).ToArray();
        return new ChatPage(page, hasMore ? EncodeCursor(page[^1].Chat) : null);
    }

    public async Task UpdateGroupAsync(
        Guid requesterUserId,
        Guid chatId,
        string title,
        string? avatarObjectId,
        CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(requesterUserId, chatId, cancellationToken);
        if (view.Member.Role != ChatMemberRole.Owner)
        {
            throw new ChatRuleException(ChatRuleError.Forbidden);
        }
        view.Chat.UpdateGroup(title, avatarObjectId, clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteOrLeaveAsync(
        Guid requesterUserId,
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(requesterUserId, chatId, cancellationToken);
        if (view.Chat.Type == ChatType.Direct)
        {
            view.Member.Hide(clock.UtcNow);
        }
        else if (view.Member.Role == ChatMemberRole.Owner)
        {
            view.Chat.Delete(clock.UtcNow);
        }
        else
        {
            view.Member.Leave(await store.ListMembersAsync(chatId, cancellationToken), clock.UtcNow);
        }
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMember>> ListMembersAsync(
        Guid requesterUserId,
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        await GetAsync(requesterUserId, chatId, cancellationToken);
        return (await store.ListMembersAsync(chatId, cancellationToken)).Where(member => member.IsActive).ToArray();
    }

    public async Task AddMemberAsync(
        Guid requesterUserId,
        Guid chatId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(requesterUserId, chatId, cancellationToken);
        if (view.Chat.Type != ChatType.Group)
        {
            throw new ChatRuleException(ChatRuleError.InvalidChatType);
        }
        if (view.Member.Role == ChatMemberRole.Member)
        {
            throw new ChatRuleException(ChatRuleError.Forbidden);
        }
        if (!await store.UserExistsAsync(userId, cancellationToken))
        {
            throw new ChatRuleException(ChatRuleError.NotFound);
        }
        var existing = await store.FindMemberAsync(chatId, userId, cancellationToken);
        if (existing is not null)
        {
            throw new ChatRuleException(ChatRuleError.AlreadyMember);
        }
        await store.AddMemberAsync(new ChatMember(chatId, userId, ChatMemberRole.Member, clock.UtcNow), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeMemberRoleAsync(
        Guid requesterUserId,
        Guid chatId,
        Guid userId,
        ChatMemberRole role,
        CancellationToken cancellationToken = default)
    {
        var actor = (await GetAsync(requesterUserId, chatId, cancellationToken)).Member;
        var target = await store.FindMemberAsync(chatId, userId, cancellationToken)
            ?? throw new ChatRuleException(ChatRuleError.NotFound);
        target.ChangeRole(actor, role, await store.ListMembersAsync(chatId, cancellationToken));
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(
        Guid requesterUserId,
        Guid chatId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var actor = (await GetAsync(requesterUserId, chatId, cancellationToken)).Member;
        var target = await store.FindMemberAsync(chatId, userId, cancellationToken)
            ?? throw new ChatRuleException(ChatRuleError.NotFound);
        var members = await store.ListMembersAsync(chatId, cancellationToken);
        if (requesterUserId == userId)
        {
            target.Leave(members, clock.UtcNow);
        }
        else
        {
            target.Remove(actor, members, clock.UtcNow);
        }
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task SetArchivedAsync(Guid requesterUserId, Guid chatId, bool value, CancellationToken cancellationToken = default)
    {
        var member = (await GetAsync(requesterUserId, chatId, cancellationToken)).Member;
        member.SetArchived(value);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task SetMutedAsync(Guid requesterUserId, Guid chatId, bool value, CancellationToken cancellationToken = default)
    {
        var member = (await GetAsync(requesterUserId, chatId, cancellationToken)).Member;
        member.SetMuted(value);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkReadAsync(Guid requesterUserId, Guid chatId, long sequence, CancellationToken cancellationToken = default)
    {
        var member = (await GetAsync(requesterUserId, chatId, cancellationToken)).Member;
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        member.MarkRead(sequence);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateDirectMembersAsync(
        Guid chatId,
        Guid senderUserId,
        CancellationToken cancellationToken = default)
    {
        var chat = await store.FindChatAsync(chatId, cancellationToken);
        if (chat?.Type != ChatType.Direct)
        {
            return;
        }
        foreach (var member in await store.ListMembersAsync(chatId, cancellationToken))
        {
            if (member.UserId != senderUserId)
            {
                member.Reactivate();
            }
        }
        await store.SaveChangesAsync(cancellationToken);
    }

    private static string EncodeCursor(Chat chat) => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{chat.UpdatedAt.UtcTicks}|{chat.Id:D}"));

    private static (DateTimeOffset? UpdatedAt, Guid? Id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (null, null);
        }
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split('|');
            return (new DateTimeOffset(long.Parse(parts[0]), TimeSpan.Zero), Guid.Parse(parts[1]));
        }
        catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException)
        {
            throw new FormatException("Invalid cursor.", exception);
        }
    }
}
