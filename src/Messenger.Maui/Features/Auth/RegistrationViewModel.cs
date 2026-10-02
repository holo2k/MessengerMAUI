using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Auth;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Auth;

public sealed class RegistrationViewModel(
    IAuthApi authApi,
    ISecureSessionStore sessionStore,
    INavigationService navigation) : ObservableObject
{
    private string _phone = string.Empty;
    private string _code = string.Empty;
    private Guid? _challengeId;

    public string CountryCode => "+7";
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public Guid? ChallengeId { get => _challengeId; private set => SetProperty(ref _challengeId, value); }

    public async Task RequestCodeAsync(CancellationToken cancellationToken = default)
    {
        var challenge = await authApi.RequestChallengeAsync(
            new ChallengeRequest(RussianPhoneNumber.Compose(Phone), "RU", "register"), cancellationToken);
        ChallengeId = challenge.ChallengeId;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var challengeId = ChallengeId ?? throw new InvalidOperationException("Сначала запросите код.");
        var session = await authApi.RegisterAsync(
            new CompleteChallengeRequest(challengeId, Code, ClientDeviceLabel.Value), cancellationToken);
        await sessionStore.SaveAsync(new ClientSession(session, ClientDeviceLabel.Value), cancellationToken);
        await navigation.ShowAuthenticatedAsync();
    }
}

internal static class RussianPhoneNumber
{
    public static string Compose(string input)
    {
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] is '7' or '8')
        {
            digits = digits[1..];
        }
        if (digits.Length != 10)
        {
            throw new FormatException("Введите 10 цифр российского номера.");
        }
        return $"+7{digits}";
    }
}

internal static class ClientDeviceLabel
{
    public const string Value = "Android/iOS";
}
