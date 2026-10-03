using System.Net.Http.Json;
using Messenger.Contracts.Support;
using Messenger.Contracts.Users;

namespace Messenger.Maui.Services;

public interface ISettingsApi
{
    Task<MeResponse> GetMeAsync(CancellationToken ct);
    Task UpdateMeAsync(UpdateMeRequest request, CancellationToken ct);
    Task<PrivacyResponse> GetPrivacyAsync(CancellationToken ct);
    Task UpdatePrivacyAsync(PrivacyRequest request, CancellationToken ct);
    Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(CancellationToken ct);
    Task RevokeSessionAsync(Guid id, CancellationToken ct);
    Task<EmailChallengeResponse> BeginTwoFactorAsync(string email, CancellationToken ct);
    Task ConfirmTwoFactorAsync(Guid id, string code, CancellationToken ct);
    Task DisableTwoFactorAsync(CancellationToken ct);
    Task<EmailChallengeResponse> BeginPhoneChangeAsync(string phone, CancellationToken ct);
    Task ConfirmPhoneChangeAsync(Guid id, string code, CancellationToken ct);
    Task<IReadOnlyList<SupportTicketResponse>> ListTicketsAsync(CancellationToken ct);
    Task<SupportConversationResponse> CreateTicketAsync(string subject, string body, CancellationToken ct);
    Task<AccountDeletionResponse> RequestDeletionAsync(CancellationToken ct);
    Task CancelDeletionAsync(CancellationToken ct);
}

public sealed class SettingsApi(HttpClient http) : ISettingsApi
{
    public async Task<MeResponse> GetMeAsync(CancellationToken ct) => await http.GetFromJsonAsync<MeResponse>("api/me/", ct) ?? throw new InvalidDataException("Missing profile.");
    public Task UpdateMeAsync(UpdateMeRequest request, CancellationToken ct) => EnsureAsync(http.PatchAsJsonAsync("api/me/", request, ct));
    public async Task<PrivacyResponse> GetPrivacyAsync(CancellationToken ct) => await http.GetFromJsonAsync<PrivacyResponse>("api/me/privacy", ct) ?? throw new InvalidDataException("Missing privacy settings.");
    public Task UpdatePrivacyAsync(PrivacyRequest request, CancellationToken ct) => EnsureAsync(http.PutAsJsonAsync("api/me/privacy", request, ct));
    public async Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(CancellationToken ct) => await http.GetFromJsonAsync<List<SessionResponse>>("api/me/sessions", ct) ?? [];
    public Task RevokeSessionAsync(Guid id, CancellationToken ct) => EnsureAsync(http.DeleteAsync($"api/me/sessions/{id}", ct));
    public async Task<EmailChallengeResponse> BeginTwoFactorAsync(string email, CancellationToken ct) => await PostAsync<EmailChallengeResponse>("api/me/2fa/email", new BeginEmailTwoFactorRequest(email), ct);
    public Task ConfirmTwoFactorAsync(Guid id, string code, CancellationToken ct) => EnsureAsync(http.PostAsJsonAsync("api/me/2fa/email/confirm", new ConfirmEmailTwoFactorRequest(id, code), ct));
    public Task DisableTwoFactorAsync(CancellationToken ct) => EnsureAsync(http.PostAsJsonAsync("api/me/2fa/disable", new { }, ct));
    public async Task<EmailChallengeResponse> BeginPhoneChangeAsync(string phone, CancellationToken ct) => await PostAsync<EmailChallengeResponse>("api/me/phone/challenges", new BeginPhoneChangeRequest(phone, "RU"), ct);
    public Task ConfirmPhoneChangeAsync(Guid id, string code, CancellationToken ct) => EnsureAsync(http.PutAsJsonAsync("api/me/phone", new ChangePhoneRequest(id, code), ct));
    public async Task<IReadOnlyList<SupportTicketResponse>> ListTicketsAsync(CancellationToken ct) => await http.GetFromJsonAsync<List<SupportTicketResponse>>("api/support/tickets", ct) ?? [];
    public async Task<SupportConversationResponse> CreateTicketAsync(string subject, string body, CancellationToken ct) => await PostAsync<SupportConversationResponse>("api/support/tickets", new CreateSupportTicketRequest(subject, body), ct);
    public async Task<AccountDeletionResponse> RequestDeletionAsync(CancellationToken ct) => await PostAsync<AccountDeletionResponse>("api/me/deletion/", new { }, ct);
    public Task CancelDeletionAsync(CancellationToken ct) => EnsureAsync(http.DeleteAsync("api/me/deletion/", ct));
    private async Task<T> PostAsync<T>(string uri, object body, CancellationToken ct) { using var response = await http.PostAsJsonAsync(uri, body, ct); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidDataException("Missing API response."); }
    private static async Task EnsureAsync(Task<HttpResponseMessage> operation) { using var response = await operation; response.EnsureSuccessStatusCode(); }
}
