using System.Text.Json;

namespace Messenger.Maui.Services;

public sealed class SecureSessionStore : ISecureSessionStore
{
    private const string SessionKey = "messenger.auth.session.v1";

    public async Task<ClientSession?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await SecureStorage.Default.GetAsync(SessionKey);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ClientSession>(json);
    }

    public Task SaveAsync(ClientSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(SessionKey);
        return Task.CompletedTask;
    }
}
