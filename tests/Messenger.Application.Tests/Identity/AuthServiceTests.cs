using Messenger.Application.Identity;
using Messenger.Application.Security;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;

namespace Messenger.Application.Tests.Identity;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Development_challenge_sends_fixed_code_111111()
    {
        var fixture = new AuthFixture();

        await fixture.Service.RequestChallengeAsync(
            new PhoneChallengeRequest("8 (999) 123-45-67", "RU", ChallengePurpose.Register));

        Assert.Equal(("+79991234567", "111111"), fixture.Sms.LastMessage);
    }

    [Fact]
    public async Task Expired_challenge_cannot_register()
    {
        var fixture = new AuthFixture();
        var challenge = await fixture.RequestRegistrationChallengeAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromTicks(1)));

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                challenge.ChallengeId, "111111", "Pixel")));

        Assert.Equal(AuthErrorCode.ChallengeExpired, error.Code);
    }

    [Fact]
    public async Task Challenge_is_expired_at_its_exact_expiry_instant()
    {
        var fixture = new AuthFixture();
        var challenge = await fixture.RequestRegistrationChallengeAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                challenge.ChallengeId, "111111", "Pixel")));

        Assert.Equal(AuthErrorCode.ChallengeExpired, error.Code);
    }

    [Fact]
    public async Task Challenge_rejects_correct_code_after_five_failed_attempts()
    {
        var fixture = new AuthFixture();
        var challenge = await fixture.RequestRegistrationChallengeAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var error = await Assert.ThrowsAsync<AuthException>(() =>
                fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                    challenge.ChallengeId, "000000", "Pixel")));
            Assert.Equal(AuthErrorCode.InvalidCode, error.Code);
        }

        var blocked = await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                challenge.ChallengeId, "111111", "Pixel")));
        Assert.Equal(AuthErrorCode.TooManyAttempts, blocked.Code);
    }

    [Fact]
    public async Task Consumed_challenge_cannot_be_used_twice()
    {
        var fixture = new AuthFixture();
        var challenge = await fixture.RequestRegistrationChallengeAsync();
        await fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
            challenge.ChallengeId, "111111", "Pixel"));

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                challenge.ChallengeId, "111111", "Pixel")));

        Assert.Equal(AuthErrorCode.ChallengeConsumed, error.Code);
    }

    [Fact]
    public async Task Register_rejects_a_second_account_for_the_same_normalized_phone()
    {
        var fixture = new AuthFixture();
        var first = await fixture.RequestRegistrationChallengeAsync("8 (999) 123-45-67");
        await fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
            first.ChallengeId, "111111", "Pixel"));
        var second = await fixture.RequestRegistrationChallengeAsync("+79991234567");

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
                second.ChallengeId, "111111", "iPhone")));

        Assert.Equal(AuthErrorCode.PhoneAlreadyRegistered, error.Code);
    }

    [Fact]
    public async Task Registration_issues_fifteen_minute_access_and_thirty_day_refresh_expiry()
    {
        var fixture = new AuthFixture();
        var challenge = await fixture.RequestRegistrationChallengeAsync();

        var session = await fixture.Service.RegisterAsync(new CompletePhoneChallengeRequest(
            challenge.ChallengeId, "111111", "Pixel"));

        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(15), session.AccessTokenExpiresAt);
        Assert.Equal(fixture.Clock.UtcNow.AddDays(30), session.RefreshTokenExpiresAt);
    }

    private sealed class AuthFixture
    {
        public AuthFixture()
        {
            Service = new AuthService(
                Store,
                new PassthroughCipher(),
                new PassthroughBlindIndex(),
                new PrefixCodeHasher(),
                Sms,
                new StubAccessTokenIssuer(),
                new SequenceRefreshTokenGenerator(),
                Clock,
                new AuthOptions());
        }

        public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        public InMemoryIdentityStore Store { get; } = new();
        public RecordingSmsSender Sms { get; } = new();
        public AuthService Service { get; }

        public Task<PhoneChallengeResult> RequestRegistrationChallengeAsync(string phone = "+79991234567") =>
            Service.RequestChallengeAsync(new PhoneChallengeRequest(phone, "RU", ChallengePurpose.Register));
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        public (string Phone, string Code)? LastMessage { get; private set; }

        public Task SendCodeAsync(PhoneNumber phone, string code, CancellationToken cancellationToken)
        {
            LastMessage = (phone.E164, code);
            return Task.CompletedTask;
        }
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

    private sealed class PrefixCodeHasher : IChallengeCodeHasher
    {
        public string Compute(Guid challengeId, string code) => $"{challengeId:N}:{code}";
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public string Issue(Guid userId, DateTimeOffset expiresAt) => $"access:{userId:N}:{expiresAt:O}";
    }

    private sealed class SequenceRefreshTokenGenerator : IRefreshTokenGenerator
    {
        private int _next;
        public string Generate() => $"refresh-{Interlocked.Increment(ref _next)}";
        public string Hash(string token) => $"hash:{token}";
    }

    private sealed class InMemoryIdentityStore : IIdentityStore
    {
        private readonly Dictionary<Guid, LoginChallenge> _challenges = [];
        private readonly Dictionary<string, User> _users = [];

        public Task AddChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken)
        {
            _challenges.Add(challenge.Id, challenge);
            return Task.CompletedTask;
        }

        public Task<LoginChallenge?> FindChallengeAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_challenges.GetValueOrDefault(id));

        public Task<User?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken) =>
            Task.FromResult(_users.GetValueOrDefault(phoneIndex));

        public Task AddUserAsync(User user, CancellationToken cancellationToken)
        {
            _users.Add(user.PhoneBlindIndex, user);
            return Task.CompletedTask;
        }

        public Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RefreshSession?> FindRefreshSessionAsync(
            string tokenHash,
            CancellationToken cancellationToken) => Task.FromResult<RefreshSession?>(null);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RefreshRotationOutcome> RotateRefreshTokenAsync(
            string presentedTokenHash,
            RefreshSession replacement,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RevokeSessionAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAllSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
