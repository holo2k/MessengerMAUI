using Messenger.Domain.Identity;

namespace Messenger.Domain.Tests.Identity;

public sealed class RefreshSessionTests
{
    [Fact]
    public void Session_is_inactive_at_its_exact_expiry_instant()
    {
        var expiresAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var session = new RefreshSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Android",
            Guid.NewGuid(),
            "hash",
            expiresAt.AddDays(-30),
            expiresAt);

        Assert.False(session.IsActive(expiresAt));
    }
}
