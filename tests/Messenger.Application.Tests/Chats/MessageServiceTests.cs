using Messenger.Application.Chats;
using Messenger.Application.Security;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Tests.Chats;

public sealed class MessageServiceTests
{
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
    public async Task Search_matches_complete_words_without_storing_plaintext_and_tampering_fails_closed()
    {
        var fixture = new Fixture();
        var sent = await fixture.Service.SendAsync(
            fixture.UserId, fixture.Chat.Id, Guid.NewGuid(), MessageKind.Text, "Привет, безопасный мир!");

        var exact = await fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "безопасный", 20);
        var partial = await fixture.Service.SearchAsync(fixture.UserId, fixture.Chat.Id, "безопас", 20);

        Assert.Equal(sent.Message.Id, Assert.Single(exact).Message.Id);
        Assert.Empty(partial);
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
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            UserId = Guid.NewGuid();
            Chat = new Chat(Guid.NewGuid(), ChatType.Group, UserId, "Группа", Now);
            Store.Chats.Add(Chat);
            Store.Members.Add(new ChatMember(Chat.Id, UserId, ChatMemberRole.Owner, Now));
            Service = new MessageService(Store, Store, Cipher, new TestIndex(), new Clock());
        }

        public Guid UserId { get; }
        public Chat Chat { get; }
        public MemoryMessageStore Store { get; } = new();
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
        public Task<IReadOnlyList<Message>> SearchMessagesAsync(Guid chatId, Guid userId, IReadOnlyCollection<string> tokens, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Message>>(Messages.Where(message => message.ChatId == chatId &&
                    Hidden.All(hidden => hidden.MessageId != message.Id || hidden.UserId != userId) &&
                    tokens.All(token => Tokens.Any(search => search.MessageId == message.Id && search.Token == token)))
                .Take(take).ToArray());
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
        public Task<IReadOnlyList<(Chat Chat, ChatMember Member)>> ListChatsAsync(Guid userId, DateTimeOffset? after, Guid? afterId, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(Chat, ChatMember)>>([]);
        public Task AddDirectAsync(Chat chat, DirectChatPair pair, IReadOnlyCollection<ChatMember> members, CancellationToken ct) => throw new NotSupportedException();
        public Task AddGroupAsync(Chat chat, IReadOnlyCollection<ChatMember> members, CancellationToken ct) => throw new NotSupportedException();
        public Task AddMemberAsync(ChatMember member, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
