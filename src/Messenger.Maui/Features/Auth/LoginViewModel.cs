using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Auth;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Auth;

public sealed class LoginViewModel(
    IAuthApi authApi,
    ISecureSessionStore sessionStore,
    INavigationService navigation) : ObservableObject
{
    private string _phone = string.Empty;
    private string _code = string.Empty;
    private Guid? _challengeId;
    private string? _pendingTwoFactorToken;

    public string CountryCode => "+7";
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public Guid? ChallengeId { get => _challengeId; private set => SetProperty(ref _challengeId, value); }
    public string? PendingTwoFactorToken
    {
        get => _pendingTwoFactorToken;
        private set => SetProperty(ref _pendingTwoFactorToken, value);
    }
    public bool RequiresTwoFactor => PendingTwoFactorToken is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.GetAsync(cancellationToken);
        if (session?.Session.RefreshTokenExpiresAt > DateTimeOffset.UtcNow)
        {
            await navigation.ShowAuthenticatedAsync();
        }
    }

    public async Task RequestCodeAsync(CancellationToken cancellationToken = default)
    {
        var challenge = await authApi.RequestChallengeAsync(
            new ChallengeRequest(RussianPhoneNumber.Compose(Phone), "RU", "login"), cancellationToken);
        ChallengeId = challenge.ChallengeId;
    }

    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        var challengeId = ChallengeId ?? throw new InvalidOperationException("Сначала запросите код.");
        var result = await authApi.LoginAsync(
            new CompleteChallengeRequest(challengeId, Code, ClientDeviceLabel.Value), cancellationToken);
        if (result.Session is not null)
        {
            await CompleteLoginAsync(result.Session, cancellationToken);
            return;
        }

        PendingTwoFactorToken = result.PendingTwoFactor?.PendingToken
            ?? throw new InvalidDataException("Сервер не вернул данные двухфакторной аутентификации.");
        OnPropertyChanged(nameof(RequiresTwoFactor));
    }

    public async Task ConfirmTwoFactorAsync(CancellationToken cancellationToken = default)
    {
        var pendingToken = PendingTwoFactorToken
            ?? throw new InvalidOperationException("Двухфакторная проверка не запрошена.");
        var session = await authApi.ConfirmTwoFactorAsync(pendingToken, Code, cancellationToken);
        await CompleteLoginAsync(session, cancellationToken);
    }

    private async Task CompleteLoginAsync(AuthSessionResponse session, CancellationToken cancellationToken)
    {
        await sessionStore.SaveAsync(new ClientSession(session, ClientDeviceLabel.Value), cancellationToken);
        await navigation.ShowAuthenticatedAsync();
    }
}
