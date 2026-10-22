using System.Buffers.Binary;

namespace Messenger.Maui.Services;

public enum ChatNoteKind { Voice, VideoNote }
public sealed record RecordedChatNote(ChatNoteKind Kind, MediaSelection Media, string LocalPath, long DurationMs, byte[] Waveform);
public interface IChatNoteRecorder
{
    event Action<byte>? LevelChanged;
    Task StartAsync(ChatNoteKind kind, CancellationToken ct = default);
    Task<RecordedChatNote> StopAsync(CancellationToken ct = default);
    Task CancelAsync();
    void Delete(RecordedChatNote note);
}

public static class VoiceWaveform
{
    public static byte Level(ReadOnlySpan<byte> pcm)
    {
        var peak = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm[i..])));
        return (byte)Math.Clamp(Math.Round(100 * Math.Sqrt(peak / 32768d)), 0, 100);
    }

    public static byte[] Compress(IReadOnlyList<byte> levels, int count = 48)
    {
        if (levels.Count == 0) return [];
        count = Math.Clamp(count, 1, 64);
        var result = new byte[Math.Min(count, levels.Count)];
        for (var i = 0; i < result.Length; i++)
        {
            var start = i * levels.Count / result.Length;
            var end = (i + 1) * levels.Count / result.Length;
            for (var index = start; index < end; index++) result[i] = Math.Max(result[i], levels[index]);
        }
        return result;
    }

    public static void WriteWavHeader(Stream stream, int sampleRate, int dataLength)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(dataLength + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(dataLength);
    }
}
