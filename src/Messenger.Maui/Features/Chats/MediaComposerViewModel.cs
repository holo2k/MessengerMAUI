using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Media;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public enum UploadItemState { Uploading, Uploaded, Failed }

public sealed class ComposerAttachment(MediaSelection selection) : ObservableObject
{
    private double _progress;
    private UploadItemState _state = UploadItemState.Uploading;
    private StoredObjectResponse? _storedObject;
    public MediaSelection Selection { get; } = selection;
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public UploadItemState State { get => _state; set => SetProperty(ref _state, value); }
    public StoredObjectResponse? StoredObject { get => _storedObject; set => SetProperty(ref _storedObject, value); }
}

public sealed class MediaComposerViewModel(
    IMediaCaptureService capture,
    IMediaUploader uploader,
    IAvatarClient avatars) : ObservableObject
{
    private string? _status;
    public ObservableCollection<ComposerAttachment> Attachments { get; } = [];
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }

    public Task AddFileAsync(CancellationToken ct = default) => AddAsync(MediaKind.File, ct);
    public Task AddPhotoAsync(CancellationToken ct = default) => AddAsync(MediaKind.Photo, ct);
    public Task AddVideoAsync(CancellationToken ct = default) => AddAsync(MediaKind.Video, ct);
    public Task AddAudioAsync(CancellationToken ct = default) => AddAsync(MediaKind.Audio, ct);

    public async Task AddAsync(MediaKind kind, CancellationToken ct = default)
    {
        var result = await capture.AcquireAsync(kind, ct);
        if (result.Status == MediaCaptureStatus.PermissionDenied)
        {
            Status = result.Message;
            return;
        }
        if (result.Selection is null) return;
        var item = new ComposerAttachment(result.Selection);
        Attachments.Add(item);
        await UploadAsync(item, ct);
    }

    public Task RetryAsync(ComposerAttachment item, CancellationToken ct = default) => UploadAsync(item, ct);

    public async Task AssignProfileAvatarAsync(ComposerAttachment item, CancellationToken ct = default) =>
        await avatars.AssignProfileAsync(RequireUploaded(item).Id, ct);

    public async Task AssignGroupAvatarAsync(Guid chatId, string title, ComposerAttachment item, CancellationToken ct = default) =>
        await avatars.AssignGroupAsync(chatId, title, RequireUploaded(item).Id, ct);

    private async Task UploadAsync(ComposerAttachment item, CancellationToken ct)
    {
        item.State = UploadItemState.Uploading;
        Status = null;
        try
        {
            item.StoredObject = await uploader.UploadAsync(item.Selection, new InlineProgress(value => item.Progress = value), ct);
            item.Progress = 1;
            item.State = UploadItemState.Uploaded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            item.State = UploadItemState.Failed;
            Status = "Загрузка отменена";
        }
        catch (Exception exception)
        {
            item.State = UploadItemState.Failed;
            Status = exception.Message;
        }
    }

    private static StoredObjectResponse RequireUploaded(ComposerAttachment item) =>
        item.StoredObject ?? throw new InvalidOperationException("Файл ещё не загружен.");

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
