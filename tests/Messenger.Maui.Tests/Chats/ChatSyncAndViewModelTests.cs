using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Realtime;
using Messenger.Contracts.Media;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Features.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Chats;

public sealed class ChatSyncAndViewModelTests
{
    [Fact]
    public async Task Reconnect_reconciles_two_missed_messages_exactly_once_in_sequence()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        var realtime = new FakeRealtimeClient();
        var cursors = new MemoryCursorStore { Cursor = 10 };
        var sync = new ChatSyncService(api, realtime, cursors);
        var received = new List<long>();
        var states = new List<ChatConnectionState>();
        sync.MessageReceived += message => { received.Add(message.Sequence); return Task.CompletedTask; };
        sync.ConnectionStateChanged += states.Add;
        await sync.StartAsync(chatId);
        api.Messages.Add(Message(chatId, 11));
        api.Messages.Add(Message(chatId, 12));

        await realtime.ReconnectAsync();
        await realtime.EmitAsync(new MessageCreatedEvent(chatId, api.Messages[1].Id, 12));

        Assert.Equal([11L, 12L], received);
        Assert.Equal(12, cursors.Cursor);
        Assert.Equal([ChatConnectionState.Connecting, ChatConnectionState.Connected], states);
    }

    [Fact]
    public async Task Chat_list_reorders_folders_runs_bulk_actions_and_creates_direct_or_group()
    {
        var api = new FakeConversationApi();
        var firstFolder = new ChatFolderResponse(Guid.NewGuid(), "Первый", 0, []);
        var secondFolder = new ChatFolderResponse(Guid.NewGuid(), "Второй", 1, []);
        var viewModel = new ChatsViewModel(api);
        viewModel.Folders.Add(firstFolder);
        viewModel.Folders.Add(secondFolder);
        var chatIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await viewModel.MoveFolderAsync(1, 0);
        await viewModel.ApplyBulkAsync(chatIds, ChatBulkAction.Archive);
        await viewModel.CreateChatAsync([Guid.NewGuid()], null);
        await viewModel.CreateChatAsync([Guid.NewGuid(), Guid.NewGuid()], "Команда");

        Assert.Equal([secondFolder.Id, firstFolder.Id], viewModel.Folders.Select(folder => folder.Id));
        Assert.Equal(2, api.Archived.Count);
        Assert.Equal(1, api.DirectCreates);
        Assert.Equal(1, api.GroupCreates);
    }

    [Fact]
    public async Task Unread_badge_increments_for_closed_chat_and_bulk_read_resets_it()
    {
        var api = new FakeConversationApi();
        var viewModel = new ChatsViewModel(api);
        var chat = Chat(Guid.NewGuid());
        viewModel.Chats.Add(new ChatListItem(chat, 0));

        viewModel.OnMessageCreated(new MessageCreatedEvent(chat.Id, Guid.NewGuid(), 3), openChatId: null);
        await viewModel.ApplyBulkAsync([chat.Id], ChatBulkAction.MarkRead);

        Assert.Equal(0, viewModel.Chats.Single().UnreadCount);
        Assert.Equal([chat.Id], api.ReadChats);
    }

    [Fact]
    public async Task Composer_is_preserved_on_failure_and_search_and_pins_update_results()
    {
        var api = new FakeConversationApi { SendFailure = new HttpRequestException("offline") };
        var chatId = Guid.NewGuid();
        api.SearchResults.Add(Message(chatId, 4));
        api.PinResults.Add(Message(chatId, 2));
        var viewModel = new ChatViewModel(chatId, api) { ComposerText = "Не потерять" };

        await Assert.ThrowsAsync<HttpRequestException>(() => viewModel.SendAsync());
        await viewModel.SearchAsync("точное");
        await viewModel.LoadPinsAsync();

        Assert.Equal("Не потерять", viewModel.ComposerText);
        Assert.Equal(4, Assert.Single(viewModel.SearchResults).Sequence);
        Assert.Equal(2, Assert.Single(viewModel.Pins).Sequence);
    }

    [Fact]
    public async Task Contact_alias_mute_and_phone_call_use_owner_local_update_and_tel_uri()
    {
        var api = new FakeConversationApi();
        var dialer = new RecordingDialer();
        var contact = new ContactResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Иван", "Иванов", "ivan", "+79991234567", null, null,
            null, null, false, DateTimeOffset.UtcNow);
        var viewModel = new ContactsViewModel(api, dialer);

        await viewModel.UpdateContactAsync(contact, "Врач", "Иванов", true);
        await viewModel.CallAsync(contact);

        Assert.Equal("Врач", api.LastContactUpdate?.LocalFirstName);
        Assert.True(api.LastContactUpdate?.IsMuted);
        Assert.Equal("tel:+79991234567", dialer.LastUri);
    }

    private static MessageResponse Message(Guid chatId, long sequence) => new(
        Guid.NewGuid(), chatId, Guid.NewGuid(), sequence, Guid.NewGuid(), "text", $"m{sequence}", false,
        DateTimeOffset.UtcNow, null, null);

    private static ChatResponse Chat(Guid id) => new(
        id, "direct", null, null, "member", false, false, false, 0, DateTimeOffset.UtcNow);

    private sealed class FakeRealtimeClient : IChatRealtimeClient
    {
        public event Func<MessageCreatedEvent, Task>? MessageCreated;
        public event Func<Task>? Reconnected;
        public event Action<ChatConnectionState>? ConnectionStateChanged;
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connecting);
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connected);
            return Task.CompletedTask;
        }
        public Task EmitAsync(MessageCreatedEvent value) => MessageCreated?.Invoke(value) ?? Task.CompletedTask;
        public Task ReconnectAsync() => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    private sealed class MemoryCursorStore : IChatCursorStore
    {
        public long Cursor { get; set; }
        public Task<long> GetAsync(Guid chatId, CancellationToken cancellationToken = default) => Task.FromResult(Cursor);
        public Task SaveAsync(Guid chatId, long cursor, CancellationToken cancellationToken = default)
        {
            Cursor = cursor;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDialer : IPhoneDialer
    {
        public string? LastUri { get; private set; }
        public Task OpenAsync(string uri)
        {
            LastUri = uri;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConversationApi : IConversationApi
    {
        public List<MessageResponse> Messages { get; } = [];
        public List<MessageResponse> SearchResults { get; } = [];
        public List<MessageResponse> PinResults { get; } = [];
        public List<Guid> Archived { get; } = [];
        public List<Guid> ReadChats { get; } = [];
        public int DirectCreates { get; private set; }
        public int GroupCreates { get; private set; }
        public Exception? SendFailure { get; set; }
        public UpdateContactRequest? LastContactUpdate { get; private set; }

        public Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatResponse>>([]);
        public Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatFolderResponse>>([]);
        public Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContactResponse>>([]);
        public Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UserSearchResponse>>([]);

        public Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MessageResponse>>(Messages.Where(message => message.ChatId == chatId && message.Sequence > afterSequence).OrderBy(message => message.Sequence).ToArray());
        public Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default) { Archived.Add(chatId); return Task.CompletedTask; }
        public Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default) { ReadChats.Add(chatId); return Task.CompletedTask; }
        public Task DeleteChatAsync(Guid chatId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChatResponse> CreateDirectAsync(Guid userId, CancellationToken ct = default) { DirectCreates++; return Task.FromResult(Chat(Guid.NewGuid())); }
        public Task<ChatResponse> CreateGroupAsync(string title, IReadOnlyList<Guid> userIds, CancellationToken ct = default) { GroupCreates++; return Task.FromResult(Chat(Guid.NewGuid())); }
        public Task<MessageResponse> SendAsync(Guid chatId, Guid clientId, string body, CancellationToken ct = default) =>
            SendFailure is null ? Task.FromResult(Message(chatId, 1)) : Task.FromException<MessageResponse>(SendFailure);
        public Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default) =>
            SendFailure is null ? Task.FromResult(Message(chatId, 1)) : Task.FromException<MessageResponse>(SendFailure);
        public Task<IReadOnlyList<MessageResponse>> SearchAsync(Guid chatId, string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MessageResponse>>(SearchResults);
        public Task<IReadOnlyList<MessageResponse>> GetPinsAsync(Guid chatId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MessageResponse>>(PinResults);
        public Task<IReadOnlyList<StoredObjectResponse>> GetChatMediaAsync(Guid chatId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StoredObjectResponse>>([]);
        public Task<ContactResponse> UpdateContactAsync(Guid contactId, UpdateContactRequest request, CancellationToken ct = default)
        {
            LastContactUpdate = request;
            return Task.FromResult(new ContactResponse(contactId, Guid.NewGuid(), "", "", null, null, null, null,
                request.LocalFirstName, request.LocalLastName, request.IsMuted, DateTimeOffset.UtcNow));
        }
    }
}
