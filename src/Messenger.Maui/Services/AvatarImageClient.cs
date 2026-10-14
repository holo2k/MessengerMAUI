using System.Collections.Concurrent;
using System.Net.Http.Json;
using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public interface IAvatarImageClient
{
    Task<byte[]?> GetAsync(string? objectId, CancellationToken cancellationToken = default);
}

public sealed class AvatarImageClient(
    HttpClient httpClient,
    HttpClient transferClient) : IAvatarImageClient
{
    private readonly ConcurrentDictionary<Guid, byte[]> _cache = new();

    public async Task<byte[]?> GetAsync(string? objectId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(objectId, out var id))
        {
            return null;
        }
        if (_cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var authorization = await httpClient.GetFromJsonAsync<DownloadAuthorizationResponse>(
            $"api/objects/{id}/download", cancellationToken);
        if (authorization is null)
        {
            return null;
        }

        var bytes = await transferClient.GetByteArrayAsync(
            UploadClient.DeviceUri(new Uri(authorization.Url)), cancellationToken);
        _cache.TryAdd(id, bytes);
        return bytes;
    }
}
