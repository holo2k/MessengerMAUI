using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

public sealed class PendingLogin : Entity
{
    private PendingLogin()
    {
    }

    public PendingLogin(
        Guid id,
        Guid userId,
        Guid emailChallengeId,
        string deviceLabel,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        EmailChallengeId = emailChallengeId;
        DeviceLabel = deviceLabel;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public Guid EmailChallengeId { get; private set; }
    public string DeviceLabel { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool CanConsume(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;
    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}
