using Messenger.Domain.Common;

namespace Messenger.Domain.Identity;

public sealed class RefreshSession : Entity
{
    private RefreshSession()
    {
    }

    public RefreshSession(
        Guid id,
        Guid userId,
        string deviceLabel,
        Guid tokenFamilyId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        DeviceLabel = deviceLabel;
        TokenFamilyId = tokenFamilyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public string DeviceLabel { get; private set; } = string.Empty;
    public Guid TokenFamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? ReplacedBySessionId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ReplacedBySessionId is null && now < ExpiresAt;

    public void ReplaceWith(Guid replacementId) => ReplacedBySessionId = replacementId;

    public void Revoke(DateTimeOffset now, string reason)
    {
        RevokedAt ??= now;
        RevocationReason ??= reason;
    }
}
