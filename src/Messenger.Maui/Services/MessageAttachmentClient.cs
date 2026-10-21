using System.Net.Http.Json;
using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public interface IMessageAttachmentClient
{
    Task DownloadAsync(Guid objectId, Stream destination, CancellationToken ct = default);
}

public sealed class MessageAttachmentClient(HttpClient api, HttpClient transfer) : IMessageAttachmentClient
{
    public async Task DownloadAsync(Guid objectId, Stream destination, CancellationToken ct = default)
    {
        var authorization = await api.GetFromJsonAsync<DownloadAuthorizationResponse>($"api/objects/{objectId}/download", ct)
            ?? throw new InvalidDataException("Не удалось получить ссылку на вложение.");
        var signedUri = new Uri(authorization.Url);
        var deviceUri = UploadClient.DeviceUri(signedUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, deviceUri);
        if (deviceUri != signedUri) request.Headers.Host = signedUri.Authority;
        using var response = await transfer.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(destination, ct);
    }
}
