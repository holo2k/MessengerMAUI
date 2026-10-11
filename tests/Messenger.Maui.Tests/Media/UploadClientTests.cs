using System.Net;
using System.Net.Http.Json;
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

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
