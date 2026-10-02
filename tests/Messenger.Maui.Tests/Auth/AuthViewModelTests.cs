using Messenger.Contracts.Auth;
using Messenger.Maui.Features.Auth;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Auth;

public sealed class AuthViewModelTests
{
    [Fact]
    public async Task Registration_code_request_composes_a_canonical_russian_phone()
    {
        var api = new RecordingAuthApi();
        var viewModel = new RegistrationViewModel(api, new MemorySessionStore(), new RecordingNavigation());
        viewModel.Phone = "8 (999) 123-45-67";

        await viewModel.RequestCodeAsync();

        Assert.Equal(new ChallengeRequest("+79991234567", "RU", "register"), api.LastChallengeRequest);
        Assert.Equal(api.Challenge.ChallengeId, viewModel.ChallengeId);
    }

    [Fact]
    public async Task Registration_saves_the_session_and_opens_the_authenticated_shell()
    {
        var api = new RecordingAuthApi();
        var store = new MemorySessionStore();
        var navigation = new RecordingNavigation();
        var viewModel = new RegistrationViewModel(api, store, navigation)
        {
            Phone = "9991234567",
            Code = "111111"
        };
        await viewModel.RequestCodeAsync();

        await viewModel.RegisterAsync();

        Assert.Equal("Android/iOS", store.Current?.DeviceLabel);
        Assert.Equal(api.Session.RefreshToken, store.Current?.Session.RefreshToken);
        Assert.Equal(1, navigation.AuthenticatedCount);
    }

    [Fact]
    public async Task Login_initialization_restores_an_existing_session()
    {
        var store = new MemorySessionStore
        {
            Current = new ClientSession(RecordingAuthApi.CreateSession("restored"), "iPhone")
        };
        var navigation = new RecordingNavigation();
        var viewModel = new LoginViewModel(new RecordingAuthApi(), store, navigation);

        await viewModel.InitializeAsync();

        Assert.Equal(1, navigation.AuthenticatedCount);
    }

    [Fact]
    public async Task Login_saves_the_new_session_and_navigates()
    {
        var api = new RecordingAuthApi();
        var store = new MemorySessionStore();
        var navigation = new RecordingNavigation();
        var viewModel = new LoginViewModel(api, store, navigation)
        {
            Phone = "9991234567",
            Code = "111111"
        };
        await viewModel.RequestCodeAsync();

        await viewModel.LoginAsync();

        Assert.Equal(api.Session.AccessToken, store.Current?.Session.AccessToken);
        Assert.Equal(1, navigation.AuthenticatedCount);
    }

    internal sealed class RecordingAuthApi : IAuthApi
    {
        public ChallengeResponse Challenge { get; } = new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        public AuthSessionResponse Session { get; } = CreateSession("new");
        public ChallengeRequest? LastChallengeRequest { get; private set; }
        public int RefreshCount => _refreshCount;
        public Exception? RefreshFailure { get; set; }
        public TimeSpan RefreshDelay { get; set; }

        public Task<ChallengeResponse> RequestChallengeAsync(ChallengeRequest request, CancellationToken cancellationToken = default)
        {
            LastChallengeRequest = request;
            return Task.FromResult(Challenge);
        }

        public Task<AuthSessionResponse> RegisterAsync(CompleteChallengeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task<AuthLoginResult> LoginAsync(CompleteChallengeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthLoginResult.Authenticated(Session));

        public Task<AuthSessionResponse> ConfirmTwoFactorAsync(string pendingToken, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public async Task<AuthSessionResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshCount);
            if (RefreshDelay > TimeSpan.Zero)
            {
                await Task.Delay(RefreshDelay, cancellationToken);
            }
            if (RefreshFailure is not null)
            {
                throw RefreshFailure;
            }
            return Session;
        }

        private int _refreshCount;
        public static AuthSessionResponse CreateSession(string suffix) => new(
            Guid.NewGuid(), $"access-{suffix}", DateTimeOffset.UtcNow.AddMinutes(15),
            $"refresh-{suffix}", DateTimeOffset.UtcNow.AddDays(30));
    }

    internal sealed class MemorySessionStore : ISecureSessionStore
    {
        public ClientSession? Current { get; set; }
        public Task<ClientSession?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(ClientSession session, CancellationToken cancellationToken = default)
        {
            Current = session;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Current = null;
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingNavigation : INavigationService
    {
        public int AuthenticatedCount { get; private set; }
        public int LoginCount { get; private set; }
        public Task ShowAuthenticatedAsync()
        {
            AuthenticatedCount++;
            return Task.CompletedTask;
        }
        public Task ShowLoginAsync()
        {
            LoginCount++;
            return Task.CompletedTask;
        }
    }
}
