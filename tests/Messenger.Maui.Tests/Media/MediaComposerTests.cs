using Messenger.Contracts.Media;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;

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
        uploader.Failure = null;
        await viewModel.RetryAsync(viewModel.Attachments.Single());

        var item = Assert.Single(viewModel.Attachments);
        Assert.Equal(UploadItemState.Uploaded, item.State);
        Assert.Equal(1, item.Progress);
        Assert.Equal(2, uploader.Calls);
        Assert.Same(selection, uploader.LastSelection);
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
