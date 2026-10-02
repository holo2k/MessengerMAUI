using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

public sealed class UserProfile : Entity
{
    private UserProfile()
    {
    }

    public UserProfile(Guid userId)
    {
        Id = userId;
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Username { get; private set; }
    public string? NormalizedUsername { get; private set; }
    public string? Bio { get; private set; }
    public string? AvatarObjectId { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }

    public void Update(string firstName, string lastName, string? username, string? bio)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        NormalizedUsername = Username?.ToUpperInvariant();
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
    }

    public void SetAvatar(string? objectId) => AvatarObjectId = objectId;
}
