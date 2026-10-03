using Messenger.Domain.Contacts;
using Messenger.Application.Users;

namespace Messenger.Application.Contacts;

public enum ContactRuleError
{
    SelfContact,
    Duplicate,
    UserNotFound,
    ContactNotFound
}

public sealed class ContactRuleException(ContactRuleError code) : Exception(code.ToString())
{
    public ContactRuleError Code { get; } = code;
}

public interface IContactStore
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<Contact?> FindByUsersAsync(Guid ownerUserId, Guid targetUserId, CancellationToken cancellationToken);
    Task<Contact?> FindOwnedAsync(Guid ownerUserId, Guid contactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Contact>> ListAsync(
        Guid ownerUserId,
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken);
    Task<Guid?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken);
    Task<Guid?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken);
    Task AddAsync(Contact contact, CancellationToken cancellationToken);
    void Remove(Contact contact);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ContactView(Contact Contact, VisibleProfile Profile);
public sealed record ContactPage(IReadOnlyList<ContactView> Items, string? NextCursor);

public interface IContactService
{
    Task<Contact> AddAsync(
        Guid ownerUserId,
        Guid targetUserId,
        string? localFirstName,
        string? localLastName,
        CancellationToken cancellationToken = default);
    Task<ContactPage> ListAsync(Guid ownerUserId, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<ContactView> UpdateAsync(
        Guid ownerUserId,
        Guid contactId,
        string? localFirstName,
        string? localLastName,
        bool muted,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid ownerUserId, Guid contactId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VisibleProfile>> SearchUsersAsync(
        Guid requesterUserId,
        string query,
        CancellationToken cancellationToken = default);
    Task<VisibleProfile> GetUserAsync(Guid requesterUserId, Guid targetUserId, CancellationToken cancellationToken = default);
}
