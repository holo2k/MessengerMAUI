using System.Net;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Music;

public sealed class AudioSourceLoaderTests
{
    [Fact]
    public async Task Remote_audio_is_fully_buffered_before_native_player_can_read_synchronously()
    {
        byte[] audio = [0x49, 0x44, 0x33, 1, 2, 3];
        var network = new AsyncOnlyStream(audio);
        using var client = new HttpClient(new Handler(network));

        await using var result = await AudioSourceLoader.OpenAsync(client, "https://audio.test/track.mp3", CancellationToken.None);

        Assert.True(network.IsDisposed);
        Assert.True(result.CanSeek);
        Assert.Equal(0, result.Position);
        Assert.Equal(audio.Length, result.Length);
        var actual = new byte[audio.Length];
        result.ReadExactly(actual);
        Assert.Equal(audio, actual);
    }

    [Fact]
    public async Task Cancelled_load_does_not_return_audio()
    {
        using var client = new HttpClient(new Handler(new AsyncOnlyStream([1, 2, 3])));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AudioSourceLoader.OpenAsync(client, "https://audio.test/track.mp3", cancellation.Token));
    }

    private sealed class Handler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }

    private sealed class AsyncOnlyStream(byte[] audio) : Stream
    {
        private readonly MemoryStream _inner = new(audio);
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous network reads are forbidden.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { IsDisposed = true; _inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
