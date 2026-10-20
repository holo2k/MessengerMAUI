using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using Messenger.Maui.Services;
using Uri = Android.Net.Uri;
using Path = System.IO.Path;
using Stream = System.IO.Stream;

namespace Messenger.Maui.Platforms.Android;

public sealed class DeviceGalleryService : IGalleryService
{
    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    public async Task<GalleryAccess> RequestAccessAsync(bool reselect = false, CancellationToken ct = default)
    {
        if (reselect || GetAccess() == GalleryAccess.Denied)
            await Permissions.RequestAsync<GalleryReadPermission>();
        ct.ThrowIfCancellationRequested();
        return GetAccess();
    }

    private static GalleryAccess GetAccess()
    {
        static bool Granted(string permission) => global::Android.App.Application.Context.CheckSelfPermission(permission) == Permission.Granted;
        if (!OperatingSystem.IsAndroidVersionAtLeast(23)) return GalleryAccess.Granted;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return Granted("android.permission.READ_EXTERNAL_STORAGE") ? GalleryAccess.Granted : GalleryAccess.Denied;
        if (Granted("android.permission.READ_MEDIA_IMAGES") && Granted("android.permission.READ_MEDIA_VIDEO")) return GalleryAccess.Granted;
        if (Granted("android.permission.READ_MEDIA_IMAGES") || Granted("android.permission.READ_MEDIA_VIDEO") ||
            OperatingSystem.IsAndroidVersionAtLeast(34) && Granted("android.permission.READ_MEDIA_VISUAL_USER_SELECTED")) return GalleryAccess.Limited;
        return GalleryAccess.Denied;
    }

    public Task<GalleryPage> GetPageAsync(string? cursor, int pageSize, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var selection = "(media_type = 1 OR media_type = 3) AND (mime_type LIKE 'image/%' OR mime_type = 'video/mp4')";
        var arguments = new List<string>();
        if (cursor is not null)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out _) || !long.TryParse(parts[1], out _))
                throw new InvalidDataException("Invalid gallery cursor.");
            selection += " AND (date_added < ? OR (date_added = ? AND _id < ?))";
            arguments.AddRange([parts[0], parts[0], parts[1]]);
        }
        string[] columns = ["_id", "_display_name", "mime_type", "_size", "date_added", "duration", "media_type"];
        using var signal = new CancellationSignal();
        using var registration = ct.Register(signal.Cancel);
        using var query = new Bundle();
        query.PutString("android:query-arg-sql-selection", selection);
        query.PutStringArray("android:query-arg-sql-selection-args", arguments.ToArray());
        query.PutStringArray("android:query-arg-sort-columns", ["date_added", "_id"]);
        query.PutInt("android:query-arg-sort-direction", 1);
        query.PutInt("android:query-arg-limit", pageSize + 1);
        var source = MediaStore.Files.GetContentUri("external")!;
        using var rows = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? Resolver.Query(source, columns, query, signal)
            : Resolver.Query(source, columns, selection, arguments.ToArray(), "date_added DESC, _id DESC");
        var items = new List<GalleryMediaItem>();
        string? next = null;
        while (rows is not null && rows.MoveToNext())
        {
            ct.ThrowIfCancellationRequested();
            if (items.Count == pageSize) return new GalleryPage(items, next);
            var id = rows.GetLong(0);
            var kind = rows.GetInt(6) == 3 ? MediaKind.Video : MediaKind.Photo;
            var uri = ContentUris.WithAppendedId(kind == MediaKind.Video ? MediaStore.Video.Media.ExternalContentUri! : MediaStore.Images.Media.ExternalContentUri!, id);
            items.Add(new(uri!.ToString()!, rows.GetString(1) ?? $"media-{id}", kind, rows.GetString(2) ?? "image/jpeg",
                rows.IsNull(3) ? 0 : rows.GetLong(3), kind == MediaKind.Video && !rows.IsNull(5) ? rows.GetLong(5) : null));
            next = $"{rows.GetLong(4)}:{id}";
        }
        return new GalleryPage(items, null);
    }, ct);

    public Task<byte[]?> GetThumbnailAsync(GalleryMediaItem item, int size, CancellationToken ct = default) => Task.Run(() =>
    {
        using var uri = Uri.Parse(item.Id)!;
        using var signal = new CancellationSignal();
        using var registration = ct.Register(signal.Cancel);
        using var bitmap = OperatingSystem.IsAndroidVersionAtLeast(29)
            ? Resolver.LoadThumbnail(uri, new global::Android.Util.Size(size, size), signal)
            : LoadLegacyThumbnail(item, uri);
        if (bitmap is null) return null;
        var scale = Math.Min(1d, (double)size / Math.Max(bitmap.Width, bitmap.Height));
        using var small = Bitmap.CreateScaledBitmap(bitmap, Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true);
        using var bytes = new MemoryStream();
        small.Compress(Bitmap.CompressFormat.Jpeg!, 80, bytes);
        ct.ThrowIfCancellationRequested();
        return bytes.ToArray();
    }, ct);

    private static Bitmap? LoadLegacyThumbnail(GalleryMediaItem item, Uri uri)
    {
        if (item.Kind == MediaKind.Video)
        {
            using var video = new MediaMetadataRetriever();
            video.SetDataSource(global::Android.App.Application.Context, uri);
            return video.GetFrameAtTime(0);
        }
        using var stream = Resolver.OpenInputStream(uri);
        return stream is null ? null : BitmapFactory.DecodeStream(stream, null, new BitmapFactory.Options { InSampleSize = 8 });
    }

    public async Task<MediaSelection> CreateSelectionAsync(GalleryMediaItem item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (item.Kind == MediaKind.Photo && item.ContentType is not ("image/jpeg" or "image/png" or "image/gif"))
        {
            return await Task.Run(() =>
            {
                using var source = Resolver.OpenInputStream(Uri.Parse(item.Id)!) ?? throw new FileNotFoundException("Фотография недоступна.");
                using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeStream(source, null, bounds);
                var sample = 1;
                while (bounds.OutWidth / sample > 4096 || bounds.OutHeight / sample > 4096) sample *= 2;
                using var input = Resolver.OpenInputStream(Uri.Parse(item.Id)!)!;
                using var bitmap = BitmapFactory.DecodeStream(input, null, new BitmapFactory.Options { InSampleSize = sample })
                    ?? throw new InvalidDataException("Не удалось прочитать фотографию.");
                using var output = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 90, output);
                var bytes = output.ToArray();
                ct.ThrowIfCancellationRequested();
                return new MediaSelection(MediaKind.Photo, Path.ChangeExtension(item.FileName, ".jpg"), "image/jpeg", bytes.Length, null,
                    () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
            }, ct);
        }
        return new(item.Kind, item.FileName, item.ContentType, item.Size, item.DurationMs, () =>
            Task.FromResult(Resolver.OpenInputStream(Uri.Parse(item.Id)!) ?? throw new FileNotFoundException("Файл больше недоступен в галерее.")));
    }
}

public sealed class GalleryReadPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions => OperatingSystem.IsAndroidVersionAtLeast(34)
        ? [("android.permission.READ_MEDIA_IMAGES", true), ("android.permission.READ_MEDIA_VIDEO", true), ("android.permission.READ_MEDIA_VISUAL_USER_SELECTED", true)]
        : OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [("android.permission.READ_MEDIA_IMAGES", true), ("android.permission.READ_MEDIA_VIDEO", true)]
            : [("android.permission.READ_EXTERNAL_STORAGE", true)];
}
