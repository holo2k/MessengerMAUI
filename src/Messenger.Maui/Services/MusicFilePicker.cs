using Plugin.Maui.Audio;

namespace Messenger.Maui.Services;

public interface IMusicFilePicker
{
    Task<MediaSelection?> PickAsync(CancellationToken ct = default);
}

public sealed class MusicFilePicker(IAudioManager audioManager) : IMusicFilePicker
{
    private const int MaxBytes = 50 * 1024 * 1024;
    private static readonly FilePickerFileType AudioTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = new[] { "audio/*", "application/ogg" },
        [DevicePlatform.iOS] = new[] { "public.audio" },
        [DevicePlatform.MacCatalyst] = new[] { "public.audio" },
        [DevicePlatform.WinUI] = new[] { ".mp3", ".m4a", ".wav", ".ogg" }
    });

    public async Task<MediaSelection?> PickAsync(CancellationToken ct = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Выберите музыку (MP3, M4A, WAV, OGG)",
            FileTypes = AudioTypes
        });
        if (file is null) return null;
        ct.ThrowIfCancellationRequested();

        var contentType = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => throw new InvalidDataException("Выберите файл MP3, M4A, WAV или OGG.")
        };

        // Content providers can return non-seekable streams without a known length.
        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        var block = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(block.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + count > MaxBytes)
                throw new InvalidDataException("Размер трека не должен превышать 50 МБ.");
            await buffer.WriteAsync(block.AsMemory(0, count), ct);
        }
        if (buffer.Length == 0) throw new InvalidDataException("Выбранный файл пуст.");
        buffer.Position = 0;
        using var player = audioManager.CreatePlayer(buffer);
        var seconds = player.Duration;
        if (!double.IsFinite(seconds) || seconds <= 0)
            throw new InvalidDataException("Не удалось прочитать длительность трека. Выберите другой аудиофайл.");

        var bytes = buffer.ToArray();
        return new MediaSelection(MediaKind.Audio, file.FileName, contentType, bytes.LongLength,
            Math.Max(1, (long)Math.Round(seconds * 1000)),
            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }
}
