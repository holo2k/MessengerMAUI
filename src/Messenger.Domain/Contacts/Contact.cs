using Messenger.Domain.Common;

namespace Messenger.Domain.Contacts;

public sealed class Contact : Entity
{
    private Contact()
    {
    }

    public Contact(
        Guid id,
        Guid ownerUserId,
        Guid targetUserId,
        string? localFirstName,
        string? localLastName,
        bool isMuted,
        DateTimeOffset createdAt)
    {
        if (ownerUserId == targetUserId)
        {
            throw new ArgumentException("A user cannot add themselves as a contact.", nameof(targetUserId));
        }

        Id = id;
        OwnerUserId = ownerUserId;
        TargetUserId = targetUserId;
        CreatedAt = createdAt;
        Rename(localFirstName, localLastName);
        IsMuted = isMuted;
    }

    public Guid OwnerUserId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public string? LocalFirstName { get; private set; }
    public string? LocalLastName { get; private set; }
    public bool IsMuted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string? firstName, string? lastName)
    {
        LocalFirstName = Normalize(firstName);
        LocalLastName = Normalize(lastName);
    }

    public void SetMuted(bool isMuted) => IsMuted = isMuted;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
