namespace Messenger.Maui.Services;

public enum GalleryAccess { Denied, Limited, Granted }

public sealed record GalleryMediaItem(string Id, string FileName, MediaKind Kind, string ContentType, long Size, long? DurationMs);
public sealed record GalleryPage(IReadOnlyList<GalleryMediaItem> Items, string? NextCursor);

public interface IGalleryService
{
    Task<GalleryAccess> RequestAccessAsync(bool reselect = false, CancellationToken ct = default);
    Task<GalleryPage> GetPageAsync(string? cursor, int pageSize, CancellationToken ct = default);
    Task<byte[]?> GetThumbnailAsync(GalleryMediaItem item, int size, CancellationToken ct = default);
    Task<MediaSelection> CreateSelectionAsync(GalleryMediaItem item, CancellationToken ct = default);
}
