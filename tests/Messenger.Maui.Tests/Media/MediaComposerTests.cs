using Messenger.Contracts.Media;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;
using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Messages;

namespace Messenger.Maui.Tests.Media;

public sealed class MediaComposerTests
{
    [Fact]
    public async Task Permission_denial_and_cancellation_do_not_create_attachments()
    {
        var capture = new FakeCapture
        {
            Next = MediaCaptureResult.PermissionDenied("Камера недоступна")
        };
        var viewModel = new MediaComposerViewModel(capture, new FakeUploader(), new FakeAvatarClient());

        await viewModel.AddPhotoAsync();
        capture.Next = MediaCaptureResult.Cancelled();
        await viewModel.AddFileAsync();

        Assert.Empty(viewModel.Attachments);
        Assert.Equal("Камера недоступна", viewModel.Status);
    }

    [Fact]
    public async Task Upload_reports_progress_and_failed_item_can_retry_without_recapture()
    {
        var selection = Selection(MediaKind.File, "report.pdf", "application/pdf");
        var capture = new FakeCapture { Next = MediaCaptureResult.Selected(selection) };
        var uploader = new FakeUploader { Failure = new HttpRequestException("offline") };
        var viewModel = new MediaComposerViewModel(capture, uploader, new FakeAvatarClient());

        await viewModel.AddFileAsync();
        Assert.Contains("report.pdf: offline", viewModel.Status);
        Assert.Equal("offline", viewModel.Attachments.Single().ErrorMessage);
        uploader.Failure = null;
        await viewModel.RetryAsync(viewModel.Attachments.Single());

        var item = Assert.Single(viewModel.Attachments);
        Assert.Equal(UploadItemState.Uploaded, item.State);
        Assert.Equal(1, item.Progress);
        Assert.Equal(2, uploader.Calls);
        Assert.Same(selection, uploader.LastSelection);
        Assert.Null(item.ErrorMessage);
        Assert.Null(viewModel.Status);
    }

    [Theory]
    [InlineData(MediaKind.Photo, "photo.jpg", "image/jpeg", null)]
    [InlineData(MediaKind.Video, "clip.mp4", "video/mp4", null)]
    [InlineData(MediaKind.Audio, "voice.m4a", "audio/mp4", 4200L)]
    public async Task Captured_media_preserves_kind_name_mime_and_audio_duration(
        MediaKind kind, string name, string mime, long? durationMs)
    {
        var selection = Selection(kind, name, mime, durationMs);
        var capture = new FakeCapture { Next = MediaCaptureResult.Selected(selection) };
        var uploader = new FakeUploader();
        var viewModel = new MediaComposerViewModel(capture, uploader, new FakeAvatarClient());

        await viewModel.AddAsync(kind);

        var item = Assert.Single(viewModel.Attachments);
        Assert.Equal(kind, item.Selection.Kind);
        Assert.Equal(name, item.Selection.FileName);
        Assert.Equal(mime, item.Selection.ContentType);
        Assert.Equal(durationMs, item.Selection.DurationMs);
    }

    [Fact]
    public async Task Uploaded_object_can_be_assigned_to_profile_or_group_avatar()
    {
        var avatars = new FakeAvatarClient();
        var viewModel = new MediaComposerViewModel(
            new FakeCapture { Next = MediaCaptureResult.Selected(Selection(MediaKind.Photo, "a.jpg", "image/jpeg")) },
            new FakeUploader(), avatars);
        await viewModel.AddPhotoAsync();
        var item = viewModel.Attachments.Single();

        await viewModel.AssignProfileAvatarAsync(item);
        await viewModel.AssignGroupAvatarAsync(Guid.NewGuid(), "Группа", item);

        Assert.Equal(1, avatars.ProfileCalls);
        Assert.Equal(1, avatars.GroupCalls);
    }

    [Theory]
    [InlineData(MediaKind.Photo, "image")]
    [InlineData(MediaKind.Video, "video")]
    public async Task Gallery_batch_keeps_order_and_uses_the_API_media_type(MediaKind firstKind, string expectedType)
    {
        var media = new MediaComposerViewModel(new FakeCapture(), new FakeUploader(), new FakeAvatarClient());
        await media.AddSelectionsAsync([Selection(firstKind, "first", firstKind == MediaKind.Video ? "video/mp4" : "image/jpeg"), Selection(MediaKind.Photo, "second.jpg", "image/jpeg")]);
        var objectIds = media.Attachments.Select(item => item.StoredObject!.Id).ToArray();
        using var handler = new RecordingSendHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var chat = new ChatViewModel(Guid.NewGuid(), new ConversationClient(http), media) { ComposerText = "Подпись" };

        await chat.SendAsync();

        Assert.Equal(expectedType, handler.Sent!.Type);
        Assert.Equal(objectIds, handler.Sent.AttachmentObjectIds);
        Assert.Equal("Подпись", handler.Sent.Body);
        Assert.Empty(media.Attachments);
        Assert.Single(chat.Messages);
        Assert.Equal(objectIds, chat.Messages.Single().Attachments!.Select(value => value.Id));
    }

    [Fact]
    public async Task Failed_uploads_are_preserved_until_retry_and_cannot_be_silently_sent_as_a_partial_batch()
    {
        var uploader = new FakeUploader { Failure = new HttpRequestException("offline") };
        var media = new MediaComposerViewModel(new FakeCapture(), uploader, new FakeAvatarClient());
        await media.AddSelectionsAsync([Selection(MediaKind.Photo, "image.jpg", "image/jpeg")]);
        using var handler = new RecordingSendHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var chat = new ChatViewModel(Guid.NewGuid(), new ConversationClient(http), media) { ComposerText = "Не потерять" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => chat.SendAsync());
        Assert.Null(handler.Sent);
        Assert.Single(media.Attachments);
        Assert.Equal("Не потерять", chat.ComposerText);

        uploader.Failure = null;
        await media.RetryAsync(media.Attachments[0]);
        await chat.SendAsync();
        Assert.NotNull(handler.Sent);
        Assert.Empty(media.Attachments);
    }

    [Fact]
    public async Task Over_limit_batch_is_rejected_before_starting_any_uploads()
    {
        var uploader = new FakeUploader();
        var media = new MediaComposerViewModel(new FakeCapture(), uploader, new FakeAvatarClient());
        await Assert.ThrowsAsync<InvalidOperationException>(() => media.AddSelectionsAsync(
            Enumerable.Range(0, 11).Select(index => Selection(MediaKind.Photo, $"{index}.jpg", "image/jpeg")).ToArray()));
        Assert.Equal(0, uploader.Calls);
        Assert.Empty(media.Attachments);
    }

    [Fact]
    public async Task A_later_success_does_not_hide_an_earlier_failed_attachment_reason()
    {
        var uploader = new FakeUploader { Failure = new HttpRequestException("Сервер отклонил формат файла.") };
        var media = new MediaComposerViewModel(new FakeCapture(), uploader, new FakeAvatarClient());
        await media.AddSelectionsAsync([Selection(MediaKind.Photo, "first.jpg", "image/jpeg")]);
        uploader.Failure = null;
        await media.AddSelectionsAsync([Selection(MediaKind.Photo, "second.jpg", "image/jpeg")]);

        Assert.Equal(UploadItemState.Failed, media.Attachments[0].State);
        Assert.Equal(UploadItemState.Uploaded, media.Attachments[1].State);
        Assert.Contains("first.jpg: Сервер отклонил формат файла.", media.Status);
        Assert.DoesNotContain("second.jpg", media.Status);
        await media.RetryAsync(media.Attachments[0]);
        Assert.Null(media.Status);
    }

    private sealed class RecordingSendHandler : HttpMessageHandler
    {
        public SendMessageRequest? Sent { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent = await request.Content!.ReadFromJsonAsync<SendMessageRequest>(ct);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new MessageResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Sent!.ClientMessageId,
                    Sent.Type, Sent.Body, false, DateTimeOffset.UtcNow, null, null))
            };
        }
    }

    private static MediaSelection Selection(MediaKind kind, string name, string mime, long? duration = null) =>
        new(kind, name, mime, 3, duration, () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));

    private sealed class FakeCapture : IMediaCaptureService
    {
        public MediaCaptureResult Next { get; set; } = MediaCaptureResult.Cancelled();
        public Task<MediaCaptureResult> AcquireAsync(MediaKind kind, CancellationToken ct = default) => Task.FromResult(Next);
    }

    private sealed class FakeUploader : IMediaUploader
    {
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public MediaSelection? LastSelection { get; private set; }
        public Task<StoredObjectResponse> UploadAsync(MediaSelection selection, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            LastSelection = selection;
            progress?.Report(.5);
            if (Failure is not null) return Task.FromException<StoredObjectResponse>(Failure);
            progress?.Report(1);
            return Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), selection.FileName, selection.ContentType,
                selection.Size, new string('a', 64), DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeAvatarClient : IAvatarClient
    {
        public int ProfileCalls { get; private set; }
        public int GroupCalls { get; private set; }
        public Task AssignProfileAsync(Guid objectId, CancellationToken ct = default) { ProfileCalls++; return Task.CompletedTask; }
        public Task AssignGroupAsync(Guid chatId, string title, Guid objectId, CancellationToken ct = default) { GroupCalls++; return Task.CompletedTask; }
    }
}
