#if ANDROID || IOS || MACCATALYST || WINDOWS
using Plugin.Maui.Audio;
using System.Diagnostics;
#endif

namespace Messenger.Maui.Services;

public enum MediaKind { File, Photo, Video, Audio }
public enum MediaCaptureStatus { Selected, Cancelled, PermissionDenied }

public sealed record MediaSelection(
    MediaKind Kind,
    string FileName,
    string ContentType,
    long Size,
    long? DurationMs,
    Func<Task<Stream>> OpenReadAsync);

public sealed record MediaCaptureResult(MediaCaptureStatus Status, MediaSelection? Selection, string? Message)
{
    public static MediaCaptureResult Selected(MediaSelection value) => new(MediaCaptureStatus.Selected, value, null);
    public static MediaCaptureResult Cancelled() => new(MediaCaptureStatus.Cancelled, null, null);
    public static MediaCaptureResult PermissionDenied(string message) => new(MediaCaptureStatus.PermissionDenied, null, message);
}

public interface IMediaCaptureService
{
    Task<MediaCaptureResult> AcquireAsync(MediaKind kind, CancellationToken ct = default);
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public sealed class NativeMediaCaptureService(IAudioManager audioManager) : IMediaCaptureService
{
    public async Task<MediaCaptureResult> AcquireAsync(MediaKind kind, CancellationToken ct = default)
    {
        try
        {
            if (kind == MediaKind.Audio)
            {
                return await RecordAudioAsync(ct);
            }
            FileResult? result = kind switch
            {
                MediaKind.Photo => await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Сделать фото" }),
                MediaKind.Video => await MediaPicker.Default.CaptureVideoAsync(new MediaPickerOptions { Title = "Снять видео" }),
                _ => await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Выберите файл" })
            };
            if (result is null) return MediaCaptureResult.Cancelled();
            await using var probe = await result.OpenReadAsync();
            var size = probe.CanSeek ? probe.Length : 0;
            var selection = new MediaSelection(kind, result.FileName, result.ContentType ?? Mime(kind), size, null,
                async () => await result.OpenReadAsync());
            return MediaCaptureResult.Selected(selection);
        }
        catch (PermissionException)
        {
            return MediaCaptureResult.PermissionDenied("Нет разрешения на камеру, микрофон или файлы.");
        }
        catch (OperationCanceledException)
        {
            return MediaCaptureResult.Cancelled();
        }
    }

    private async Task<MediaCaptureResult> RecordAudioAsync(CancellationToken ct)
    {
        var permission = await Permissions.RequestAsync<Permissions.Microphone>();
        if (permission != PermissionStatus.Granted)
        {
            return MediaCaptureResult.PermissionDenied("Нет разрешения на микрофон.");
        }
        var recorder = audioManager.CreateRecorder();
        var elapsed = Stopwatch.StartNew();
        await recorder.StartAsync();
        await Shell.Current.DisplayAlertAsync("Аудиосообщение", "Идёт запись. Нажмите «Стоп», когда закончите.", "Стоп");
        var source = await recorder.StopAsync();
        elapsed.Stop();
        var file = (FileAudioSource)source;
        var filePath = file.GetFilePath();
        var info = new FileInfo(filePath);
        var extension = info.Extension.ToLowerInvariant();
        var mime = extension == ".wav" ? "audio/wav" : "audio/mp4";
        return MediaCaptureResult.Selected(new MediaSelection(
            MediaKind.Audio, info.Name, mime, info.Length, (long)elapsed.Elapsed.TotalMilliseconds,
            () => Task.FromResult<Stream>(File.OpenRead(filePath))));
    }

    private static string Mime(MediaKind kind) => kind switch
    {
        MediaKind.Photo => "image/jpeg",
        MediaKind.Video => "video/mp4",
        MediaKind.Audio => "audio/mp4",
        _ => "application/octet-stream"
    };
}
#endif
