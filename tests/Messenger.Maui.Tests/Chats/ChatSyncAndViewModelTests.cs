using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Realtime;
using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Features.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Chats;

public sealed class ChatSyncAndViewModelTests
{
    [Fact]
    public async Task Search_finds_parts_of_words_in_loaded_history_and_tracks_edits_and_deletions()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        var first = Message(chatId, 1) with { Body = "Вечером отправлю фотографию" };
        var second = Message(chatId, 2) with { Body = "Ещё один снимок" };
        var vm = new ChatViewModel(chatId, api);
        vm.Receive(first);
        vm.Receive(second);
        // An older server's whole-word search returns no matches for a substring.
        await vm.SearchAsync("ФОТО");
        Assert.Equal(first.Id, Assert.Single(vm.SearchResults).Id);
        vm.Receive(first with { Body = "Текст без снимков", EditedAt = DateTimeOffset.UtcNow });
        Assert.Empty(vm.SearchResults);
        vm.Receive(second with { Body = "Фотография готова", EditedAt = DateTimeOffset.UtcNow });
        Assert.Equal(second.Id, Assert.Single(vm.SearchResults).Id);
        vm.Receive(second with { DeletedAt = DateTimeOffset.UtcNow });
        Assert.Empty(vm.SearchResults);
        Assert.DoesNotContain(vm.Messages, value => value.Id == second.Id);
        await vm.SearchAsync(" ");
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public async Task Shared_attachments_filter_content_and_remove_deleted_media_without_removing_independent_copies()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        StoredObjectResponse File(string name, string type) => new(Guid.NewGuid(), name, type, 2048, "sha", DateTimeOffset.UtcNow);
        var photo = File("photo.jpg", "image/jpeg");
        var video = File("video.mp4", "video/mp4");
        var audio = File("voice.m4a", "audio/mp4");
        var file = File("report.pdf", "application/pdf");
        var first = Message(chatId, 1) with { Attachments = [photo, video, audio, file], Body = "Сайт https://example.com/page. И www.example.org/test" };
        var duplicate = Message(chatId, 2) with { Attachments = [photo], Body = null };
        var deleted = Message(chatId, 3) with { Attachments = [File("deleted.jpg", "image/jpeg")], DeletedAt = DateTimeOffset.UtcNow };
        api.Messages.AddRange([first, duplicate, deleted]);
        var vm = new SharedMediaViewModel(chatId, api);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Items.Count);
        Assert.Contains(vm.Items, value => value.IsVideo);
        vm.RemoveMessage(duplicate.Id);
        Assert.Equal(2, vm.Items.Count);
        vm.Filter = SharedMediaFilter.Audio;
        Assert.Equal(audio.Id, Assert.Single(vm.Items).Attachment!.Id);
        vm.Filter = SharedMediaFilter.Files;
        Assert.Equal(file.Id, Assert.Single(vm.Items).Attachment!.Id);
        vm.Filter = SharedMediaFilter.Links;
        Assert.Equal(["https://example.com/page", "https://www.example.org/test"], vm.Items.Select(value => value.Url));
        vm.RemoveMessage(first.Id);
        Assert.Empty(vm.Items);
        vm.Filter = SharedMediaFilter.Media;
        Assert.Empty(vm.Items);
        await vm.LoadAsync();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Editing_updates_history_search_and_pins_and_restores_the_unsent_draft()
    {
        var chatId = Guid.NewGuid();
        var self = Guid.NewGuid();
        var source = Message(chatId, 1) with { SenderUserId = self };
        var api = new FakeConversationApi();
        api.Messages.Add(source);
        var viewModel = new ChatViewModel(chatId, api) { ComposerText = "Несохранённый черновик" };
        viewModel.SetCurrentUser(self);
        viewModel.Receive(source);
        viewModel.SearchResults.Add(source);
        viewModel.Pins.Add(source);
        viewModel.BeginEdit(source);
        viewModel.ComposerText = "Исправленный текст";

        await viewModel.SendAsync();

        Assert.False(viewModel.IsEditing);
        Assert.Equal("Несохранённый черновик", viewModel.ComposerText);
        Assert.All(new[] { viewModel.Messages, viewModel.SearchResults, viewModel.Pins }, rows =>
            Assert.Equal("Исправленный текст", Assert.Single(rows).Body));
        Assert.Equal((source.Id, "Исправленный текст"), Assert.Single(api.Edits));
        viewModel.Receive(source);
        Assert.Equal("Исправленный текст", viewModel.Messages[0].Body);
    }

    [Fact]
    public async Task Failed_edits_preserve_typed_changes_and_deletion_only_removes_data_after_server_success()
    {
        var chatId = Guid.NewGuid();
        var self = Guid.NewGuid();
        var source = Message(chatId, 1) with { SenderUserId = self };
        var api = new FakeConversationApi { EditFailure = new HttpRequestException("offline"), DeleteFailure = new HttpRequestException("offline") };
        api.Messages.Add(source);
        var viewModel = new ChatViewModel(chatId, api) { ComposerText = "Черновик" };
        viewModel.SetCurrentUser(self);
        viewModel.Receive(source);
        viewModel.SearchResults.Add(source);
        viewModel.Pins.Add(source);
        viewModel.BeginEdit(source);
        viewModel.ComposerText = "Новая редакция";
        await Assert.ThrowsAsync<HttpRequestException>(() => viewModel.SendAsync());
        Assert.True(viewModel.IsEditing);
        Assert.Equal("Новая редакция", viewModel.ComposerText);
        await Assert.ThrowsAsync<HttpRequestException>(() => viewModel.DeleteMessageAsync(source, globally: false));
        Assert.Single(viewModel.Messages);
        api.DeleteFailure = null;
        await viewModel.DeleteMessageAsync(source, globally: false);
        Assert.Empty(viewModel.Messages);
        Assert.Empty(viewModel.SearchResults);
        Assert.Empty(viewModel.Pins);
        Assert.Equal("Черновик", viewModel.ComposerText);
        viewModel.Receive(source);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task Realtime_changes_apply_to_old_messages_and_reconnect_reconciles_missed_edits_and_hides()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        var first = Message(chatId, 1);
        var second = Message(chatId, 2);
        api.Messages.AddRange([first, second]);
        var realtime = new FakeRealtimeClient();
        var sync = new ChatSyncService(api, realtime, new MemoryCursorStore { Cursor = 2 });
        var viewModel = new ChatViewModel(chatId, api);
        viewModel.Receive(first);
        viewModel.Receive(second);
        sync.MessageReceived += row => { viewModel.Receive(row); return Task.CompletedTask; };
        sync.MessagesReconciled += rows => { viewModel.ApplySnapshot(rows); return Task.CompletedTask; };
        sync.MessageRemoved += id => { viewModel.RemoveMessage(id); return Task.CompletedTask; };
        await sync.StartAsync(chatId);
        api.Messages[0] = first with { Body = "Правка", EditedAt = DateTimeOffset.UtcNow };
        await realtime.EmitUpdatedAsync(new(chatId, first.Id, 1));
        Assert.Equal("Правка", viewModel.Messages[0].Body);
        api.Messages[0] = api.Messages[0] with { Body = "Правка офлайн", EditedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
        api.Messages.RemoveAt(1);
        await realtime.ReconnectAsync();
        Assert.Equal("Правка офлайн", Assert.Single(viewModel.Messages).Body);
        api.Messages[0] = api.Messages[0] with { DeletedAt = DateTimeOffset.UtcNow, Body = null, Attachments = [] };
        await realtime.EmitDeletedAsync(new(chatId, first.Id, 1));
        Assert.Empty(viewModel.Messages);
        viewModel.Receive(first);
        Assert.Empty(viewModel.Messages);
        await realtime.EmitHiddenAsync(new(chatId, first.Id));
        Assert.Empty(viewModel.Messages);
        sync.Stop();
        await realtime.EmitUpdatedAsync(new(chatId, first.Id, 1));
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public void Only_own_non_music_messages_can_be_edited_and_cancelling_restores_the_draft()
    {
        var chatId = Guid.NewGuid();
        var self = Guid.NewGuid();
        var viewModel = new ChatViewModel(chatId, new FakeConversationApi()) { ComposerText = "Черновик" };
        viewModel.SetCurrentUser(self);
        Assert.False(viewModel.CanEdit(Message(chatId, 1)));
        var own = Message(chatId, 1) with { SenderUserId = self };
        Assert.False(viewModel.CanEdit(own with { Type = "music" }));
        Assert.False(viewModel.CanEdit(own with { DeletedAt = DateTimeOffset.UtcNow }));
        viewModel.BeginEdit(own);
        viewModel.ComposerText = "Временная правка";
        viewModel.CancelEdit();
        Assert.Equal("Черновик", viewModel.ComposerText);
    }

    [Fact]
    public async Task Startup_unauthorized_connection_uses_API_session_recovery_then_reconnects_without_losing_subscriptions()
    {
        var chat = Chat(Guid.NewGuid());
        var api = new FakeConversationApi();
        api.ChatRows.Add(chat);
        var realtime = new FakeRealtimeClient
        {
            ConnectFailure = new HttpRequestException("Expired token", null, System.Net.HttpStatusCode.Unauthorized),
            ConnectFailuresRemaining = 1,
            BeforeConnect = attempt => { if (attempt == 2) Assert.Equal(1, api.ChatListLookups); }
        };
        var sync = new ChatListSyncService(api, realtime);
        var changes = 0;
        sync.ChatChanged += (_, _) => { changes++; return Task.CompletedTask; };

        await sync.StartAsync();
        await sync.StartAsync();
        await realtime.EmitAsync(new MessageCreatedEvent(chat.Id, Guid.NewGuid(), 1));

        Assert.Equal(2, realtime.ConnectAttempts);
        Assert.Equal(1, api.ChatListLookups);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Persistent_unauthorized_connection_is_retried_only_once_and_can_be_started_later()
    {
        var api = new FakeConversationApi();
        var realtime = new FakeRealtimeClient
        {
            ConnectFailure = new HttpRequestException("Expired token", null, System.Net.HttpStatusCode.Unauthorized),
            ConnectFailuresRemaining = 2
        };
        var sync = new ChatListSyncService(api, realtime);

        await Assert.ThrowsAsync<HttpRequestException>(() => sync.StartAsync());
        await sync.StartAsync();

        Assert.Equal(3, realtime.ConnectAttempts);
        Assert.Equal(1, api.ChatListLookups);
    }

    [Fact]
    public async Task Connection_errors_other_than_401_are_not_hidden_or_retried_as_session_refresh()
    {
        var api = new FakeConversationApi();
        var failure = new HttpRequestException("Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable);
        var realtime = new FakeRealtimeClient { ConnectFailure = failure, ConnectFailuresRemaining = 1 };
        var sync = new ChatListSyncService(api, realtime);

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => sync.StartAsync()));
        Assert.Equal(1, realtime.ConnectAttempts);
        Assert.Equal(0, api.ChatListLookups);
    }

    [Fact]
    public async Task Chat_list_sync_start_is_idempotent_and_fetches_one_summary_per_event()
    {
        var chat = Chat(Guid.NewGuid());
        var api = new FakeConversationApi();
        api.ChatRows.Add(chat);
        var realtime = new FakeRealtimeClient();
        var sync = new ChatListSyncService(api, realtime);
        var changed = new List<Guid>();
        sync.ChatChanged += (summary, _) => { changed.Add(summary.Id); return Task.CompletedTask; };

        await sync.StartAsync();
        await sync.StartAsync();
        await realtime.EmitAsync(new MessageCreatedEvent(chat.Id, Guid.NewGuid(), 1));

        Assert.Equal(1, api.ChatLookups);
        Assert.Equal([chat.Id], changed);
    }

    [Fact]
    public async Task Chat_list_sync_stop_unsubscribes_and_lookup_failure_waits_for_reconnect_reload()
    {
        var api = new FakeConversationApi { ChatLookupFailure = new HttpRequestException("offline") };
        var realtime = new FakeRealtimeClient();
        var sync = new ChatListSyncService(api, realtime);
        var changed = 0;
        var reloads = 0;
        sync.ChatChanged += (_, _) => { changed++; return Task.CompletedTask; };
        sync.ReloadRequested += () => { reloads++; return Task.CompletedTask; };
        await sync.StartAsync();

        await realtime.EmitAsync(new MessageCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), 1));
        await realtime.ReconnectAsync();
        sync.Stop();
        await realtime.EmitAsync(new MessageCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), 2));
        await realtime.ReconnectAsync();

        Assert.Equal(1, api.ChatLookups);
        Assert.Equal(0, changed);
        Assert.Equal(1, reloads);
    }

    [Fact]
    public void Chat_upsert_adds_or_updates_once_orders_by_last_message_and_counts_only_new_incoming()
    {
        var currentUserId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var first = Chat(Guid.NewGuid()) with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var target = Chat(Guid.NewGuid()) with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var viewModel = new ChatsViewModel(new FakeConversationApi());
        viewModel.SetCurrentUser(currentUserId);
        viewModel.Chats.Add(new ChatListItem(first, 0, currentUserId));
        viewModel.Chats.Add(new ChatListItem(target, 0, currentUserId));
        var incomingMessageId = Guid.NewGuid();
        var incoming = target with
        {
            LastMessage = new ChatLastMessageResponse(
                incomingMessageId, peerId, "text", "Новое", DateTimeOffset.UtcNow, null)
        };

        viewModel.Upsert(incoming, incrementUnread: true);
        viewModel.Upsert(incoming, incrementUnread: true);

        Assert.Equal(2, viewModel.Chats.Count);
        Assert.Equal(target.Id, viewModel.VisibleChats.First().Chat.Id);
        Assert.Equal(1, viewModel.Chats.Single(item => item.Chat.Id == target.Id).UnreadCount);

        var outgoing = Chat(Guid.NewGuid()) with
        {
            LastMessage = new ChatLastMessageResponse(
                Guid.NewGuid(), currentUserId, "text", "Моё", DateTimeOffset.UtcNow.AddSeconds(1), null)
        };
        viewModel.Upsert(outgoing, incrementUnread: true);

        Assert.Equal(3, viewModel.Chats.Count);
        Assert.Equal(0, viewModel.Chats.Single(item => item.Chat.Id == outgoing.Id).UnreadCount);
    }

    [Fact]
    public void Chat_upsert_keeps_all_active_chats_visible()
    {
        var first = Chat(Guid.NewGuid()) with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var second = Chat(Guid.NewGuid());
        var viewModel = new ChatsViewModel(new FakeConversationApi());

        viewModel.Upsert(first, incrementUnread: false);
        viewModel.Upsert(second, incrementUnread: false);

        Assert.Equal([second.Id, first.Id], viewModel.VisibleChats.Select(item => item.Chat.Id));
    }

    [Fact]
    public async Task Chat_upsert_during_initial_load_is_not_overwritten_by_a_stale_response()
    {
        var chat = Chat(Guid.NewGuid()) with
        {
            LastMessage = new ChatLastMessageResponse(
                Guid.NewGuid(), Guid.NewGuid(), "text", "Новое", DateTimeOffset.UtcNow, null)
        };
        var api = new FakeConversationApi
        {
            FirstChatLookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseFirstChatLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            SecondChatRows = [chat]
        };
        var viewModel = new ChatsViewModel(api);
        var load = viewModel.LoadAsync();
        await api.FirstChatLookupStarted.Task;

        viewModel.Upsert(chat, incrementUnread: true);
        api.ReleaseFirstChatLookup.SetResult();
        await load;

        Assert.Equal(2, api.ChatListLookups);
        Assert.Equal(chat.Id, Assert.Single(viewModel.VisibleChats).Chat.Id);
    }

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
    public async Task Chat_list_runs_bulk_actions_and_creates_direct_or_group()
    {
        var api = new FakeConversationApi();
        var viewModel = new ChatsViewModel(api);
        var chatIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await viewModel.ApplyBulkAsync(chatIds, ChatBulkAction.Archive);
        await viewModel.CreateChatAsync([Guid.NewGuid()], null);
        await viewModel.CreateChatAsync([Guid.NewGuid(), Guid.NewGuid()], "Команда");

        Assert.Equal(2, api.Archived.Count);
        Assert.Equal(1, api.DirectCreates);
        Assert.Equal(1, api.GroupCreates);
    }

    [Fact]
    public void Chat_list_item_uses_peer_or_group_title_and_formats_text_direction()
    {
        var currentUserId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeSpan.FromHours(5));
        var direct = Chat(Guid.NewGuid()) with
        {
            Peer = new ChatPeerResponse(peerId, "Евгений", null),
            LastMessage = new ChatLastMessageResponse(
                Guid.NewGuid(), currentUserId, "text", "Привет", now.AddMinutes(-5), null)
        };
        var group = direct with { Id = Guid.NewGuid(), Type = "group", Title = "Команда", Peer = null };

        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+5-test", TimeSpan.FromHours(5), "UTC+5-test", "UTC+5-test");
        var directItem = new ChatListItem(direct, 0, currentUserId, () => now, zone);
        var groupItem = new ChatListItem(group, 0, currentUserId, () => now, zone);

        Assert.Equal("Евгений", directItem.Title);
        Assert.Equal("Вы: Привет", directItem.Preview);
        Assert.Equal("Команда", groupItem.Title);
        Assert.Equal("10:25", directItem.DateText);
        Assert.Null(directItem.AvatarObjectId);
        Assert.Null(groupItem.AvatarObjectId);
    }

    [Fact]
    public void Chat_list_item_selects_peer_or_group_avatar()
    {
        var direct = Chat(Guid.NewGuid()) with
        {
            AvatarObjectId = "wrong-group-avatar",
            Peer = new ChatPeerResponse(Guid.NewGuid(), "Peer", "peer-avatar")
        };
        var group = direct with
        {
            Id = Guid.NewGuid(), Type = "group", Title = "Group", Peer = null,
            AvatarObjectId = "group-avatar"
        };

        Assert.Equal("peer-avatar", new ChatListItem(direct, 0).AvatarObjectId);
        Assert.Equal("group-avatar", new ChatListItem(group, 0).AvatarObjectId);
    }

    [Theory]
    [InlineData("image", null, false, "Фото")]
    [InlineData("video", null, false, "Видео")]
    [InlineData("audio", null, false, "Аудио")]
    [InlineData("file", null, false, "Файл")]
    [InlineData("text", "Удалено", true, "")]
    public void Chat_list_item_maps_message_types(
        string type,
        string? body,
        bool deleted,
        string expected)
    {
        var now = new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeSpan.FromHours(5));
        var chat = Chat(Guid.NewGuid()) with
        {
            LastMessage = new ChatLastMessageResponse(
                Guid.NewGuid(), Guid.NewGuid(), type, body, now,
                deleted ? now : null)
        };

        Assert.Equal(expected, new ChatListItem(chat, 0, Guid.Empty, () => now).Preview);
    }

    [Fact]
    public void Chat_list_item_uses_local_calendar_boundary_and_empty_chat_has_no_date()
    {
        var now = new DateTimeOffset(2026, 10, 12, 0, 30, 0, TimeSpan.FromHours(5));
        var today = Chat(Guid.NewGuid()) with
        {
            LastMessage = new ChatLastMessageResponse(
                Guid.NewGuid(), Guid.NewGuid(), "text", "Сегодня",
                new DateTimeOffset(2026, 10, 11, 19, 15, 0, TimeSpan.Zero), null)
        };
        var yesterday = today with
        {
            Id = Guid.NewGuid(),
            LastMessage = today.LastMessage! with
            {
                Id = Guid.NewGuid(),
                CreatedAt = new DateTimeOffset(2026, 10, 11, 18, 59, 0, TimeSpan.Zero)
            }
        };
        var empty = Chat(Guid.NewGuid());

        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+5-boundary", TimeSpan.FromHours(5), "UTC+5-boundary", "UTC+5-boundary");
        Assert.Equal("00:15", new ChatListItem(today, 0, Guid.Empty, () => now, zone).DateText);
        Assert.Equal("11.10.26", new ChatListItem(yesterday, 0, Guid.Empty, () => now, zone).DateText);
        var emptyItem = new ChatListItem(empty, 0, Guid.Empty, () => now);
        Assert.Equal("Пока нет сообщений", emptyItem.Preview);
        Assert.Equal(string.Empty, emptyItem.DateText);
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
    public async Task Chat_list_loads_all_active_chats_without_requesting_folders()
    {
        var api = new FakeConversationApi();
        var first = Chat(Guid.NewGuid()) with { UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var second = Chat(Guid.NewGuid());
        var archived = Chat(Guid.NewGuid()) with { IsArchived = true };
        var hidden = Chat(Guid.NewGuid()) with { IsHidden = true };
        api.ChatRows.AddRange([first, second, archived, hidden]);
        var viewModel = new ChatsViewModel(api);

        await viewModel.LoadAsync();
        Assert.Equal([second.Id, first.Id], viewModel.VisibleChats.Select(item => item.Chat.Id));
        Assert.Equal(0, api.FolderLookups);

        viewModel.Upsert(first with { IsArchived = true }, incrementUnread: false);
        Assert.Equal(second.Id, Assert.Single(viewModel.VisibleChats).Chat.Id);

        viewModel.Upsert(second with { IsHidden = true }, incrementUnread: false);
        Assert.Empty(viewModel.VisibleChats);
    }

    [Fact]
    public async Task Leaving_a_chat_unsubscribes_from_realtime_messages()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        var realtime = new FakeRealtimeClient();
        var sync = new ChatSyncService(api, realtime, new MemoryCursorStore());
        var received = new List<long>();
        sync.MessageReceived += message => { received.Add(message.Sequence); return Task.CompletedTask; };
        await sync.StartAsync(chatId);
        sync.Stop();
        api.Messages.Add(Message(chatId, 1));

        await realtime.EmitAsync(new MessageCreatedEvent(chatId, api.Messages[0].Id, 1));

        Assert.Empty(received);
        await sync.StartAsync(chatId);
        Assert.Equal([1L], received);
    }

    [Fact]
    public async Task Composer_is_preserved_on_failure_and_search_and_pins_update_results()
    {
        var api = new FakeConversationApi { SendFailure = new HttpRequestException("offline") };
        var chatId = Guid.NewGuid();
        api.SearchResults.Add(Message(chatId, 4) with { Body = "Точное совпадение" });
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

    [Fact]
    public void Read_indicator_ignores_own_receipts_and_waits_for_every_other_group_member()
    {
        var chatId = Guid.NewGuid();
        var self = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var third = Guid.NewGuid();
        var viewModel = new ChatViewModel(chatId, new FakeConversationApi());
        viewModel.SetCurrentUser(self);
        viewModel.ApplyReadPosition(new(chatId, peer, 1));
        Assert.Equal(0, viewModel.ReadSequence);
        viewModel.ApplyReadPositions([new(self, "member", DateTimeOffset.UtcNow, 50), new(peer, "member", DateTimeOffset.UtcNow)]);
        Assert.Equal(1, viewModel.ReadSequence);
        viewModel.ApplyReadPosition(new(chatId, peer, 6));
        viewModel.ApplyReadPosition(new(Guid.NewGuid(), peer, 99));
        viewModel.ApplyReadPosition(new(chatId, peer, 2));
        viewModel.ApplyReadPositions([new(self, "member", DateTimeOffset.UtcNow, 50), new(peer, "member", DateTimeOffset.UtcNow, 1)]);
        Assert.Equal(6, viewModel.ReadSequence);

        viewModel.ApplyReadPositions([new(self, "owner", DateTimeOffset.UtcNow, 50),
            new(peer, "member", DateTimeOffset.UtcNow, 6), new(third, "member", DateTimeOffset.UtcNow)]);
        Assert.Equal(0, viewModel.ReadSequence);
        viewModel.ApplyReadPosition(new(chatId, third, 4));
        Assert.Equal(4, viewModel.ReadSequence);
        viewModel.ApplyReadPosition(new(chatId, third, 8));
        Assert.Equal(6, viewModel.ReadSequence);
    }

    [Fact]
    public async Task Visible_read_receipts_do_not_acknowledge_unseen_messages_and_retry_after_failure()
    {
        var chatId = Guid.NewGuid();
        var api = new FakeConversationApi();
        api.Messages.AddRange(Enumerable.Range(1, 20).Select(index => Message(chatId, index)));
        var viewModel = new ChatViewModel(chatId, api);
        viewModel.SetCurrentUser(Guid.NewGuid());
        await viewModel.LoadAsync();

        await viewModel.MarkVisibleReadAsync(3);
        await viewModel.MarkVisibleReadAsync(2);
        await viewModel.MarkVisibleReadAsync(3);
        Assert.Equal([3L], api.ReadSequences);
        api.ReadFailure = new HttpRequestException("offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => viewModel.MarkVisibleReadAsync(8));
        api.ReadFailure = null;
        await viewModel.MarkVisibleReadAsync(8);
        await viewModel.MarkVisibleReadAsync(long.MaxValue);
        Assert.Equal([3L, 8L, 20L], api.ReadSequences);
    }

    [Fact]
    public async Task Read_receipts_are_reconciled_on_reconnect_and_unsubscribed_when_chat_closes()
    {
        var chatId = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var api = new FakeConversationApi();
        api.Members.Add(new(peer, "member", DateTimeOffset.UtcNow, 1));
        var realtime = new FakeRealtimeClient();
        var sync = new ChatSyncService(api, realtime, new MemoryCursorStore());
        var viewModel = new ChatViewModel(chatId, api);
        viewModel.SetCurrentUser(Guid.NewGuid());
        sync.ReadPositionsLoaded += members => { viewModel.ApplyReadPositions(members); return Task.CompletedTask; };
        sync.ReadPositionChanged += receipt => { viewModel.ApplyReadPosition(receipt); return Task.CompletedTask; };

        await sync.StartAsync(chatId);
        Assert.Equal(1, viewModel.ReadSequence);
        await realtime.EmitReadAsync(new(chatId, peer, 4));
        Assert.Equal(4, viewModel.ReadSequence);
        api.Members[0] = api.Members[0] with { LastReadSequence = 9 };
        await realtime.ReconnectAsync();
        Assert.Equal(9, viewModel.ReadSequence);
        sync.Stop();
        await realtime.EmitReadAsync(new(chatId, peer, 12));
        Assert.Equal(9, viewModel.ReadSequence);
    }

    [Fact]
    public async Task Delayed_reconciliation_from_a_closed_chat_does_not_leak_into_the_next_chat()
    {
        var previousChat = Guid.NewGuid();
        var nextChat = Guid.NewGuid();
        var api = new FakeConversationApi
        {
            FirstMessagesLookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseFirstMessagesLookup = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        api.Messages.Add(Message(previousChat, 10));
        api.Messages.Add(Message(nextChat, 1));
        var realtime = new FakeRealtimeClient();
        var sync = new ChatSyncService(api, realtime, new MemoryCursorStore());
        var received = new List<Guid>();
        sync.MessageReceived += message => { received.Add(message.ChatId); return Task.CompletedTask; };
        var previousStart = sync.StartAsync(previousChat);
        await api.FirstMessagesLookupStarted.Task;

        sync.Stop();
        var nextStart = sync.StartAsync(nextChat);
        api.ReleaseFirstMessagesLookup.SetResult();
        await Task.WhenAll(previousStart, nextStart);

        Assert.Equal([nextChat], received);
        Assert.Equal([previousChat, nextChat], realtime.JoinedChats);
    }

    [Fact]
    public void Shared_track_cover_updates_in_messages_search_and_pins_without_changing_the_message_identity()
    {
        var chatId = Guid.NewGuid();
        var track = new MusicTrackResponse(Guid.NewGuid(), "Песня", "Автор", 1000, null, "available");
        var message = Message(chatId, 1) with { Type = "music", MusicTrack = track };
        var viewModel = new ChatViewModel(chatId, new FakeConversationApi());
        viewModel.Messages.Add(message);
        viewModel.SearchResults.Add(message);
        viewModel.Pins.Add(message);
        var cover = Guid.NewGuid();

        viewModel.ApplyMusicTrack(track with { CoverObjectId = cover });

        Assert.All(new[] { viewModel.Messages, viewModel.SearchResults, viewModel.Pins }, rows =>
        {
            Assert.Equal(message.Id, Assert.Single(rows).Id);
            Assert.Equal(cover, rows[0].MusicTrack!.CoverObjectId);
        });
    }

    private static MessageResponse Message(Guid chatId, long sequence) => new(
        Guid.NewGuid(), chatId, Guid.NewGuid(), sequence, Guid.NewGuid(), "text", $"m{sequence}", false,
        DateTimeOffset.UtcNow, null, null);

    private static ChatResponse Chat(Guid id) => new(
        id, "direct", null, null, "member", false, false, false, 0, DateTimeOffset.UtcNow);

    private sealed class FakeRealtimeClient : IChatRealtimeClient
    {
        public Exception? ConnectFailure { get; init; }
        public int ConnectFailuresRemaining { get; set; }
        public int ConnectAttempts { get; private set; }
        public Action<int>? BeforeConnect { get; init; }
        public event Func<MessageCreatedEvent, Task>? MessageCreated;
        public event Func<MessageUpdatedEvent, Task>? MessageUpdated;
        public event Func<MessageDeletedEvent, Task>? MessageDeleted;
        public event Func<MessageHiddenEvent, Task>? MessageHidden;
        public event Func<ReadPositionChangedEvent, Task>? ReadPositionChanged;
        public event Func<Task>? Reconnected;
        public event Action<ChatConnectionState>? ConnectionStateChanged;
        public List<Guid> JoinedChats { get; } = [];
        public Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default)
        {
            JoinedChats.Add(chatId);
            return Task.CompletedTask;
        }
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectAttempts++;
            BeforeConnect?.Invoke(ConnectAttempts);
            if (ConnectFailuresRemaining-- > 0 && ConnectFailure is { } failure)
                return Task.FromException(failure);
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connecting);
            ConnectionStateChanged?.Invoke(ChatConnectionState.Connected);
            return Task.CompletedTask;
        }
        public Task EmitAsync(MessageCreatedEvent value) => MessageCreated?.Invoke(value) ?? Task.CompletedTask;
        public Task EmitUpdatedAsync(MessageUpdatedEvent value) => MessageUpdated?.Invoke(value) ?? Task.CompletedTask;
        public Task EmitDeletedAsync(MessageDeletedEvent value) => MessageDeleted?.Invoke(value) ?? Task.CompletedTask;
        public Task EmitHiddenAsync(MessageHiddenEvent value) => MessageHidden?.Invoke(value) ?? Task.CompletedTask;
        public Task EmitReadAsync(ReadPositionChangedEvent value) => ReadPositionChanged?.Invoke(value) ?? Task.CompletedTask;
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
        public Exception? EditFailure { get; set; }
        public Exception? DeleteFailure { get; set; }
        public List<(Guid Id, string Body)> Edits { get; } = [];
        public List<(Guid Id, bool Globally)> Deletes { get; } = [];
        public Task<MessageResponse> GetMessageAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult(Messages.Single(message => message.Id == messageId));
        public Task<MessageResponse> EditMessageAsync(Guid messageId, string body, CancellationToken ct = default)
        {
            if (EditFailure is not null) return Task.FromException<MessageResponse>(EditFailure);
            Edits.Add((messageId, body));
            var index = Messages.FindIndex(message => message.Id == messageId);
            Messages[index] = Messages[index] with { Body = body, EditedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(Messages[index]);
        }
        public Task DeleteMessageAsync(Guid messageId, bool globally, CancellationToken ct = default)
        {
            if (DeleteFailure is not null) return Task.FromException(DeleteFailure);
            Deletes.Add((messageId, globally));
            var index = Messages.FindIndex(message => message.Id == messageId);
            if (globally) Messages[index] = Messages[index] with { DeletedAt = DateTimeOffset.UtcNow, Body = null, Attachments = [], MusicTrack = null };
            else Messages.RemoveAt(index);
            return Task.CompletedTask;
        }
        public Task<MessageResponse> ForwardMessageAsync(Guid messageId, Guid targetChatId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(Messages.Single(message => message.Id == messageId) with { Id = Guid.NewGuid(), ChatId = targetChatId, ClientMessageId = clientId, IsForwarded = true });
        public List<ChatResponse> ChatRows { get; } = [];
        public List<ChatMemberResponse> Members { get; } = [];
        public List<long> ReadSequences { get; } = [];
        public Exception? ReadFailure { get; set; }
        public int FolderLookups { get; private set; }
        public List<MessageResponse> Messages { get; } = [];
        public List<MessageResponse> SearchResults { get; } = [];
        public List<MessageResponse> PinResults { get; } = [];
        public List<Guid> Archived { get; } = [];
        public List<Guid> ReadChats { get; } = [];
        public int DirectCreates { get; private set; }
        public int GroupCreates { get; private set; }
        public Exception? SendFailure { get; set; }
        public Exception? ChatLookupFailure { get; set; }
        public int ChatLookups { get; private set; }
        public int ChatListLookups { get; private set; }
        public TaskCompletionSource? FirstChatLookupStarted { get; init; }
        public TaskCompletionSource? ReleaseFirstChatLookup { get; init; }
        public TaskCompletionSource? FirstMessagesLookupStarted { get; init; }
        public TaskCompletionSource? ReleaseFirstMessagesLookup { get; init; }
        private int _messagesLookups;
        public IReadOnlyList<ChatResponse>? SecondChatRows { get; init; }
        public UpdateContactRequest? LastContactUpdate { get; private set; }

        public async Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default)
        {
            ChatListLookups++;
            if (ChatListLookups == 1 && ReleaseFirstChatLookup is not null)
            {
                FirstChatLookupStarted?.SetResult();
                await ReleaseFirstChatLookup.Task.WaitAsync(ct);
            }
            return ChatListLookups > 1 && SecondChatRows is not null ? SecondChatRows : ChatRows;
        }
        public Task<ChatResponse> GetChatAsync(Guid chatId, CancellationToken ct = default)
        {
            ChatLookups++;
            return ChatLookupFailure is null
                ? Task.FromResult(ChatRows.Single(chat => chat.Id == chatId))
                : Task.FromException<ChatResponse>(ChatLookupFailure);
        }
        public Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default)
        {
            FolderLookups++;
            return Task.FromResult<IReadOnlyList<ChatFolderResponse>>([]);
        }
        public Task<IReadOnlyList<ChatMemberResponse>> GetChatMembersAsync(Guid chatId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChatMemberResponse>>(Members.ToArray());
        public Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContactResponse>>([]);
        public Task<ContactResponse?> GetContactByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<ContactResponse?>(null);
        public Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UserSearchResponse>>([]);
        public Task<UserSearchResponse> GetVisibleUserAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default)
        {
            var messages = Messages.Where(message => message.ChatId == chatId && message.Sequence > afterSequence)
                .OrderBy(message => message.Sequence).ToArray();
            if (++_messagesLookups == 1 && ReleaseFirstMessagesLookup is not null)
            {
                FirstMessagesLookupStarted?.SetResult();
                await ReleaseFirstMessagesLookup.Task.WaitAsync(ct);
            }
            return messages;
        }
        public Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default) { Archived.Add(chatId); return Task.CompletedTask; }
        public Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default)
        {
            if (ReadFailure is not null) return Task.FromException(ReadFailure);
            ReadChats.Add(chatId);
            ReadSequences.Add(sequence);
            return Task.CompletedTask;
        }
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
