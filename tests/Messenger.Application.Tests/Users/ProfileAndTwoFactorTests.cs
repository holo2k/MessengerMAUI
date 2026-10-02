using Messenger.Application.Security;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests.Users;

public sealed class ProfileAndTwoFactorTests
{
    [Fact]
    public async Task Email_confirmation_code_is_single_use()
    {
        var fixture = new TwoFactorFixture();
        var userId = Guid.NewGuid();
        var challenge = await fixture.Service.BeginEmailSetupAsync(userId, "user@example.test");
        await fixture.Service.ConfirmEmailAsync(userId, challenge.ChallengeId, "123456");

        var error = await Assert.ThrowsAsync<UserSettingsException>(() =>
            fixture.Service.ConfirmEmailAsync(userId, challenge.ChallengeId, "123456"));

        Assert.Equal(UserSettingsError.CodeConsumed, error.Code);
        Assert.True(fixture.Store.Security.Single().TwoFactorEnabled);
    }

    [Fact]
    public async Task Smtp_failure_does_not_enable_two_factor_authentication()
    {
        var fixture = new TwoFactorFixture { Email = { Failure = new IOException("smtp unavailable") } };
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Service.BeginEmailSetupAsync(userId, "user@example.test"));

        Assert.DoesNotContain(fixture.Store.Security, settings => settings.TwoFactorEnabled);
    }

    [Fact]
    public async Task Pending_login_token_and_code_are_single_use()
    {
        var fixture = new TwoFactorFixture();
        var userId = Guid.NewGuid();
        fixture.Store.Security.Add(UserSecuritySettings.Enabled(
            userId, "user@example.test", "nonce", "tag", 1));
        var pending = await fixture.Service.BeginLoginAsync(userId, "iPhone");

        var verified = await fixture.Service.ConfirmLoginAsync(pending.PendingToken, "123456");

        Assert.Equal(userId, verified.UserId);
        Assert.Equal("iPhone", verified.DeviceLabel);
        var error = await Assert.ThrowsAsync<UserSettingsException>(() =>
            fixture.Service.ConfirmLoginAsync(pending.PendingToken, "123456"));
        Assert.Equal(UserSettingsError.CodeConsumed, error.Code);
    }

    [Fact]
    public async Task Username_uniqueness_is_case_insensitive()
    {
        var fixture = new ProfileFixture();
        var alice = fixture.AddUser("+79990000101");
        var bob = fixture.AddUser("+79990000102");
        await fixture.Service.UpdateProfileAsync(alice.Id, new UpdateProfileRequest("Alice", "A", "Alice", null));

        var error = await Assert.ThrowsAsync<UserSettingsException>(() =>
            fixture.Service.UpdateProfileAsync(bob.Id, new UpdateProfileRequest("Bob", "B", "alice", null)));

        Assert.Equal(UserSettingsError.UsernameTaken, error.Code);
    }

    [Fact]
    public async Task Privacy_hides_phone_and_avatar_from_non_contacts()
    {
        var fixture = new ProfileFixture();
        var owner = fixture.AddUser("+79990000103");
        var stranger = fixture.AddUser("+79990000104");
        await fixture.Service.UpdateProfileAsync(owner.Id, new UpdateProfileRequest("Owner", "", "owner", "bio"));
        await fixture.Service.UpdateAvatarAsync(owner.Id, "avatar-object");
        await fixture.Service.UpdatePrivacyAsync(owner.Id, new PrivacyUpdateRequest(
            PrivacyVisibility.Contacts,
            PrivacyVisibility.Contacts,
            PrivacyVisibility.Nobody));

        var visible = await fixture.Service.GetProfileAsync(owner.Id, stranger.Id);

        Assert.Null(visible.Phone);
        Assert.Null(visible.AvatarObjectId);
        Assert.Null(visible.LastSeenAt);
    }

    [Fact]
    public async Task Revoking_another_users_session_returns_not_found()
    {
        var fixture = new ProfileFixture();
        var owner = fixture.AddUser("+79990000105");
        var attacker = fixture.AddUser("+79990000106");
        var session = fixture.AddSession(owner.Id);

        var revoked = await fixture.Service.RevokeSessionAsync(attacker.Id, session.Id);

        Assert.False(revoked);
        Assert.True(session.IsActive(fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task Phone_change_requires_a_recent_consumed_challenge()
    {
        var fixture = new ProfileFixture();
        var user = fixture.AddUser("+79990000107");
        var challenge = fixture.AddPhoneChangeChallenge(
            "+79990000108",
            fixture.Clock.UtcNow.Subtract(TimeSpan.FromMinutes(11)));

        var error = await Assert.ThrowsAsync<UserSettingsException>(() =>
            fixture.Service.ChangePhoneAsync(user.Id, challenge.Id, "111111"));

        Assert.Equal(UserSettingsError.RecentChallengeRequired, error.Code);
    }

    private sealed class ProfileFixture
    {
        public ProfileFixture()
        {
            Service = new ProfileService(
                Store,
                new PassthroughCipher(),
                new PassthroughBlindIndex(),
                new PrefixCodeHasher(),
                Clock);
        }

        public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero));
        public InMemoryUserSettingsStore Store { get; } = new();
        public ProfileService Service { get; }

        public User AddUser(string phone)
        {
            var user = new User(Guid.NewGuid(), phone, "nonce", "tag", 1, phone, Clock.UtcNow);
            Store.Users.Add(user);
            return user;
        }

        public RefreshSession AddSession(Guid userId)
        {
            var session = new RefreshSession(
                Guid.NewGuid(), userId, "Android", Guid.NewGuid(), Guid.NewGuid().ToString("N"),
                Clock.UtcNow, Clock.UtcNow.AddDays(30));
            Store.Sessions.Add(session);
            return session;
        }

        public LoginChallenge AddPhoneChangeChallenge(string phone, DateTimeOffset? consumedAt)
        {
            var challenge = new LoginChallenge(
                Guid.NewGuid(), ChallengePurpose.PhoneChange, phone, "nonce", "tag", 1, phone,
                "hash", Clock.UtcNow.AddMinutes(5), 5);
            if (consumedAt is not null)
            {
                challenge.MarkConsumed(consumedAt.Value);
            }
            Store.Challenges.Add(challenge);
            return challenge;
        }
    }

    private sealed class TwoFactorFixture
    {
        public TwoFactorFixture()
        {
            Service = new TwoFactorService(
                Store,
                Email,
                new PassthroughCipher(),
                new PrefixCodeHasher(),
                new FixedCodeGenerator(),
                new SequenceTokenGenerator(),
                Clock,
                new TwoFactorOptions());
        }

        public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero));
        public InMemoryTwoFactorStore Store { get; } = new();
        public RecordingEmailSender Email { get; } = new();
        public TwoFactorService Service { get; }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class PassthroughCipher : IFieldCipher
    {
        public EncryptedValue Encrypt(string plaintext) => new(plaintext, "nonce", "tag", 1);
        public string Decrypt(EncryptedValue value) => value.Ciphertext;
    }

    private sealed class PassthroughBlindIndex : IBlindIndex
    {
        public string Compute(string normalizedValue) => normalizedValue;
    }

    private sealed class PrefixCodeHasher : Messenger.Application.Identity.IChallengeCodeHasher
    {
        public string Compute(Guid challengeId, string code) => $"{challengeId:N}:{code}";
    }

    private sealed class FixedCodeGenerator : IOneTimeCodeGenerator
    {
        public string Generate() => "123456";
    }

    private sealed class SequenceTokenGenerator : IPendingLoginTokenGenerator
    {
        private int _value;
        public string Generate() => $"pending-{Interlocked.Increment(ref _value)}";
        public string Hash(string token) => $"hash:{token}";
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public Exception? Failure { get; set; }

        public Task SendTwoFactorCodeAsync(string recipient, string code, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTwoFactorStore : ITwoFactorStore
    {
        public List<UserSecuritySettings> Security { get; } = [];
        public List<EmailCodeChallenge> Challenges { get; } = [];
        public List<PendingLogin> PendingLogins { get; } = [];

        public Task<UserSecuritySettings?> FindSecurityAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Security.SingleOrDefault(settings => settings.UserId == userId));

        public Task<EmailCodeChallenge?> FindEmailChallengeAsync(Guid challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(Challenges.SingleOrDefault(challenge => challenge.Id == challengeId));

        public Task<PendingLogin?> FindPendingLoginAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(PendingLogins.SingleOrDefault(login => login.TokenHash == tokenHash));

        public Task AddSecurityAsync(UserSecuritySettings settings, CancellationToken cancellationToken)
        {
            Security.Add(settings);
            return Task.CompletedTask;
        }

        public Task AddEmailChallengeAsync(EmailCodeChallenge challenge, CancellationToken cancellationToken)
        {
            Challenges.Add(challenge);
            return Task.CompletedTask;
        }

        public Task AddPendingLoginAsync(PendingLogin pendingLogin, CancellationToken cancellationToken)
        {
            PendingLogins.Add(pendingLogin);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryUserSettingsStore : IUserSettingsStore
    {
        public List<User> Users { get; } = [];
        public List<UserProfile> Profiles { get; } = [];
        public List<UserPrivacySettings> Privacy { get; } = [];
        public List<RefreshSession> Sessions { get; } = [];
        public List<LoginChallenge> Challenges { get; } = [];

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.SingleOrDefault(profile => profile.UserId == userId));

        public Task<UserPrivacySettings?> FindPrivacyAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Privacy.SingleOrDefault(settings => settings.UserId == userId));

        public Task<bool> UsernameExistsAsync(string normalizedUsername, Guid exceptUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.Any(profile => profile.UserId != exceptUserId && profile.NormalizedUsername == normalizedUsername));

        public Task<bool> AreContactsAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<RefreshSession?> FindOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.UserId == userId && session.Id == sessionId));

        public Task<IReadOnlyList<RefreshSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RefreshSession>>(Sessions.Where(session => session.UserId == userId).ToArray());

        public Task<LoginChallenge?> FindChallengeAsync(Guid challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(Challenges.SingleOrDefault(challenge => challenge.Id == challengeId));

        public Task AddProfileAsync(UserProfile profile, CancellationToken cancellationToken)
        {
            Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task AddPrivacyAsync(UserPrivacySettings settings, CancellationToken cancellationToken)
        {
            Privacy.Add(settings);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
