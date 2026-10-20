namespace Messenger.Maui.Services;

public static class MusicCoverPicker
{
    private static readonly FilePickerFileType ImageTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["image/jpeg", "image/png"],
        [DevicePlatform.iOS] = ["public.jpeg", "public.png"],
        [DevicePlatform.MacCatalyst] = ["public.jpeg", "public.png"],
        [DevicePlatform.WinUI] = [".jpg", ".jpeg", ".png"]
    });

    public static async Task<MediaSelection?> PickAsync(CancellationToken ct = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Выберите обложку (JPEG, PNG)", FileTypes = ImageTypes });
        if (file is null) return null;
        ct.ThrowIfCancellationRequested();
        var contentType = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => throw new InvalidDataException("Выберите изображение JPEG или PNG.")
        };
        await using var input = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        var block = new byte[64 * 1024];
        int count;
        while ((count = await input.ReadAsync(block.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + count > 25 * 1024 * 1024)
                throw new InvalidDataException("Размер обложки не должен превышать 25 МБ.");
            await buffer.WriteAsync(block.AsMemory(0, count), ct);
        }
        if (buffer.Length == 0) throw new InvalidDataException("Выбранный файл пуст.");
        var bytes = buffer.ToArray();
        return new(MediaKind.Photo, file.FileName, contentType, bytes.LongLength, null,
            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }
}
