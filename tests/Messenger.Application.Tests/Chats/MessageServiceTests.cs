using Messenger.Application.Chats;
using Messenger.Application.Security;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Music;
using Messenger.Application.Tests.Music;
using Messenger.Domain.Media;

namespace Messenger.Application.Tests.Chats;

public sealed class MessageServiceTests
{
    [Fact]
    public async Task Search_scans_older_batches_matches_case_and_yo_and_excludes_hidden_or_deleted_messages()
    {
        var fixture = new Fixture();
        var first = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Ёжик на фотографии");
        for (var index = 0; index < 105; index++)
            await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Другое сообщение");
        var deleted = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Ежик фото удалено");
        var hidden = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Ежик фото скрыто");
        await fixture.Service.DeleteAsync(fixture.UserId, deleted.Message.Id, true);
        await fixture.Service.HideAsync(fixture.UserId, hidden.Message.Id);
        var results = await fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "ЕЖ ФОТО", 20);
        Assert.Equal(first.Message.Id, Assert.Single(results).Message.Id);
    }

    [Fact]
    public async Task A_recipient_can_forward_photos_with_caption_and_order_without_owning_or_reuploading_the_files()
    {
        var fixture = new Fixture(ChatType.Direct);
        var source = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Image, "Два снимка");
        StoredObject[] photos = [
            new(Guid.NewGuid(), fixture.UserId, "first", "one.jpg", "image/jpeg", 20, "sha1", StoredObjectStatus.Available, Fixture.Now),
            new(Guid.NewGuid(), fixture.UserId, "second", "two.png", "image/png", 30, "sha2", StoredObjectStatus.Available, Fixture.Now)
        ];
        fixture.Store.Attachments[source.Message.Id] = photos;
        var recipient = Guid.NewGuid();
        var target = AddTarget(fixture, fixture.OtherUserId, recipient);
        var clientId = Guid.NewGuid();

        var forwarded = await fixture.Service.ForwardAsync(fixture.OtherUserId, source.Message.Id, target.Id, clientId);
        var retry = await fixture.Service.ForwardAsync(fixture.OtherUserId, source.Message.Id, target.Id, clientId);

        Assert.Equal(forwarded.Message.Id, retry.Message.Id);
        Assert.True(forwarded.Message.IsForwarded);
        Assert.Equal(fixture.OtherUserId, forwarded.Message.SenderUserId);
        Assert.Equal("Два снимка", forwarded.Body);
        Assert.Equal(photos.Select(photo => photo.Id), forwarded.Attachments!.Select(photo => photo.Id));
        Assert.Equal(2, fixture.Store.Messages.Count);
        Assert.All(forwarded.Attachments!, photo => Assert.Equal(fixture.UserId, photo.OwnerUserId));
        var received = Assert.Single(await fixture.Service.ListAsync(recipient, target.Id, 0, 20));
        Assert.Equal(photos.Select(photo => photo.Id), received.Attachments!.Select(photo => photo.Id));
        await fixture.Service.EditAsync(fixture.UserId, source.Message.Id, "Новая подпись оригинала");
        Assert.Equal("Два снимка", (await fixture.Service.GetAsync(recipient, forwarded.Message.Id)).Body);
    }

    [Fact]
    public async Task Forwarding_requires_both_chat_memberships_and_a_visible_undeleted_source()
    {
        var fixture = new Fixture(ChatType.Direct);
        var source = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Секрет");
        var outsider = Guid.NewGuid();
        var target = AddTarget(fixture, fixture.OtherUserId, outsider);

        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.ForwardAsync(fixture.UserId, source.Message.Id, target.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.ForwardAsync(outsider, source.Message.Id, target.Id, Guid.NewGuid()));
        await fixture.Service.HideAsync(fixture.OtherUserId, source.Message.Id);
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.ForwardAsync(fixture.OtherUserId, source.Message.Id, target.Id, Guid.NewGuid()));
        await fixture.Service.DeleteAsync(fixture.UserId, source.Message.Id, globally: true);
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.ForwardAsync(fixture.UserId, source.Message.Id, fixture.Chat.Id, Guid.NewGuid()));
        Assert.Single(fixture.Store.Messages);
    }

    [Fact]
    public async Task Forwarded_music_preserves_the_track_and_an_image_caption_can_be_removed()
    {
        var fixture = new Fixture(ChatType.Direct);
        var track = new MusicTrack(Guid.NewGuid(), fixture.UserId, Guid.NewGuid(), "Песня", "Автор", 1000, null, Fixture.Now);
        fixture.Music.Tracks.Add(track);
        var source = await fixture.Service.SendMusicAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), track.Id);
        var forwarded = await fixture.Service.ForwardAsync(fixture.OtherUserId, source.Message.Id, fixture.Chat.Id, Guid.NewGuid());
        Assert.Equal(track.Id, forwarded.MusicTrack!.Id);
        Assert.True(forwarded.Message.IsForwarded);
        var photo = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Image, "Подпись");
        Assert.Equal("", (await fixture.Service.EditAsync(fixture.UserId, photo.Message.Id, "")).Body);
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.EditAsync(fixture.OtherUserId, photo.Message.Id, "Чужая правка"));
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.EditAsync(fixture.UserId, source.Message.Id, "Подмена песни"));
        var text = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Текст");
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.EditAsync(fixture.UserId, text.Message.Id, " "));
    }

    private static Chat AddTarget(Fixture fixture, Guid sender, Guid recipient)
    {
        var chat = new Chat(Guid.NewGuid(), ChatType.Direct, sender, null, Fixture.Now);
        fixture.Store.Chats.Add(chat);
        fixture.Store.Members.Add(new(chat.Id, sender, ChatMemberRole.Member, Fixture.Now));
        fixture.Store.Members.Add(new(chat.Id, recipient, ChatMemberRole.Member, Fixture.Now));
        return chat;
    }

    [Fact]
    public async Task Photo_attachments_are_returned_to_sender_recipient_search_and_pins_in_attachment_order()
    {
        var fixture = new Fixture(ChatType.Direct);
        var sent = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Image, "Снимки");
        var first = new StoredObject(Guid.NewGuid(), fixture.UserId, "first", "first.png", "image/png", 12, "checksum", StoredObjectStatus.Available, Fixture.Now);
        var second = new StoredObject(Guid.NewGuid(), fixture.UserId, "second", "second.jpg", "image/jpeg", 20, "checksum", StoredObjectStatus.Available, Fixture.Now);
        fixture.Store.Attachments[sent.Message.Id] = [first, second];
        await fixture.Service.PinAsync(fixture.UserId, sent.Message.Id);

        var views = new[]
        {
            await fixture.Service.GetAsync(fixture.UserId, sent.Message.Id),
            Assert.Single(await fixture.Service.ListAsync(fixture.OtherUserId, fixture.Chat.Id, 0, 20)),
            Assert.Single(await fixture.Service.SearchAsync(fixture.OtherUserId, fixture.Chat.Id, "снимки", 20)),
            Assert.Single(await fixture.Service.ListPinsAsync(fixture.OtherUserId, fixture.Chat.Id))
        };

        Assert.All(views, view =>
        {
            Assert.Equal("Снимки", view.Body);
            Assert.Equal([first.Id, second.Id], view.Attachments!.Select(value => value.Id));
        });
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.GetAsync(Guid.NewGuid(), sent.Message.Id));
        await fixture.Service.DeleteAsync(fixture.UserId, sent.Message.Id, globally: true);
        Assert.Empty((await fixture.Service.GetAsync(fixture.OtherUserId, sent.Message.Id)).Attachments!);
    }

    [Fact]
    public async Task Send_is_idempotent_per_sender_client_id_and_sequences_are_monotonic()
    {
        var fixture = new Fixture();
        var clientId = Guid.NewGuid();

        var first = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, clientId, MessageKind.Text, "Первое");
        var retry = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, clientId, MessageKind.Text, "Другое");
        var second = await fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Второе");

        Assert.Equal(first.Message.Id, retry.Message.Id);
        Assert.Equal(1, first.Message.Sequence);
        Assert.Equal(2, second.Message.Sequence);
        Assert.Equal(2, fixture.Store.Messages.Count);
    }

    [Fact]
    public async Task Non_member_gets_not_found_for_send_and_search()
    {
        var fixture = new Fixture();
        var outsider = Guid.NewGuid();

        Assert.Equal(MessageRuleError.NotFound, (await Assert.ThrowsAsync<MessageRuleException>(() =>
            fixture.Service.SendAsync(outsider, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "secret"))).Code);
        Assert.Equal(MessageRuleError.NotFound, (await Assert.ThrowsAsync<MessageRuleException>(() =>
            fixture.Service.SearchAsync(outsider, fixture.Chat.Id, "secret", 20))).Code);
    }

    [Fact]
    public async Task Search_matches_substrings_without_storing_plaintext_and_tampering_fails_closed()
    {
        var fixture = new Fixture();
        var sent = await fixture.Service.SendAsync(
            fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Привет, безопасный мир!");

        var exact = await fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "безопасный", 20);
        var partial = await fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "безопас", 20);

        Assert.Equal(sent.Message.Id, Assert.Single(exact).Message.Id);
        Assert.Equal(sent.Message.Id, Assert.Single(partial).Message.Id);
        Assert.DoesNotContain("безопасный", fixture.Store.Messages.Single().BodyCiphertext, StringComparison.OrdinalIgnoreCase);

        fixture.Cipher.Tampered = true;
        await Assert.ThrowsAsync<System.Security.Cryptography.AuthenticationTagMismatchException>(() =>
            fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "безопасный", 20));
    }

    [Fact]
    public async Task Per_user_hide_excludes_only_that_users_copy_and_group_pin_requires_admin()
    {
        var fixture = new Fixture();
        var otherUser = Guid.NewGuid();
        fixture.Store.Members.Add(new ChatMember(fixture.Chat.Id, otherUser, ChatMemberRole.Member, Fixture.Now));
        var sent = await fixture.Service.SendAsync(
            fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Сообщение");

        await fixture.Service.HideAsync(fixture.UserId, sent.Message.Id);

        Assert.Empty(await fixture.Service.ListAsync(fixture.UserId, fixture.Chat.Id, 0, 20));
        Assert.Single(await fixture.Service.ListAsync(otherUser, fixture.Chat.Id, 0, 20));
        Assert.Equal(MessageRuleError.Forbidden, (await Assert.ThrowsAsync<MessageRuleException>(() =>
            fixture.Service.PinAsync(otherUser, sent.Message.Id))).Code);
        await fixture.Service.PinAsync(fixture.UserId, sent.Message.Id);
        Assert.Single(fixture.Store.Pins);
        Assert.Empty(await fixture.Service.ListPinsAsync(fixture.UserId, fixture.Chat.Id));
        Assert.Single(await fixture.Service.ListPinsAsync(otherUser, fixture.Chat.Id));
        await fixture.Service.DeleteAsync(fixture.UserId, sent.Message.Id, globally: true);
        Assert.Empty(await fixture.Service.ListPinsAsync(otherUser, fixture.Chat.Id));
    }

    [Fact]
    public async Task Direct_message_reactivates_hidden_recipient_before_save()
    {
        var fixture = new Fixture(ChatType.Direct);
        var recipient = fixture.Store.Members.Single(member => member.UserId == fixture.OtherUserId);
        recipient.Hide(Fixture.Now);

        await fixture.Service.SendAsync(
            fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Возвращает диалог");

        Assert.Null(recipient.HiddenAt);
    }

    [Fact]
    public async Task Group_message_does_not_reactivate_members_who_left()
    {
        var fixture = new Fixture();
        var member = new ChatMember(fixture.Chat.Id, fixture.OtherUserId, ChatMemberRole.Member, Fixture.Now);
        fixture.Store.Members.Add(member);
        member.Leave(fixture.Store.Members, Fixture.Now.AddMinutes(1));

        await fixture.Service.SendAsync(
            fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Групповое сообщение");

        Assert.Equal(Fixture.Now.AddMinutes(1), member.LeftAt);
    }

    [Fact]
    public async Task Shared_music_preserves_the_track_and_current_cover_without_copying_audio()
    {
        var fixture = new Fixture(ChatType.Direct);
        var uploader = Guid.NewGuid();
        var track = new MusicTrack(Guid.NewGuid(), uploader, Guid.NewGuid(), "Песня", "Автор", 1000, Guid.NewGuid(), Fixture.Now);
        fixture.Music.Tracks.Add(track);
        var clientId = Guid.NewGuid();
        var sent = await fixture.Service.SendMusicAsync(fixture.UserId, fixture.Chat.Id, clientId, track.Id);
        var retry = await fixture.Service.SendMusicAsync(fixture.UserId, fixture.Chat.Id, clientId, track.Id);
        Assert.Equal(sent.Message.Id, retry.Message.Id);
        Assert.Single(fixture.Store.Messages);
        Assert.Equal(track.Id, sent.Message.MusicTrackId);
        Assert.Equal(MessageKind.Music, sent.Message.Kind);
        Assert.Equal("Песня — Автор", sent.Body);
        var cover = Guid.NewGuid();
        track.UpdateCover(uploader, cover, Fixture.Now);
        var received = Assert.Single(await fixture.Service.ListAsync(fixture.OtherUserId, fixture.Chat.Id, 0, 20));
        Assert.Equal(cover, received.MusicTrack!.CoverObjectId);
        track.Block(Fixture.Now);
        Assert.Null(Assert.Single(await fixture.Service.ListAsync(fixture.OtherUserId, fixture.Chat.Id, 0, 20)).MusicTrack);
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.SendMusicAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), track.Id));
    }

    [Fact]
    public async Task Music_messages_require_an_available_track_and_chat_membership()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.SendMusicAsync(Guid.NewGuid(), fixture.Chat.Id, Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.SendMusicAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<MessageRuleException>(() => fixture.Service.SendAsync(fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Music, "fake"));
        Assert.Empty(fixture.Store.Messages);
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        public Fixture(ChatType type = ChatType.Group)
        {
            UserId = Guid.NewGuid();
            OtherUserId = Guid.NewGuid();
            Chat = new Chat(Guid.NewGuid(), type, UserId, type == ChatType.Group ? "Группа" : null, Now);
            Store.Chats.Add(Chat);
            Store.Members.Add(new ChatMember(
                Chat.Id, UserId, type == ChatType.Group ? ChatMemberRole.Owner : ChatMemberRole.Member, Now));
            if (type == ChatType.Direct)
            {
                Store.Members.Add(new ChatMember(Chat.Id, OtherUserId, ChatMemberRole.Member, Now));
            }
            Service = new MessageService(Store, Store, Cipher, new TestIndex(), new Clock(), Music);
        }

        public Guid UserId { get; }
        public Guid OtherUserId { get; }
        public Chat Chat { get; }
        public MemoryMessageStore Store { get; } = new();
        public MemoryMusicStore Music { get; } = new();
        public TestCipher Cipher { get; } = new();
        public MessageService Service { get; }
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Fixture.Now;
    }

    private sealed class TestIndex : IBlindIndex
    {
        public string Compute(string normalizedValue) => $"hmac:{normalizedValue}";
    }

    private sealed class TestCipher : IFieldCipher
    {
        public bool Tampered { get; set; }
        public EncryptedValue Encrypt(string plaintext) => new(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext)), "nonce", "valid", 1);

        public string Decrypt(EncryptedValue value)
        {
            if (Tampered || value.Tag != "valid")
            {
                throw new System.Security.Cryptography.AuthenticationTagMismatchException();
            }
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value.Ciphertext));
        }
    }

    private sealed class MemoryMessageStore : IMessageStore, IChatStore
    {
        public Task<bool> IsHiddenAsync(Guid messageId, Guid userId, CancellationToken ct) =>
            Task.FromResult(Hidden.Any(value => value.MessageId == messageId && value.UserId == userId));
        public Task AddAttachmentsAsync(Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct)
        {
            var objects = Attachments.Values.SelectMany(values => values).DistinctBy(value => value.Id).ToDictionary(value => value.Id);
            Attachments[messageId] = objectIds.Select(id => objects[id]).ToArray();
            return Task.CompletedTask;
        }
        public Dictionary<Guid, IReadOnlyList<StoredObject>> Attachments { get; } = [];
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<StoredObject>>> ListAttachmentsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<StoredObject>>>(Attachments.Where(row => messageIds.Contains(row.Key)).ToDictionary());
        public List<Chat> Chats { get; } = [];
        public List<ChatMember> Members { get; } = [];
        public List<Message> Messages { get; } = [];
        public List<MessageSearchToken> Tokens { get; } = [];
        public List<HiddenMessage> Hidden { get; } = [];
        public List<PinnedMessage> Pins { get; } = [];

        public Task<Message?> FindByClientIdAsync(Guid senderId, Guid clientMessageId, CancellationToken ct) =>
            Task.FromResult(Messages.SingleOrDefault(message => message.SenderUserId == senderId && message.ClientMessageId == clientMessageId));
        public Task<Message?> FindMessageAsync(Guid messageId, CancellationToken ct) =>
            Task.FromResult(Messages.SingleOrDefault(message => message.Id == messageId));
        public Task<IReadOnlyList<Message>> ListMessagesAsync(Guid chatId, Guid userId, long afterSequence, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Message>>(Messages.Where(message => message.ChatId == chatId && message.Sequence > afterSequence &&
                Hidden.All(hidden => hidden.MessageId != message.Id || hidden.UserId != userId)).OrderBy(message => message.Sequence).Take(take).ToArray());
        public Task<IReadOnlyList<Message>> ListSearchCandidatesAsync(Guid chatId, Guid userId, long beforeSequence, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Message>>(Messages.Where(message => message.ChatId == chatId && message.Sequence < beforeSequence && message.DeletedAt is null &&
                    Hidden.All(hidden => hidden.MessageId != message.Id || hidden.UserId != userId))
                .OrderByDescending(message => message.Sequence).Take(take).ToArray());
        public Task AddMessageAsync(Message message, IReadOnlyCollection<MessageSearchToken> tokens, CancellationToken ct)
        {
            Messages.Add(message);
            Tokens.AddRange(tokens);
            return Task.CompletedTask;
        }
        public Task AddHiddenAsync(HiddenMessage hidden, CancellationToken ct) { Hidden.Add(hidden); return Task.CompletedTask; }
        public Task ReplaceSearchTokensAsync(Guid messageId, IReadOnlyCollection<MessageSearchToken> tokens, CancellationToken ct)
        {
            Tokens.RemoveAll(token => token.MessageId == messageId);
            Tokens.AddRange(tokens);
            return Task.CompletedTask;
        }
        public Task AddPinAsync(PinnedMessage pin, CancellationToken ct) { Pins.Add(pin); return Task.CompletedTask; }
        public Task<PinnedMessage?> FindPinAsync(Guid chatId, Guid messageId, CancellationToken ct) =>
            Task.FromResult(Pins.SingleOrDefault(pin => pin.ChatId == chatId && pin.MessageId == messageId));
        public Task<IReadOnlyList<PinnedMessage>> ListPinsAsync(Guid chatId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PinnedMessage>>(Pins.Where(pin => pin.ChatId == chatId).ToArray());
        public void RemovePin(PinnedMessage pin) => Pins.Remove(pin);

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
        public Task<DirectChatPair?> FindDirectPairAsync(Guid lowerUserId, Guid higherUserId, CancellationToken ct) => Task.FromResult<DirectChatPair?>(null);
        public Task<Chat?> FindChatAsync(Guid chatId, CancellationToken ct) => Task.FromResult(Chats.SingleOrDefault(chat => chat.Id == chatId));
        public Task<ChatMember?> FindMemberAsync(Guid chatId, Guid userId, CancellationToken ct) =>
            Task.FromResult(Members.SingleOrDefault(member => member.ChatId == chatId && member.UserId == userId));
        public Task<IReadOnlyList<ChatMember>> ListMembersAsync(Guid chatId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMember>>(Members.Where(member => member.ChatId == chatId).ToArray());
        public Task<IReadOnlyList<Guid>> ListActiveChatIdsAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Members.Where(member => member.UserId == userId && member.IsActive)
                .Select(member => member.ChatId).ToArray());
        public Task<IReadOnlyList<(Chat Chat, ChatMember Member)>> ListChatsAsync(Guid userId, DateTimeOffset? after, Guid? afterId, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(Chat, ChatMember)>>([]);
        public Task AddDirectAsync(Chat chat, DirectChatPair pair, IReadOnlyCollection<ChatMember> members, CancellationToken ct) => throw new NotSupportedException();
        public Task AddGroupAsync(Chat chat, IReadOnlyCollection<ChatMember> members, CancellationToken ct) => throw new NotSupportedException();
        public Task AddMemberAsync(ChatMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<long> AdvanceReadPositionAsync(Guid chatId, Guid userId, long sequence, CancellationToken ct)
        {
            var member = Members.Single(value => value.ChatId == chatId && value.UserId == userId);
            member.MarkRead(sequence);
            return Task.FromResult(member.LastReadSequence);
        }
    }
}
