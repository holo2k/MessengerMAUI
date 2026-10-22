using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class MessageForwardingTests
{
    [Fact]
    public async Task Older_server_can_forward_photos_and_retries_reuse_uploaded_objects_and_client_id()
    {
        using var fixture = new Fixture();
        fixture.FailFirstSend = true;
        var clientId = Guid.NewGuid();
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, clientId));
        var sent = await fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, clientId);

        Assert.Equal(1, fixture.Uploader.Calls);
        Assert.Equal(1, fixture.Downloads.Calls);
        Assert.Equal("caption", fixture.Sends[0].Body);
        Assert.All(fixture.Sends, request => Assert.Equal(clientId, request.ClientMessageId));
        Assert.Equal(fixture.Sends[0].AttachmentObjectIds, fixture.Sends[1].AttachmentObjectIds);
        Assert.NotEqual(fixture.Source.Attachments![0].Id, Assert.Single(fixture.Sends[1].AttachmentObjectIds!));
        Assert.Equal(fixture.Target, sent.ChatId);
        Assert.True(sent.IsForwarded);
        Assert.Equal(fixture.Uploader.Object.Id, Assert.Single(sent.Attachments!).Id);
    }

    [Fact]
    public async Task Fallback_does_not_copy_a_deleted_original_or_send_to_an_inaccessible_chat()
    {
        using var fixture = new Fixture();
        fixture.SourceVisible = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, Guid.NewGuid()));
        fixture.SourceVisible = true;
        fixture.TargetVisible = false;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, Guid.NewGuid()));
        Assert.Empty(fixture.Sends);
        Assert.Equal(0, fixture.Uploader.Calls);
        Assert.Equal(0, fixture.Downloads.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_404_failures_do_not_trigger_a_second_send(HttpStatusCode status)
    {
        using var fixture = new Fixture { ForwardStatus = status };
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, Guid.NewGuid()));
        Assert.Empty(fixture.Sends);
        Assert.Equal(0, fixture.Uploader.Calls);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("music")]
    public async Task Fallback_preserves_text_or_music_without_downloading_media(string type)
    {
        using var fixture = new Fixture();
        var track = new MusicTrackResponse(Guid.NewGuid(), "Title", "Artist", 5000, Guid.NewGuid(), "available", Guid.NewGuid());
        fixture.Source = fixture.Source with { Type = type, Attachments = [], MusicTrack = type == "music" ? track : null };
        await fixture.Forwarder.ForwardAsync(fixture.Source, fixture.Target, Guid.NewGuid());
        var request = Assert.Single(fixture.Sends);
        Assert.Equal(type, request.Type);
        if (type == "music") Assert.Equal(track.Id, request.MusicTrackId);
        else Assert.Equal("caption", request.Body);
        Assert.Equal(0, fixture.Downloads.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public Guid Target { get; } = Guid.NewGuid();
        public MessageResponse Source { get; set; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "image", "caption", false,
            DateTimeOffset.UtcNow, null, null, Attachments: [new(Guid.NewGuid(), "photo.jpg", "image/jpeg", 3, "sha", DateTimeOffset.UtcNow)]);
        public bool SourceVisible { get; set; } = true;
        public bool TargetVisible { get; set; } = true;
        public bool FailFirstSend { get; set; }
        public HttpStatusCode ForwardStatus { get; init; } = HttpStatusCode.NotFound;
        public List<SendMessageRequest> Sends { get; } = [];
        public Uploader Uploader { get; } = new();
        public Downloads Downloads { get; } = new();
        public MessageForwarder Forwarder { get; }

        public Fixture()
        {
            _http = new HttpClient(new Handler(async request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/forward")) return new(ForwardStatus);
                if (request.Method == HttpMethod.Get && path == $"/api/chats/{Target}")
                    return TargetVisible ? Ok(new ChatResponse(Target, "direct", null, null, "member", false, false, false, 0, DateTimeOffset.UtcNow)) : new(HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Get && path == $"/api/chats/{Source.ChatId}/messages")
                    return Ok(new MessagePageResponse(SourceVisible ? [Source] : [], null));
                if (request.Method == HttpMethod.Post && path == $"/api/chats/{Target}/messages")
                {
                    var body = (await request.Content!.ReadFromJsonAsync<SendMessageRequest>())!;
                    Sends.Add(body);
                    if (FailFirstSend && Sends.Count == 1) return new(HttpStatusCode.ServiceUnavailable);
                    return Ok(Source with { Id = Guid.NewGuid(), ChatId = Target, ClientMessageId = body.ClientMessageId, Attachments = [] });
                }
                throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
            })) { BaseAddress = new Uri("https://api.test/") };
            Forwarder = new MessageForwarder(new ConversationClient(_http), Uploader, Downloads, new MusicApiClient(_http));
        }

        private static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        public void Dispose() => _http.Dispose();
    }

    private sealed class Uploader : IMediaUploader
    {
        public int Calls { get; private set; }
        public StoredObjectResponse Object { get; } = new(Guid.NewGuid(), "photo.jpg", "image/jpeg", 3, "sha", DateTimeOffset.UtcNow);
        public async Task<StoredObjectResponse> UploadAsync(MediaSelection selection, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            await using var stream = await selection.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray());
            return Object;
        }
    }
    private sealed class Downloads : IMessageAttachmentClient
    {
        public int Calls { get; private set; }
        public async Task DownloadAsync(Guid objectId, Stream destination, CancellationToken ct = default)
        { Calls++; await destination.WriteAsync(new byte[] { 1, 2, 3 }, ct); }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
