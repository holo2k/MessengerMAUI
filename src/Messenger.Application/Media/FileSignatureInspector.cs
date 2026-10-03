namespace Messenger.Application.Media;

public sealed class FileSignatureInspector : IFileInspector
{
    public const int MaxImageBytes = 25 * 1024 * 1024;
    public const int MaxAudioBytes = 50 * 1024 * 1024;
    public const int MaxVideoBytes = 100 * 1024 * 1024;
    public const int MaxFileBytes = 50 * 1024 * 1024;

    private sealed record Rule(string Extension, string Mime, int Max, byte[] Signature, int Offset = 0);
    private static readonly Rule[] Rules =
    [
        new(".png", "image/png", MaxImageBytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        new(".jpg", "image/jpeg", MaxImageBytes, [0xFF, 0xD8, 0xFF]),
        new(".jpeg", "image/jpeg", MaxImageBytes, [0xFF, 0xD8, 0xFF]),
        new(".gif", "image/gif", MaxImageBytes, [0x47, 0x49, 0x46, 0x38]),
        new(".pdf", "application/pdf", MaxFileBytes, [0x25, 0x50, 0x44, 0x46]),
        new(".mp3", "audio/mpeg", MaxAudioBytes, [0x49, 0x44, 0x33]),
        new(".ogg", "audio/ogg", MaxAudioBytes, [0x4F, 0x67, 0x67, 0x53]),
        new(".wav", "audio/wav", MaxAudioBytes, [0x52, 0x49, 0x46, 0x46]),
        new(".m4a", "audio/mp4", MaxAudioBytes, [0x66, 0x74, 0x79, 0x70], 4),
        new(".mp4", "video/mp4", MaxVideoBytes, [0x66, 0x74, 0x79, 0x70], 4)
    ];

    public void Validate(string fileName, string declaredContentType, string storedContentType, long size, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rule = Rules.FirstOrDefault(value => value.Extension == extension &&
            string.Equals(value.Mime, declaredContentType, StringComparison.OrdinalIgnoreCase));
        if (rule is null || !string.Equals(rule.Mime, storedContentType, StringComparison.OrdinalIgnoreCase) ||
            header.Length < rule.Offset + rule.Signature.Length ||
            !header.Slice(rule.Offset, rule.Signature.Length).SequenceEqual(rule.Signature))
        {
            throw new MediaException(MediaError.FileTypeMismatch);
        }
        if (size <= 0 || size > rule.Max)
        {
            throw new MediaException(MediaError.FileTooLarge);
        }
    }
}
