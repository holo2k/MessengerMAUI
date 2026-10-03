using Messenger.Application.Chats;
using Messenger.Application.Contacts;
using Messenger.Domain.Chats;
using Messenger.Domain.Contacts;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Social;

public sealed class EfSocialStore(MessengerDbContext dbContext) : IContactStore, IChatStore, IChatFolderStore
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    public Task<Contact?> FindByUsersAsync(Guid ownerUserId, Guid targetUserId, CancellationToken cancellationToken) =>
        dbContext.Contacts.SingleOrDefaultAsync(contact =>
            contact.OwnerUserId == ownerUserId && contact.TargetUserId == targetUserId, cancellationToken);

    public Task<Contact?> FindOwnedAsync(Guid ownerUserId, Guid contactId, CancellationToken cancellationToken) =>
        dbContext.Contacts.SingleOrDefaultAsync(contact =>
            contact.OwnerUserId == ownerUserId && contact.Id == contactId, cancellationToken);

    public async Task<IReadOnlyList<Contact>> ListAsync(
        Guid ownerUserId,
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Contacts.Where(contact => contact.OwnerUserId == ownerUserId)
            .OrderBy(contact => contact.CreatedAt).ThenBy(contact => contact.Id)
            .ToListAsync(cancellationToken);
        return rows.Where(contact => afterCreatedAt is null || contact.CreatedAt > afterCreatedAt ||
                contact.CreatedAt == afterCreatedAt && contact.Id.CompareTo(afterId!.Value) > 0)
            .Take(take).ToArray();
    }

    public Task<Guid?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken) =>
        dbContext.UserProfiles.Where(profile => profile.NormalizedUsername == normalizedUsername)
            .Select(profile => (Guid?)profile.UserId).SingleOrDefaultAsync(cancellationToken);

    public Task<Guid?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken) =>
        dbContext.Users.Where(user => user.PhoneBlindIndex == phoneIndex)
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Contact contact, CancellationToken cancellationToken) =>
        await dbContext.Contacts.AddAsync(contact, cancellationToken);

    public void Remove(Contact contact) => dbContext.Contacts.Remove(contact);

    public Task<DirectChatPair?> FindDirectPairAsync(Guid lowerUserId, Guid higherUserId, CancellationToken cancellationToken) =>
        dbContext.DirectChatPairs.SingleOrDefaultAsync(pair =>
            pair.LowerUserId == lowerUserId && pair.HigherUserId == higherUserId, cancellationToken);

    public Task<Chat?> FindChatAsync(Guid chatId, CancellationToken cancellationToken) =>
        dbContext.Chats.SingleOrDefaultAsync(chat => chat.Id == chatId, cancellationToken);

    public Task<ChatMember?> FindMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.ChatMembers.SingleOrDefaultAsync(member =>
            member.ChatId == chatId && member.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ChatMember>> ListMembersAsync(Guid chatId, CancellationToken cancellationToken) =>
        await dbContext.ChatMembers.Where(member => member.ChatId == chatId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(Chat Chat, ChatMember Member)>> ListChatsAsync(
        Guid userId,
        DateTimeOffset? afterUpdatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await (from member in dbContext.ChatMembers
            join chat in dbContext.Chats on member.ChatId equals chat.Id
            where member.UserId == userId && member.LeftAt == null && member.HiddenAt == null && chat.DeletedAt == null
            orderby chat.UpdatedAt descending, chat.Id descending
            select new { Chat = chat, Member = member }).ToListAsync(cancellationToken);
        return rows.Where(row => afterUpdatedAt is null || row.Chat.UpdatedAt < afterUpdatedAt ||
                row.Chat.UpdatedAt == afterUpdatedAt && row.Chat.Id.CompareTo(afterId!.Value) < 0)
            .Take(take).Select(row => (row.Chat, row.Member)).ToArray();
    }

    public Task AddDirectAsync(Chat chat, DirectChatPair pair, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken)
    {
        dbContext.Chats.Add(chat);
        dbContext.DirectChatPairs.Add(pair);
        dbContext.ChatMembers.AddRange(members);
        return Task.CompletedTask;
    }

    public Task AddGroupAsync(Chat chat, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken)
    {
        dbContext.Chats.Add(chat);
        dbContext.ChatMembers.AddRange(members);
        return Task.CompletedTask;
    }

    public async Task AddMemberAsync(ChatMember member, CancellationToken cancellationToken) =>
        await dbContext.ChatMembers.AddAsync(member, cancellationToken);

    public async Task<IReadOnlyList<ChatFolder>> ListFoldersAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.ChatFolders.Where(folder => folder.UserId == userId)
            .OrderBy(folder => folder.Position).ToListAsync(cancellationToken);

    public Task<ChatFolder?> FindOwnedFolderAsync(Guid userId, Guid folderId, CancellationToken cancellationToken) =>
        dbContext.ChatFolders.SingleOrDefaultAsync(folder => folder.UserId == userId && folder.Id == folderId, cancellationToken);

    public Task<bool> IsActiveMemberAsync(Guid userId, Guid chatId, CancellationToken cancellationToken) =>
        dbContext.ChatMembers.AnyAsync(member =>
            member.UserId == userId && member.ChatId == chatId && member.LeftAt == null, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListFolderChatIdsAsync(Guid folderId, CancellationToken cancellationToken) =>
        await dbContext.ChatFolderItems.Where(item => item.FolderId == folderId)
            .OrderBy(item => item.ChatId).Select(item => item.ChatId).ToListAsync(cancellationToken);

    public async Task AddFolderAsync(ChatFolder folder, CancellationToken cancellationToken) =>
        await dbContext.ChatFolders.AddAsync(folder, cancellationToken);

    public void RemoveFolder(ChatFolder folder) => dbContext.ChatFolders.Remove(folder);

    public async Task ReplaceFolderItemsAsync(Guid folderId, IReadOnlyCollection<Guid> chatIds, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ChatFolderItems.Where(item => item.FolderId == folderId)
            .ToListAsync(cancellationToken);
        dbContext.ChatFolderItems.RemoveRange(existing);
        await dbContext.ChatFolderItems.AddRangeAsync(
            chatIds.Select(chatId => new ChatFolderItem(folderId, chatId)), cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
