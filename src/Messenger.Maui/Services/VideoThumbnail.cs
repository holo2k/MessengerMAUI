using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public static class VideoThumbnail
{
    public static async Task<byte[]> CreateAsync(StoredObjectResponse attachment, IMessageAttachmentClient client, CancellationToken ct)
    {
        var path = await AttachmentFile.DownloadAsync(attachment, client, ct);
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
#if ANDROID
            using var retriever = new Android.Media.MediaMetadataRetriever();
            retriever.SetDataSource(path);
            using var frame = (OperatingSystem.IsAndroidVersionAtLeast(27)
                ? retriever.GetScaledFrameAtTime(0, Android.Media.Option.ClosestSync, 320, 320)
                : retriever.GetFrameAtTime(0, Android.Media.Option.ClosestSync))
                ?? throw new InvalidDataException("Не удалось получить кадр видео.");
            using var buffer = new MemoryStream();
            frame.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 80, buffer);
            return buffer.ToArray();
#elif IOS
            using var url = Foundation.NSUrl.FromFilename(path);
            using var asset = AVFoundation.AVUrlAsset.Create(url, null);
            using var generator = new AVFoundation.AVAssetImageGenerator(asset)
                { AppliesPreferredTrackTransform = true, MaximumSize = new CoreGraphics.CGSize(320, 320) };
            using var frame = generator.CopyCGImageAtTime(new CoreMedia.CMTime(0, 1), out _, out var error);
            using (error)
            {
                if (frame is null) throw new InvalidDataException("Не удалось получить кадр видео.");
                using var image = UIKit.UIImage.FromImage(frame);
                using var data = image.AsJPEG(0.8f);
                return data?.ToArray() ?? throw new InvalidDataException("Не удалось получить кадр видео.");
            }
#else
            throw new PlatformNotSupportedException();
#endif
        }, ct);
    }
}
