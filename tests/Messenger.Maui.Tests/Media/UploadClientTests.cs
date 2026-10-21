using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messenger.Contracts.Media;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class UploadClientTests
{
    [Theory]
    [InlineData("track.mp3", "audio/mpeg")]
    [InlineData("track.m4a", "audio/mp4")]
    [InlineData("track.wav", "audio/wav")]
    [InlineData("track.ogg", "audio/ogg")]
    public async Task Audio_upload_sends_content_type_before_serializing_body(string name, string mime)
    {
        byte[] bytes = [1, 2, 3, 4];
        var stored = new StoredObjectResponse(Guid.NewGuid(), name, mime, bytes.Length, "checksum", DateTimeOffset.UtcNow);
        using var api = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/complete"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(stored) };
            var declaration = await request.Content!.ReadFromJsonAsync<CreateUploadRequest>();
            Assert.Equal(mime, declaration!.ContentType);
            Assert.Equal(bytes.Length, declaration.Size);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UploadSessionResponse(Guid.NewGuid(), "track", "https://storage.test/track", DateTimeOffset.UtcNow.AddHours(1)))
            };
        })) { BaseAddress = new Uri("https://api.test/") };
        var transfers = 0;
        using var transfer = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(mime, request.Content!.Headers.ContentType?.MediaType);
            Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync());
            transfers++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var selection = new MediaSelection(MediaKind.Audio, name, mime, bytes.Length, 1000,
            () => Task.FromResult<Stream>(new MemoryStream(bytes)));

        var result = await new UploadClient(api, transfer).UploadAsync(selection);

        Assert.Equal(stored.Id, result.Id);
        Assert.Equal(1, transfers);
    }

    [Theory]
    [InlineData("screenshot.jpg", "image/jpeg", "89504E470D0A1A0A010203", "screenshot.png", "image/png")]
    [InlineData("photo", "application/octet-stream", "FFD8FFE0010203", "photo.jpg", "image/jpeg")]
    [InlineData("photo.heic", "image/heic", "FFD8FFE0010203", "photo.jpg", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpg", "FFD8FFE0010203", "photo.jpg", "image/jpeg")]
    [InlineData("animation.png", "image/png", "474946383961010203", "animation.gif", "image/gif")]
    public async Task Photo_upload_declares_the_actual_format_and_sends_the_same_bytes_and_MIME_to_storage(
        string originalName, string reportedMime, string hex, string expectedName, string expectedMime)
    {
        var bytes = Convert.FromHexString(hex);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var stored = new StoredObjectResponse(Guid.NewGuid(), expectedName, expectedMime, bytes.Length, checksum, DateTimeOffset.UtcNow);
        var transferred = false;
        using var api = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/complete"))
            {
                Assert.True(transferred);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(stored) };
            }
            var declaration = await request.Content!.ReadFromJsonAsync<CreateUploadRequest>();
            Assert.Equal(expectedName, declaration!.FileName);
            Assert.Equal(expectedMime, declaration.ContentType);
            Assert.Equal(bytes.Length, declaration.Size);
            Assert.Equal(checksum, declaration.Sha256);
            return SessionResponse();
        })) { BaseAddress = new Uri("https://api.test/") };
        using var transfer = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(expectedMime, request.Content!.Headers.ContentType?.MediaType);
            Assert.Equal(bytes.Length, request.Content.Headers.ContentLength);
            Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync());
            transferred = true;
            return new(HttpStatusCode.OK);
        }));
        var selection = new MediaSelection(MediaKind.Photo, originalName, reportedMime, 0, null,
            () => Task.FromResult<Stream>(new MemoryStream(bytes)));

        var result = await new UploadClient(api, transfer).UploadAsync(selection);

        Assert.Equal(stored, result);
    }

    [Theory]
    [InlineData("", "пуст")]
    [InlineData("01020304", "формат")]
    public async Task Empty_or_unrecognized_photos_fail_before_creating_a_server_upload(string hex, string reason)
    {
        using var api = new HttpClient(new Handler(_ => throw new Exception("Upload should not start")));
        using var transfer = new HttpClient(new Handler(_ => throw new Exception("Transfer should not start")));
        var bytes = Convert.FromHexString(hex);
        var selection = new MediaSelection(MediaKind.Photo, "image.jpg", "image/jpeg", bytes.Length, null,
            () => Task.FromResult<Stream>(new MemoryStream(bytes)));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new UploadClient(api, transfer).UploadAsync(selection));

        Assert.Contains(reason, error.Message);
    }

    [Theory]
    [InlineData("file_type_mismatch", "Формат файла")]
    [InlineData("file_too_large", "Размер файла")]
    [InlineData("scan_rejected", "при проверке")]
    [InlineData("checksum_mismatch", "не полностью")]
    public async Task Completion_rejection_explains_the_server_reason(string code, string reason)
    {
        using var api = new HttpClient(new Handler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/complete")
                ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = JsonContent.Create(new { code }) }
                : SessionResponse()))) { BaseAddress = new Uri("https://api.test/") };
        using var transfer = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new UploadClient(api, transfer).UploadAsync(Photo()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Contains("завершить загрузку", error.Message);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task Storage_rejection_does_not_complete_the_upload_or_expose_the_signed_URL()
    {
        using var api = new HttpClient(new Handler(request =>
        {
            Assert.DoesNotContain("/complete", request.RequestUri!.AbsolutePath);
            return Task.FromResult(SessionResponse());
        })) { BaseAddress = new Uri("https://api.test/") };
        using var transfer = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<Error><Code>SignatureDoesNotMatch</Code><secret>credentials</secret></Error>")
        })));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new UploadClient(api, transfer).UploadAsync(Photo()));

        Assert.Contains("хранилище", error.Message);
        Assert.Contains("403", error.Message);
        Assert.DoesNotContain("credentials", error.Message);
        Assert.DoesNotContain("https://", error.Message);
    }

    private static MediaSelection Photo() => new(MediaKind.Photo, "photo.jpg", "image/jpeg", 4, null,
        () => Task.FromResult<Stream>(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0])));

    private static HttpResponseMessage SessionResponse() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new UploadSessionResponse(Guid.NewGuid(), "photo", "https://storage.test/photo?signature=secret", DateTimeOffset.UtcNow.AddHours(1)))
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
