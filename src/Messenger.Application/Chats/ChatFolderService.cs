using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Chats;

public interface IChatFolderStore
{
    Task<IReadOnlyList<ChatFolder>> ListFoldersAsync(Guid userId, CancellationToken cancellationToken);
    Task<ChatFolder?> FindOwnedFolderAsync(Guid userId, Guid folderId, CancellationToken cancellationToken);
    Task<bool> IsActiveMemberAsync(Guid userId, Guid chatId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListFolderChatIdsAsync(Guid folderId, CancellationToken cancellationToken);
    Task AddFolderAsync(ChatFolder folder, CancellationToken cancellationToken);
    void RemoveFolder(ChatFolder folder);
    Task ReplaceFolderItemsAsync(Guid folderId, IReadOnlyCollection<Guid> chatIds, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ChatFolderView(ChatFolder Folder, IReadOnlyList<Guid> ChatIds);

public interface IChatFolderService
{
    Task<IReadOnlyList<ChatFolderView>> ListAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ChatFolderView> CreateAsync(Guid userId, string title, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid userId, Guid folderId, string title, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, Guid folderId, CancellationToken cancellationToken = default);
    Task ReorderAsync(Guid userId, IReadOnlyList<Guid> folderIds, CancellationToken cancellationToken = default);
    Task SetChatsAsync(Guid userId, Guid folderId, IReadOnlyCollection<Guid> chatIds, CancellationToken cancellationToken = default);
}

public sealed class ChatFolderService(IChatFolderStore store, IClock clock) : IChatFolderService
{
    public async Task<IReadOnlyList<ChatFolderView>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var folders = await store.ListFoldersAsync(userId, cancellationToken);
        var result = new List<ChatFolderView>(folders.Count);
        foreach (var folder in folders)
        {
            result.Add(new ChatFolderView(folder,
                await store.ListFolderChatIdsAsync(folder.Id, cancellationToken)));
        }
        return result;
    }

    public async Task<ChatFolderView> CreateAsync(
        Guid userId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var folders = await store.ListFoldersAsync(userId, cancellationToken);
        var folder = new ChatFolder(Guid.NewGuid(), userId, title, folders.Count, clock.UtcNow);
        await store.AddFolderAsync(folder, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new ChatFolderView(folder, []);
    }

    public async Task UpdateAsync(Guid userId, Guid folderId, string title, CancellationToken cancellationToken = default)
    {
        var folder = await RequireOwnedAsync(userId, folderId, cancellationToken);
        folder.Rename(title);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await RequireOwnedAsync(userId, folderId, cancellationToken);
        store.RemoveFolder(folder);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderAsync(
        Guid userId,
        IReadOnlyList<Guid> folderIds,
        CancellationToken cancellationToken = default)
    {
        var folders = await store.ListFoldersAsync(userId, cancellationToken);
        if (folderIds.Count != folders.Count || folderIds.Distinct().Count() != folders.Count ||
            !folderIds.ToHashSet().SetEquals(folders.Select(folder => folder.Id)))
        {
            throw new ChatRuleException(ChatRuleError.NotFound);
        }
        var byId = folders.ToDictionary(folder => folder.Id);
        for (var position = 0; position < folderIds.Count; position++)
        {
            byId[folderIds[position]].ChangePosition(position);
        }
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task SetChatsAsync(
        Guid userId,
        Guid folderId,
        IReadOnlyCollection<Guid> chatIds,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnedAsync(userId, folderId, cancellationToken);
        var distinct = chatIds.Distinct().ToArray();
        foreach (var chatId in distinct)
        {
            if (!await store.IsActiveMemberAsync(userId, chatId, cancellationToken))
            {
                throw new ChatRuleException(ChatRuleError.NotFound);
            }
        }
        await store.ReplaceFolderItemsAsync(folderId, distinct, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<ChatFolder> RequireOwnedAsync(Guid userId, Guid folderId, CancellationToken cancellationToken) =>
        await store.FindOwnedFolderAsync(userId, folderId, cancellationToken)
        ?? throw new ChatRuleException(ChatRuleError.NotFound);
}
