using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Music;

public sealed class MusicApiClientTests
{
    [Fact]
    public async Task Sharing_sends_a_persistent_track_reference_and_the_retry_identifier()
    {
        var trackId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        using var http = new HttpClient(new RequestHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/chats/{chatId}/messages", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<SendMessageRequest>();
            Assert.NotNull(body);
            Assert.Equal("music", body.Type);
            Assert.Equal(trackId, body.MusicTrackId);
            Assert.Equal(clientId, body.ClientMessageId);
            Assert.Null(body.AttachmentObjectIds);
            return JsonContent.Create(new MessageResponse(Guid.NewGuid(), chatId, Guid.NewGuid(), 1, clientId,
                "music", "Песня — Автор", false, DateTimeOffset.UtcNow, null, null,
                new(trackId, "Песня", "Автор", 1000, Guid.NewGuid(), "available")));
        })) { BaseAddress = new Uri("https://example.test/") };

        var sent = await new MusicApiClient(http).ShareAsync(trackId, chatId, clientId, CancellationToken.None);
        Assert.Equal(trackId, sent.MusicTrack!.Id);
        Assert.NotNull(sent.MusicTrack.CoverObjectId);
    }

    [Fact]
    public async Task Cover_update_uses_the_uploaded_object_and_returns_updated_track_metadata()
    {
        var trackId = Guid.NewGuid();
        var coverId = Guid.NewGuid();
        using var http = new HttpClient(new RequestHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal($"/api/music/tracks/{trackId}/cover", request.RequestUri!.AbsolutePath);
            Assert.Equal(coverId, (await request.Content!.ReadFromJsonAsync<UpdateTrackCoverRequest>())!.CoverObjectId);
            return JsonContent.Create(new MusicTrackResponse(trackId, "Песня", "Автор", 1000, coverId, "available"));
        })) { BaseAddress = new Uri("https://example.test/") };
        Assert.Equal(coverId, (await new MusicApiClient(http).UpdateCoverAsync(trackId, coverId, CancellationToken.None)).CoverObjectId);
    }

    private sealed class RequestHandler(Func<HttpRequestMessage, Task<HttpContent>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK) { Content = await response(request) };
    }
}
