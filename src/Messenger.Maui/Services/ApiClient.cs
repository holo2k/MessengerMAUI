using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Users;

namespace Messenger.Maui.Services;

public sealed record AuthLoginResult(AuthSessionResponse? Session, PendingTwoFactorResponse? PendingTwoFactor)
{
    public static AuthLoginResult Authenticated(AuthSessionResponse session) => new(session, null);
    public static AuthLoginResult RequiresTwoFactor(PendingTwoFactorResponse pending) => new(null, pending);
}

public interface IAuthApi
{
    Task<ChallengeResponse> RequestChallengeAsync(ChallengeRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionResponse> RegisterAsync(CompleteChallengeRequest request, CancellationToken cancellationToken = default);
    Task<AuthLoginResult> LoginAsync(CompleteChallengeRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionResponse> ConfirmTwoFactorAsync(string pendingToken, string code, CancellationToken cancellationToken = default);
    Task<AuthSessionResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
}

public sealed class ApiClient(HttpClient httpClient) : IAuthApi
{
    public async Task<ChallengeResponse> RequestChallengeAsync(
        ChallengeRequest request,
        CancellationToken cancellationToken = default) =>
        await PostAsync<ChallengeRequest, ChallengeResponse>("api/auth/challenges", request, cancellationToken);

    public async Task<AuthSessionResponse> RegisterAsync(
        CompleteChallengeRequest request,
        CancellationToken cancellationToken = default) =>
        await PostAsync<CompleteChallengeRequest, AuthSessionResponse>("api/auth/register", request, cancellationToken);

    public async Task<AuthLoginResult> LoginAsync(
        CompleteChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/login", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var pending = await response.Content.ReadFromJsonAsync<PendingTwoFactorResponse>(cancellationToken);
            return AuthLoginResult.RequiresTwoFactor(pending ?? throw new InvalidDataException("Missing 2FA response."));
        }

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken);
        return AuthLoginResult.Authenticated(session ?? throw new InvalidDataException("Missing auth session."));
    }

    public async Task<AuthSessionResponse> ConfirmTwoFactorAsync(
        string pendingToken,
        string code,
        CancellationToken cancellationToken = default) =>
        await PostAsync<ConfirmLoginTwoFactorRequest, AuthSessionResponse>(
            "api/auth/2fa/confirm", new ConfirmLoginTwoFactorRequest(pendingToken, code), cancellationToken);

    public async Task<AuthSessionResponse> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default) =>
        await PostAsync<RefreshRequest, AuthSessionResponse>("api/auth/refresh", request, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidDataException($"The server returned no {typeof(TResponse).Name} payload.");
    }
}
