using System.Diagnostics;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Plugin.Maui.Audio;

namespace Messenger.Maui.Services;

public sealed class NativeChatNoteRecorder(IAudioManager audio, ContentView cameraHost) : IChatNoteRecorder
{
    private readonly object _audioLock = new();
    private readonly List<byte> _levels = [];
    private IAudioStreamer? _streamer;
    private FileStream? _file;
    private CameraView? _camera;
    private ChatNoteKind _kind;
    private string? _path;
    private readonly Stopwatch _elapsed = new();
    private bool _recordingVideo;
    private IOException? _audioError;
    private const int SampleRate = 16000;
    public event Action<byte>? LevelChanged;

    public async Task StartAsync(ChatNoteKind kind, CancellationToken ct = default)
    {
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            throw new PermissionException("Разрешите доступ к микрофону для записи сообщения.");
        ct.ThrowIfCancellationRequested();
        if (kind == ChatNoteKind.VideoNote && await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            throw new PermissionException("Разрешите доступ к камере для записи кружочка.");
        ct.ThrowIfCancellationRequested();
        _kind = kind;
        var folder = Path.Combine(FileSystem.CacheDirectory, "chat-recordings");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, $"{(kind == ChatNoteKind.Voice ? "voice" : "video_note")}_{Guid.NewGuid():N}.{(kind == ChatNoteKind.Voice ? "wav" : "mp4")}");
        _levels.Clear();
        _audioError = null;
        if (kind == ChatNoteKind.Voice)
        {
            _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            VoiceWaveform.WriteWavHeader(_file, SampleRate, 0);
            _streamer = audio.CreateStreamer();
            _streamer.Options.SampleRate = SampleRate;
            _streamer.Options.Channels = ChannelType.Mono;
            _streamer.Options.BitDepth = BitDepth.Pcm16bit;
            _streamer.OnAudioCaptured += OnAudioCaptured;
            await _streamer.StartAsync();
        }
        else
        {
            // A fresh view for each take avoids stale native camera recording sessions.
            var camera = _camera = new CameraView();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded(object? sender, EventArgs args) => ready.TrySetResult();
            camera.Loaded += OnLoaded;
            cameraHost.Content = camera;
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            finally { camera.Loaded -= OnLoaded; }
            var cameras = await camera.GetAvailableCameras(ct);
            camera.SelectedCamera = cameras.FirstOrDefault(value => value.Position == CameraPosition.Front) ?? cameras.FirstOrDefault()
                ?? throw new InvalidOperationException("Камера недоступна.");
            await camera.StartCameraPreview(ct);
            _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            await camera.StartVideoRecording(_file, ct);
            _recordingVideo = true;
        }
        ct.ThrowIfCancellationRequested();
        _elapsed.Restart();
    }

    private void OnAudioCaptured(object? sender, AudioStreamEventArgs e)
    {
        var level = VoiceWaveform.Level(e.Audio);
        lock (_audioLock)
        {
            if (_file is null) return;
            if (_audioError is not null) return;
            try
            {
                _file.Write(e.Audio);
                // Sample actual PCM every 10 ms, independently of the platform callback size.
                for (var offset = 0; offset < e.Audio.Length; offset += 320)
                    _levels.Add(VoiceWaveform.Level(e.Audio.AsSpan(offset, Math.Min(320, e.Audio.Length - offset))));
            }
            catch (IOException exception) { _audioError = exception; }
        }
        MainThread.BeginInvokeOnMainThread(() => LevelChanged?.Invoke(level));
    }

    public async Task<RecordedChatNote> StopAsync(CancellationToken ct = default)
    {
        _elapsed.Stop();
        if (_path is null) throw new InvalidOperationException("Запись не начата.");
        var path = _path;
        long duration;
        byte[] waveform;
        if (_kind == ChatNoteKind.Voice)
        {
            if (_streamer is not null)
            {
                await _streamer.StopAsync();
                _streamer.OnAudioCaptured -= OnAudioCaptured;
                (_streamer as IDisposable)?.Dispose(); _streamer = null;
            }
            lock (_audioLock)
            {
                if (_audioError is not null) throw new IOException("Не удалось сохранить голосовое сообщение.", _audioError);
                var length = checked((int)(_file!.Length - 44));
                duration = Math.Min(600_000, length * 1000L / (SampleRate * 2));
                _file.Position = 0;
                VoiceWaveform.WriteWavHeader(_file, SampleRate, length);
                _file.Dispose(); _file = null;
                waveform = VoiceWaveform.Compress(_levels);
            }
        }
        else
        {
            try
            {
                if (_recordingVideo && _camera is not null)
                {
                    await _camera.StopVideoRecording(ct);
                    _recordingVideo = false;
                }
                if (_file is not null) { await _file.DisposeAsync(); _file = null; }
            }
            finally { CloseCamera(); }
            duration = Math.Min(60_000, _elapsed.ElapsedMilliseconds);
            waveform = [];
        }
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) throw new InvalidDataException("Не удалось сохранить запись. Попробуйте ещё раз.");
        var selection = new MediaSelection(_kind == ChatNoteKind.Voice ? MediaKind.Audio : MediaKind.Video, info.Name,
            _kind == ChatNoteKind.Voice ? "audio/wav" : "video/mp4", info.Length, duration,
            () => Task.FromResult<Stream>(File.OpenRead(path)));
        _path = null;
        return new(_kind, selection, path, duration, waveform);
    }

    public async Task CancelAsync()
    {
        try
        {
            if (_streamer is not null)
            {
                _streamer.OnAudioCaptured -= OnAudioCaptured;
                try { await _streamer.StopAsync(); } finally { (_streamer as IDisposable)?.Dispose(); _streamer = null; }
            }
            if (_recordingVideo && _camera is not null)
                try { await _camera.StopVideoRecording(); } finally { _recordingVideo = false; }
        }
        catch (Exception exception) { Debug.WriteLine($"Cancel chat recording: {exception.Message}"); }
        finally
        {
            lock (_audioLock) { _file?.Dispose(); _file = null; }
            CloseCamera();
            if (_path is { } path && File.Exists(path)) File.Delete(path);
            _path = null;
        }
    }

    public void Delete(RecordedChatNote note)
    {
        try { if (File.Exists(note.LocalPath)) File.Delete(note.LocalPath); }
        catch (IOException exception) { Debug.WriteLine($"Delete recording cache: {exception.Message}"); }
    }
    private void CloseCamera()
    {
        if (_camera is not { } camera) return;
        _camera = null;
        try { camera.StopCameraPreview(); }
        catch (Exception exception) { Debug.WriteLine($"Stop camera preview: {exception.Message}"); }
        cameraHost.Content = null;
        camera.Handler?.DisconnectHandler();
        camera.Dispose();
    }
}
