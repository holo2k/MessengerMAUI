using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class MessageAttachmentTests
{
    [Fact]
    public void Message_JSON_preserves_image_metadata_without_needing_a_text_body()
    {
        var attachment = new StoredObjectResponse(Guid.NewGuid(), "photo.png", "image/png", 20, "sha", DateTimeOffset.UtcNow);
        var message = new MessageResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "image", "", false,
            DateTimeOffset.UtcNow, null, null, Attachments: [attachment]);

        var received = JsonSerializer.Deserialize<MessageResponse>(JsonSerializer.Serialize(message))!;

        Assert.Equal(attachment, Assert.Single(received.Attachments!));
        Assert.Equal("", received.Body);
    }

    [Fact]
    public async Task Attachment_download_authorizes_the_object_before_fetching_private_image_bytes()
    {
        var id = Guid.NewGuid();
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47];
        var authorized = false;
        using var api = new HttpClient(new Handler(request =>
        {
            Assert.Equal($"/api/objects/{id}/download", request.RequestUri!.AbsolutePath);
            authorized = true;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DownloadAuthorizationResponse("https://storage.test/private/photo?signature=value", DateTimeOffset.UtcNow.AddMinutes(5))) };
        })) { BaseAddress = new Uri("https://api.test/") };
        using var transfer = new HttpClient(new Handler(request =>
        {
            Assert.True(authorized);
            Assert.Equal("?signature=value", request.RequestUri!.Query);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        using var destination = new MemoryStream();

        await new MessageAttachmentClient(api, transfer).DownloadAsync(id, destination);

        Assert.Equal(bytes, destination.ToArray());
    }

    [Fact]
    public async Task Denied_download_does_not_fetch_storage_or_leave_an_empty_successful_image()
    {
        using var api = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound))) { BaseAddress = new Uri("https://api.test/") };
        using var transfer = new HttpClient(new Handler(_ => throw new Exception("Must not download an unauthorized object")));
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<HttpRequestException>(() => new MessageAttachmentClient(api, transfer).DownloadAsync(Guid.NewGuid(), destination));
        Assert.Equal(0, destination.Length);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
}
