using Android.Media;
using Messenger.Maui.Services;

namespace Messenger.Maui.Platforms.Android;

// MediaPlayer owns HTTP range requests and buffers while playing. No network
// stream is passed to a synchronous decoder on the Android UI thread.
public sealed class StreamingAudioPlayer : IAudioPlayerService
{
    private MediaPlayer? _player;
    private TaskCompletionSource? _preparation;
    private bool _prepared, _ended, _initializing;
    public bool IsPlaying => _prepared && _player?.IsPlaying == true;
    public bool IsBuffering { get; private set; }
    public double BufferedProgress { get; private set; }
    public string? PlaybackError { get; private set; }
    public double DurationMs => _prepared ? _player?.Duration ?? 0 : 0;
    public double PositionMs => _ended ? DurationMs : _prepared ? _player?.CurrentPosition ?? 0 : 0;
    public event EventHandler? PlaybackEnded;

    public async Task PlayAsync(string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaPlayer? native = null;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Stop();
                ct.ThrowIfCancellationRequested();
                native = new MediaPlayer();
                _player = native;
                _preparation = ready;
                _initializing = true;
                IsBuffering = true;
                native.SetAudioAttributes(new AudioAttributes.Builder()!
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Music)!.Build());
                native.Prepared += (_, _) =>
                {
                    if (_player != native || ct.IsCancellationRequested) return;
                    try
                    {
                        _prepared = true;
                        IsBuffering = false;
                        native.Start();
                        ready.TrySetResult();
                    }
                    catch (Exception ex) { _prepared = false; IsBuffering = false; ready.TrySetException(ex); }
                };
                native.BufferingUpdate += (_, e) =>
                {
                    if (_player == native) BufferedProgress = Math.Clamp(e.Percent / 100d, 0, 1);
                };
                native.Info += (_, e) =>
                {
                    if (_player != native) return;
                    if (e.What == MediaInfo.BufferingStart && native.IsPlaying) IsBuffering = true;
                    if (e.What == MediaInfo.BufferingEnd) IsBuffering = false;
                };
                native.Completion += (_, _) =>
                {
                    if (_player != native || !_prepared) return;
                    _ended = true;
                    IsBuffering = false;
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                };
                native.Error += (_, e) =>
                {
                    e.Handled = true;
                    if (_player != native) return;
                    _prepared = false;
                    IsBuffering = false;
                    PlaybackError = "Не удалось воспроизвести трек. Проверьте соединение и попробуйте ещё раз.";
                    ready.TrySetException(new IOException(PlaybackError));
                };
            });
            // setDataSource can open a connection; it must also stay off the UI thread.
            await Task.Run(() => { ct.ThrowIfCancellationRequested(); native!.SetDataSource(source); native.PrepareAsync(); }, ct);
            await ready.Task.WaitAsync(ct);
            if (_player == native) _initializing = false;
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() => { if (_player == native) Stop(); });
            throw;
        }
        finally
        {
            // A superseded connection may still be finishing setDataSource.
            // Release it only once that call has returned.
            if (_player != native) { native?.Release(); native?.Dispose(); }
        }
    }

    public void Pause() { if (IsPlaying) _player!.Pause(); IsBuffering = false; }
    public void Resume() { if (_prepared) { if (_ended) Seek(0); _player!.Start(); } }
    public void Seek(double milliseconds)
    {
        if (!_prepared || !double.IsFinite(milliseconds)) return;
        _ended = false;
        _player!.SeekTo((int)Math.Clamp(milliseconds, 0, DurationMs));
    }
    public void Stop()
    {
        var native = _player;
        var deferRelease = _initializing;
        _player = null;
        _initializing = false;
        _prepared = _ended = false;
        IsBuffering = false;
        BufferedProgress = 0;
        PlaybackError = null;
        _preparation?.TrySetCanceled();
        _preparation = null;
        if (!deferRelease) { native?.Release(); native?.Dispose(); }
    }
}
