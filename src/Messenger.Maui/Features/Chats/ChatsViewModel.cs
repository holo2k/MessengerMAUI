using System.Collections.ObjectModel;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Realtime;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public enum ChatBulkAction
{
    Archive,
    MarkRead,
    Delete
}

public sealed class ChatListItem(ChatResponse chat, int unreadCount)
{
    public ChatResponse Chat { get; } = chat;
    public int UnreadCount { get; private set; } = unreadCount;
    public void IncrementUnread() => UnreadCount++;
    public void MarkRead() => UnreadCount = 0;
}

public sealed class ChatsViewModel(IConversationApi api)
{
    public ObservableCollection<ChatFolderResponse> Folders { get; } = [];
    public ObservableCollection<ChatListItem> Chats { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Folders.Clear();
        foreach (var folder in await api.GetFoldersAsync(cancellationToken))
        {
            Folders.Add(folder);
        }
        Chats.Clear();
        foreach (var chat in await api.GetChatsAsync(cancellationToken))
        {
            Chats.Add(new ChatListItem(chat, 0));
        }
    }

    public async Task MoveFolderAsync(int oldIndex, int newIndex, CancellationToken cancellationToken = default)
    {
        if (oldIndex == newIndex)
        {
            return;
        }
        Folders.Move(oldIndex, newIndex);
        await api.ReorderFoldersAsync(Folders.Select(folder => folder.Id).ToArray(), cancellationToken);
    }

    public async Task ApplyBulkAsync(
        IReadOnlyCollection<Guid> chatIds,
        ChatBulkAction action,
        CancellationToken cancellationToken = default)
    {
        foreach (var chatId in chatIds)
        {
            switch (action)
            {
                case ChatBulkAction.Archive:
                    await api.SetArchivedAsync(chatId, true, cancellationToken);
                    break;
                case ChatBulkAction.MarkRead:
                    var item = Chats.SingleOrDefault(value => value.Chat.Id == chatId);
                    await api.MarkReadAsync(chatId, item?.Chat.CurrentMessageSequence ?? 0, cancellationToken);
                    item?.MarkRead();
                    break;
                case ChatBulkAction.Delete:
                    await api.DeleteChatAsync(chatId, cancellationToken);
                    break;
            }
        }
    }

    public async Task<ChatResponse> CreateChatAsync(
        IReadOnlyList<Guid> userIds,
        string? groupTitle,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 1)
        {
            return await api.CreateDirectAsync(userIds[0], cancellationToken);
        }
        if (userIds.Count < 2 || string.IsNullOrWhiteSpace(groupTitle))
        {
            throw new ArgumentException("Для группы выберите минимум двух участников и задайте название.");
        }
        return await api.CreateGroupAsync(groupTitle.Trim(), userIds, cancellationToken);
    }

    public void OnMessageCreated(MessageCreatedEvent message, Guid? openChatId)
    {
        if (message.ChatId == openChatId)
        {
            return;
        }
        Chats.SingleOrDefault(item => item.Chat.Id == message.ChatId)?.IncrementUnread();
    }
}
