using Messenger.Domain.Common;
using Messenger.Domain.Contacts;
using Messenger.Application.Security;
using Messenger.Application.Users;

namespace Messenger.Application.Contacts;

public sealed class ContactService(
    IContactStore store,
    IClock clock,
    IProfileService? profiles = null,
    IBlindIndex? blindIndex = null) : IContactService
{
    public async Task<Contact> AddAsync(
        Guid ownerUserId,
        Guid targetUserId,
        string? localFirstName,
        string? localLastName,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == targetUserId)
        {
            throw new ContactRuleException(ContactRuleError.SelfContact);
        }
        if (!await store.UserExistsAsync(targetUserId, cancellationToken))
        {
            throw new ContactRuleException(ContactRuleError.UserNotFound);
        }
        if (await store.FindByUsersAsync(ownerUserId, targetUserId, cancellationToken) is not null)
        {
            throw new ContactRuleException(ContactRuleError.Duplicate);
        }

        var contact = new Contact(
            Guid.NewGuid(), ownerUserId, targetUserId, localFirstName, localLastName, false, clock.UtcNow);
        await store.AddAsync(contact, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public async Task<ContactPage> ListAsync(
        Guid ownerUserId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureProfiles();
        limit = Math.Clamp(limit, 1, 100);
        var (afterCreatedAt, afterId) = DecodeCursor(cursor);
        var contacts = await store.ListAsync(ownerUserId, afterCreatedAt, afterId, limit + 1, cancellationToken);
        var hasMore = contacts.Count > limit;
        var page = contacts.Take(limit).ToArray();
        var views = new List<ContactView>(page.Length);
        foreach (var contact in page)
        {
            views.Add(new ContactView(contact,
                await profiles!.GetProfileAsync(contact.TargetUserId, ownerUserId, cancellationToken)));
        }
        return new ContactPage(views, hasMore ? EncodeCursor(page[^1]) : null);
    }

    public async Task<ContactView> UpdateAsync(
        Guid ownerUserId,
        Guid contactId,
        string? localFirstName,
        string? localLastName,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        EnsureProfiles();
        var contact = await store.FindOwnedAsync(ownerUserId, contactId, cancellationToken)
            ?? throw new ContactRuleException(ContactRuleError.ContactNotFound);
        contact.Rename(localFirstName, localLastName);
        contact.SetMuted(muted);
        await store.SaveChangesAsync(cancellationToken);
        return new ContactView(contact,
            await profiles!.GetProfileAsync(contact.TargetUserId, ownerUserId, cancellationToken));
    }

    public async Task DeleteAsync(Guid ownerUserId, Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await store.FindOwnedAsync(ownerUserId, contactId, cancellationToken)
            ?? throw new ContactRuleException(ContactRuleError.ContactNotFound);
        store.Remove(contact);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VisibleProfile>> SearchUsersAsync(
        Guid requesterUserId,
        string query,
        CancellationToken cancellationToken = default)
    {
        EnsureProfiles();
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        Guid? targetUserId = null;
        if (blindIndex is not null)
        {
            try
            {
                var phone = PhoneNumber.Parse(normalizedQuery, "RU");
                targetUserId = await store.FindUserByPhoneIndexAsync(
                    blindIndex.Compute(phone.E164), cancellationToken);
            }
            catch (FormatException)
            {
            }
        }
        targetUserId ??= await store.FindUserByUsernameAsync(
            normalizedQuery.ToUpperInvariant(), cancellationToken);
        if (targetUserId is null || targetUserId == requesterUserId)
        {
            return [];
        }
        return [await profiles!.GetProfileAsync(targetUserId.Value, requesterUserId, cancellationToken)];
    }

    public async Task<VisibleProfile> GetUserAsync(
        Guid requesterUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfiles();
        return await profiles!.GetProfileAsync(targetUserId, requesterUserId, cancellationToken);
    }

    private void EnsureProfiles()
    {
        if (profiles is null)
        {
            throw new InvalidOperationException("Profile services are required for this operation.");
        }
    }

    private static string EncodeCursor(Contact contact) => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{contact.CreatedAt.UtcTicks}|{contact.Id:D}"));

    private static (DateTimeOffset? CreatedAt, Guid? Id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (null, null);
        }
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split('|');
            return (new DateTimeOffset(long.Parse(parts[0]), TimeSpan.Zero), Guid.Parse(parts[1]));
        }
        catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException)
        {
            throw new FormatException("Invalid cursor.", exception);
        }
    }
}
