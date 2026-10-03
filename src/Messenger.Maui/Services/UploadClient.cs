using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Media;
using Messenger.Contracts.Users;

namespace Messenger.Maui.Services;

public interface IMediaUploader
{
    Task<StoredObjectResponse> UploadAsync(MediaSelection selection, IProgress<double>? progress = null, CancellationToken ct = default);
}

public interface IAvatarClient
{
    Task AssignProfileAsync(Guid objectId, CancellationToken ct = default);
    Task AssignGroupAsync(Guid chatId, string title, Guid objectId, CancellationToken ct = default);
}

public sealed class UploadClient(HttpClient httpClient, HttpClient transferClient) : IMediaUploader, IAvatarClient
{
    public async Task<StoredObjectResponse> UploadAsync(
        MediaSelection selection, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await using var source = await selection.OpenReadAsync();
        await using var buffered = new MemoryStream();
        await source.CopyToAsync(buffered, ct);
        var bytes = buffered.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var createResponse = await httpClient.PostAsJsonAsync("api/uploads/", new CreateUploadRequest(
            selection.FileName, selection.ContentType, bytes.LongLength, checksum), ct);
        createResponse.EnsureSuccessStatusCode();
        var session = await createResponse.Content.ReadFromJsonAsync<UploadSessionResponse>(ct)
            ?? throw new InvalidDataException("Missing upload session.");

        var signedUri = new Uri(session.UploadUrl);
        var transferUri = DeviceUri(signedUri);
        using var put = new HttpRequestMessage(HttpMethod.Put, transferUri)
        {
            Content = new ProgressByteArrayContent(bytes, selection.ContentType, progress)
        };
        if (transferUri != signedUri)
        {
            put.Headers.Host = signedUri.Authority;
        }
        using var uploadResponse = await transferClient.SendAsync(put, HttpCompletionOption.ResponseHeadersRead, ct);
        uploadResponse.EnsureSuccessStatusCode();
        using var completeResponse = await httpClient.PostAsJsonAsync($"api/uploads/{session.Id}/complete", new { }, ct);
        completeResponse.EnsureSuccessStatusCode();
        return await completeResponse.Content.ReadFromJsonAsync<StoredObjectResponse>(ct)
            ?? throw new InvalidDataException("Missing stored object response.");
    }

    public async Task AssignProfileAsync(Guid objectId, CancellationToken ct = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/me/avatar", new AvatarRequest(objectId.ToString()), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task AssignGroupAsync(Guid chatId, string title, Guid objectId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/chats/{chatId}")
        {
            Content = JsonContent.Create(new UpdateGroupChatRequest(title, objectId.ToString()))
        };
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Uri DeviceUri(Uri signedUri)
    {
#if ANDROID
        if (signedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || signedUri.Host == "127.0.0.1")
        {
            return new UriBuilder(signedUri) { Host = "10.0.2.2" }.Uri;
        }
#endif
        return signedUri;
    }

    private sealed class ProgressByteArrayContent(byte[] bytes, string contentType, IProgress<double>? progress) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = bytes.LongLength; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            const int block = 64 * 1024;
            var written = 0;
            while (written < bytes.Length)
            {
                var count = Math.Min(block, bytes.Length - written);
                await stream.WriteAsync(bytes.AsMemory(written, count));
                written += count;
                progress?.Report((double)written / bytes.Length);
            }
        }
    }
}
