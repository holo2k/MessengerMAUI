using Messenger.Application.Chats;
using Messenger.Application.Contacts;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Contacts;

namespace Messenger.Application.Tests.Chats;

public sealed class ContactChatServiceTests
{
    [Fact]
    public async Task Adding_a_duplicate_contact_is_rejected_and_alias_stays_owner_local()
    {
        var store = new MemorySocialStore();
        var ownerId = store.AddUser();
        var targetId = store.AddUser();
        var service = new ContactService(store, new FixedClock());

        var contact = await service.AddAsync(ownerId, targetId, "Мой", "Контакт");
        var error = await Assert.ThrowsAsync<ContactRuleException>(() =>
            service.AddAsync(ownerId, targetId, "Другой", "Псевдоним"));

        Assert.Equal(ContactRuleError.Duplicate, error.Code);
        Assert.Equal("Мой", contact.LocalFirstName);
        Assert.Equal(targetId, contact.TargetUserId);
    }

    [Fact]
    public async Task Creating_the_same_direct_chat_reuses_it_and_reactivates_hidden_membership()
    {
        var store = new MemorySocialStore();
        var firstId = store.AddUser();
        var secondId = store.AddUser();
        var service = new ChatService(store, new FixedClock());
        var created = await service.CreateDirectAsync(firstId, secondId);
        store.Members.Single(member => member.ChatId == created.Id && member.UserId == firstId)
            .Hide(FixedClock.Now);

        var reused = await service.CreateDirectAsync(firstId, secondId);

        Assert.Equal(created.Id, reused.Id);
        Assert.Single(store.Chats);
        Assert.Null(store.Members.Single(member => member.ChatId == created.Id && member.UserId == firstId).HiddenAt);
    }

    private sealed class FixedClock : IClock
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 3, 7, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class MemorySocialStore : IContactStore, IChatStore
    {
        public HashSet<Guid> Users { get; } = [];
        public List<Contact> Contacts { get; } = [];
        public List<Chat> Chats { get; } = [];
        public List<DirectChatPair> Pairs { get; } = [];
        public List<ChatMember> Members { get; } = [];

        public Guid AddUser()
        {
            var id = Guid.NewGuid();
            Users.Add(id);
            return id;
        }

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.Contains(userId));

        public Task<Contact?> FindByUsersAsync(Guid ownerUserId, Guid targetUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Contacts.SingleOrDefault(contact =>
                contact.OwnerUserId == ownerUserId && contact.TargetUserId == targetUserId));

        public Task<Contact?> FindOwnedAsync(Guid ownerUserId, Guid contactId, CancellationToken cancellationToken) =>
            Task.FromResult(Contacts.SingleOrDefault(contact => contact.OwnerUserId == ownerUserId && contact.Id == contactId));

        public Task<IReadOnlyList<Contact>> ListAsync(Guid ownerUserId, DateTimeOffset? afterCreatedAt, Guid? afterId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Contact>>(Contacts.Where(contact => contact.OwnerUserId == ownerUserId).Take(take).ToArray());

        public Task<Guid?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<Guid?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task AddAsync(Contact contact, CancellationToken cancellationToken)
        {
            Contacts.Add(contact);
            return Task.CompletedTask;
        }

        public void Remove(Contact contact) => Contacts.Remove(contact);

        public Task<DirectChatPair?> FindDirectPairAsync(Guid lowerUserId, Guid higherUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Pairs.SingleOrDefault(pair =>
                pair.LowerUserId == lowerUserId && pair.HigherUserId == higherUserId));

        public Task<Chat?> FindChatAsync(Guid chatId, CancellationToken cancellationToken) =>
            Task.FromResult(Chats.SingleOrDefault(chat => chat.Id == chatId));

        public Task<ChatMember?> FindMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Members.SingleOrDefault(member => member.ChatId == chatId && member.UserId == userId));

        public Task<IReadOnlyList<ChatMember>> ListMembersAsync(Guid chatId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMember>>(Members.Where(member => member.ChatId == chatId).ToArray());

        public Task<IReadOnlyList<Guid>> ListActiveChatIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Members.Where(member => member.UserId == userId && member.IsActive)
                .Select(member => member.ChatId).ToArray());

        public Task<IReadOnlyList<(Chat Chat, ChatMember Member)>> ListChatsAsync(Guid userId, DateTimeOffset? afterUpdatedAt, Guid? afterId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(Chat, ChatMember)>>([]);

        public Task AddDirectAsync(Chat chat, DirectChatPair pair, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken)
        {
            Chats.Add(chat);
            Pairs.Add(pair);
            Members.AddRange(members);
            return Task.CompletedTask;
        }

        public Task AddGroupAsync(Chat chat, IReadOnlyCollection<ChatMember> members, CancellationToken cancellationToken)
        {
            Chats.Add(chat);
            Members.AddRange(members);
            return Task.CompletedTask;
        }

        public Task AddMemberAsync(ChatMember member, CancellationToken cancellationToken)
        {
            Members.Add(member);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
