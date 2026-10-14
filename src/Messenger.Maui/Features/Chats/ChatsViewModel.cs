using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

public sealed class ChatListItem(
    ChatResponse chat,
    int unreadCount,
    Guid currentUserId = default,
    Func<DateTimeOffset>? now = null) : ObservableObject
{
    private ChatResponse _chat = chat;
    private int _unreadCount = unreadCount;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    public ChatResponse Chat => _chat;
    public string Title => Chat.Type == "direct"
        ? Chat.Peer?.DisplayName ?? "Пользователь"
        : string.IsNullOrWhiteSpace(Chat.Title) ? "Групповой чат" : Chat.Title;
    public string Preview
    {
        get
        {
            if (Chat.LastMessage is not { } message)
            {
                return "Пока нет сообщений";
            }

            var value = message.DeletedAt.HasValue
                ? "Сообщение удалено"
                : message.Type switch
                {
                    "text" => string.IsNullOrWhiteSpace(message.Body) ? "Сообщение" : message.Body,
                    "image" => "Фото",
                    "video" => "Видео",
                    "audio" => "Аудио",
                    "file" => "Файл",
                    _ => "Сообщение"
                };
            return currentUserId != Guid.Empty && message.SenderUserId == currentUserId
                ? $"Вы: {value}"
                : value;
        }
    }
    public string DateText
    {
        get
        {
            if (Chat.LastMessage is not { } message)
            {
                return string.Empty;
            }

            var localNow = _now();
            var localCreated = message.CreatedAt.ToOffset(localNow.Offset);
            return localCreated.Date == localNow.Date
                ? localCreated.ToString("HH:mm")
                : localCreated.ToString("dd.MM.yy");
        }
    }
    public int UnreadCount { get => _unreadCount; private set => SetProperty(ref _unreadCount, value); }
    public void Update(ChatResponse value)
    {
        _chat = value;
        OnPropertyChanged(nameof(Chat));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DateText));
    }
    public void IncrementUnread() => UnreadCount++;
    public void MarkRead() => UnreadCount = 0;
}

public sealed class ChatsViewModel(IConversationApi api)
{
    private Guid? _selectedFolderId;
    private Guid _currentUserId;
    public ObservableCollection<ChatFolderResponse> Folders { get; } = [];
    public ObservableCollection<ChatListItem> Chats { get; } = [];
    public ObservableCollection<ChatListItem> VisibleChats { get; } = [];

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
            Chats.Add(new ChatListItem(chat, 0, _currentUserId));
        }
        SelectFolder(_selectedFolderId);
    }

    public void SelectFolder(Guid? folderId)
    {
        _selectedFolderId = folderId;
        var folder = Folders.FirstOrDefault(value => value.Id == folderId);
        VisibleChats.Clear();
        foreach (var item in Chats.Where(item => !item.Chat.IsHidden && !item.Chat.IsArchived &&
                     (folder is null || folder.ChatIds.Contains(item.Chat.Id)))
                 .OrderByDescending(item => item.Chat.LastMessage?.CreatedAt ?? item.Chat.UpdatedAt))
        {
            VisibleChats.Add(item);
        }
    }

    public void SetCurrentUser(Guid userId) => _currentUserId = userId;

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
