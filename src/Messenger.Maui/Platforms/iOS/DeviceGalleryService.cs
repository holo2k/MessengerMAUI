using AVFoundation;
using CoreGraphics;
using Foundation;
using Messenger.Maui.Services;
using Photos;

namespace Messenger.Maui.Platforms.iOS;

public sealed class DeviceGalleryService : IGalleryService
{
    public async Task<GalleryAccess> RequestAccessAsync(bool reselect = false, CancellationToken ct = default)
    {
        var status = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.ReadWrite);
        if (status == PHAuthorizationStatus.NotDetermined)
            status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.ReadWrite).WaitAsync(ct);
        else if (reselect && status == PHAuthorizationStatus.Limited)
            AppInfo.Current.ShowSettingsUI();
        ct.ThrowIfCancellationRequested();
        return status switch
        {
            PHAuthorizationStatus.Authorized => GalleryAccess.Granted,
            PHAuthorizationStatus.Limited => GalleryAccess.Limited,
            _ => GalleryAccess.Denied
        };
    }

    public Task<GalleryPage> GetPageAsync(string? cursor, int pageSize, CancellationToken ct = default) => Task.Run(() =>
    {
        var offset = cursor is null ? 0 : int.Parse(cursor);
        using var options = new PHFetchOptions
        {
            Predicate = NSPredicate.FromFormat("mediaType == 1 OR mediaType == 2"),
            SortDescriptors = [new NSSortDescriptor("creationDate", false)],
            FetchLimit = (nuint)(offset + pageSize + 1)
        };
        using var assets = PHAsset.FetchAssets(options);
        var items = new List<GalleryMediaItem>();
        for (var index = offset; index < assets.Count && items.Count < pageSize; index++)
        {
            ct.ThrowIfCancellationRequested();
            using var asset = (PHAsset)assets.ObjectAt(index);
            var resource = PHAssetResource.GetAssetResources(asset).FirstOrDefault();
            var video = asset.MediaType == PHAssetMediaType.Video;
            items.Add(new(asset.LocalIdentifier, Path.ChangeExtension(resource?.OriginalFilename ?? "media", video ? ".mp4" : ".jpg"),
                video ? MediaKind.Video : MediaKind.Photo, video ? "video/mp4" : "image/jpeg", 0,
                video ? (long)(asset.Duration * 1000) : null));
        }
        return new GalleryPage(items, assets.Count > offset + pageSize ? (offset + pageSize).ToString() : null);
    }, ct);

    public Task<byte[]?> GetThumbnailAsync(GalleryMediaItem item, int size, CancellationToken ct = default) =>
        GetImageAsync(item.Id, size, PHImageContentMode.AspectFill, ct);

    private static async Task<byte[]?> GetImageAsync(string id, int size, PHImageContentMode mode, CancellationToken ct)
    {
        using var assets = PHAsset.FetchAssetsUsingLocalIdentifiers([id], null);
        if (assets.FirstObject is not PHAsset asset) throw new FileNotFoundException("Фото больше недоступно в галерее.");
        using var options = new PHImageRequestOptions
        {
            DeliveryMode = PHImageRequestOptionsDeliveryMode.HighQualityFormat,
            ResizeMode = PHImageRequestOptionsResizeMode.Exact,
            NetworkAccessAllowed = true
        };
        var completed = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = PHImageManager.DefaultManager;
        var request = manager.RequestImageForAsset(asset, new CGSize(size, size), mode, options, (image, info) =>
        {
            try
            {
                using var data = image?.AsJPEG(0.9f);
                completed.TrySetResult(data?.ToArray());
            }
            catch (Exception exception) { completed.TrySetException(exception); }
        });
        using var cancellation = ct.Register(() => { manager.CancelImageRequest(request); completed.TrySetCanceled(ct); });
        return await completed.Task;
    }

    public async Task<MediaSelection> CreateSelectionAsync(GalleryMediaItem item, CancellationToken ct = default)
    {
        if (item.Kind == MediaKind.Photo)
        {
            var bytes = await GetImageAsync(item.Id, 4096, PHImageContentMode.AspectFit, ct)
                ?? throw new InvalidDataException("Не удалось прочитать фотографию.");
            if (bytes.Length > 25 * 1024 * 1024) throw new InvalidDataException("Фото не должно превышать 25 МБ.");
            return new(MediaKind.Photo, item.FileName, "image/jpeg", bytes.LongLength, null,
                () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        }

        using var assets = PHAsset.FetchAssetsUsingLocalIdentifiers([item.Id], null);
        if (assets.FirstObject is not PHAsset asset) throw new FileNotFoundException("Видео больше недоступно в галерее.");
        using var options = new PHVideoRequestOptions { NetworkAccessAllowed = true };
        var ready = new TaskCompletionSource<AVAssetExportSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = PHImageManager.DefaultManager;
        var request = manager.RequestExportSession(asset, options, "AVAssetExportPreset1280x720", (session, info) =>
        {
            if (session is null) ready.TrySetException(new InvalidDataException("Не удалось подготовить видео."));
            else if (!ready.TrySetResult(session)) session.Dispose();
        });
        using var requestCancellation = ct.Register(() => { manager.CancelImageRequest(request); ready.TrySetCanceled(ct); });
        using var export = await ready.Task;
        var folder = Path.Combine(FileSystem.CacheDirectory, "gallery-exports");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder).Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)))
            File.Delete(old);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.mp4");
        using var url = NSUrl.FromFilename(path);
        export.OutputUrl = url;
        export.OutputFileType = "public.mpeg-4";
        export.ShouldOptimizeForNetworkUse = true;
        using var cancellation = ct.Register(export.CancelExport);
        try
        {
            await export.ExportTaskAsync();
            ct.ThrowIfCancellationRequested();
            if (export.Status != AVAssetExportSessionStatus.Completed)
                throw new InvalidDataException(export.Error?.LocalizedDescription ?? "Не удалось подготовить видео.");
            var size = new FileInfo(path).Length;
            if (size > 100 * 1024 * 1024) throw new InvalidDataException("Видео не должно превышать 100 МБ.");
            return new(MediaKind.Video, item.FileName, "video/mp4", size, item.DurationMs,
                () => Task.FromResult<Stream>(File.OpenRead(path)));
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}
