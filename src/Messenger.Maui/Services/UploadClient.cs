using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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
        if (bytes.Length == 0) throw new InvalidDataException("Выбранный файл пуст.");
        var (fileName, contentType) = UploadMetadata(selection, bytes);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var createResponse = await httpClient.PostAsJsonAsync("api/uploads/", new CreateUploadRequest(
            fileName, contentType, bytes.LongLength, checksum), ct);
        await EnsureUploadSuccessAsync(createResponse, "Не удалось начать загрузку", ct);
        var session = await createResponse.Content.ReadFromJsonAsync<UploadSessionResponse>(ct)
            ?? throw new InvalidDataException("Missing upload session.");

        var signedUri = new Uri(session.UploadUrl);
        var transferUri = DeviceUri(signedUri);
        using var put = new HttpRequestMessage(HttpMethod.Put, transferUri)
        {
            Content = new ProgressByteArrayContent(bytes, contentType, progress)
        };
        if (transferUri != signedUri)
        {
            put.Headers.Host = signedUri.Authority;
        }
        using var uploadResponse = await transferClient.SendAsync(put, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureUploadSuccessAsync(uploadResponse, "Не удалось передать файл в хранилище", ct);
        using var completeResponse = await httpClient.PostAsJsonAsync($"api/uploads/{session.Id}/complete", new { }, ct);
        await EnsureUploadSuccessAsync(completeResponse, "Не удалось завершить загрузку", ct);
        return await completeResponse.Content.ReadFromJsonAsync<StoredObjectResponse>(ct)
            ?? throw new InvalidDataException("Missing stored object response.");
    }

    private static (string FileName, string ContentType) UploadMetadata(MediaSelection selection, ReadOnlySpan<byte> bytes)
    {
        if (selection.Kind != MediaKind.Photo) return (selection.FileName, selection.ContentType);
        if (bytes.Length > 25 * 1024 * 1024) throw new InvalidDataException("Фото не должно превышать 25 МБ.");

        // Gallery providers can report a stale MIME type or omit/mislabel the filename extension.
        var format = bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? (".jpg", "image/jpeg")
            : bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? (".png", "image/png")
            : bytes.StartsWith("GIF8"u8) ? (".gif", "image/gif")
            : throw new InvalidDataException("Не удалось прочитать формат фотографии. Выберите фото JPEG, PNG или GIF.");
        var name = Path.GetFileNameWithoutExtension(selection.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) name = "photo";
        return (name + format.Item1, format.Item2);
    }

    private static async Task EnsureUploadSuccessAsync(HttpResponseMessage response, string stage, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String)
                    code = value.GetString();
            }
            catch (JsonException) { }
        }
        var reason = code switch
        {
            "file_type_mismatch" => "Формат файла не совпадает с его содержимым или не поддерживается сервером.",
            "file_too_large" => "Размер файла превышает допустимый. Фото — до 25 МБ, видео — до 100 МБ.",
            "checksum_mismatch" => "Файл передан не полностью. Повторите загрузку.",
            "scan_rejected" => "Сервер отклонил файл при проверке.",
            "expired" => "Время загрузки истекло. Повторите загрузку.",
            "invalid_declaration" => "Сервер не принял данные файла.",
            _ when response.StatusCode == HttpStatusCode.Unauthorized => "Сессия истекла. Войдите в приложение снова.",
            _ when response.StatusCode == HttpStatusCode.RequestEntityTooLarge => "Сервер отклонил файл из-за его размера.",
            _ => $"Сервер вернул HTTP {(int)response.StatusCode}. Повторите загрузку."
        };
        throw new HttpRequestException($"{stage}. {reason}", null, response.StatusCode);
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

    internal static Uri DeviceUri(Uri signedUri)
    {
#if ANDROID
        if (signedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || signedUri.Host == "127.0.0.1")
        {
            return new UriBuilder(signedUri) { Host = "10.0.2.2" }.Uri;
        }
#endif
        return signedUri;
    }

    private sealed class ProgressByteArrayContent : HttpContent
    {
        private readonly byte[] bytes;
        private readonly IProgress<double>? progress;

        public ProgressByteArrayContent(byte[] bytes, string contentType, IProgress<double>? progress)
        {
            this.bytes = bytes;
            this.progress = progress;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        protected override bool TryComputeLength(out long length) { length = bytes.LongLength; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
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
