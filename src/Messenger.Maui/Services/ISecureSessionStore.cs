using Messenger.Contracts.Auth;

namespace Messenger.Maui.Services;

public sealed record ClientSession(AuthSessionResponse Session, string DeviceLabel);

public interface ISecureSessionStore
{
    Task<ClientSession?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ClientSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
