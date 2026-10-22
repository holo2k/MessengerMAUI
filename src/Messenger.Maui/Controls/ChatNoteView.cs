using Messenger.Contracts.Messages;
using Messenger.Contracts.Media;
using Messenger.Maui.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Audio;

namespace Messenger.Maui.Controls;

public sealed class ChatNoteView : ContentView
{
    private readonly bool _isVideo;
    private readonly StoredObjectResponse? _attachment;
    private readonly string? _draftPath;
    private readonly long _duration;
    private readonly ImageButton _play = new() { Source = "music_play_dark.png", WidthRequest = 40, HeightRequest = 40, Padding = 10 };
    private readonly Label _time = new() { FontSize = 11, TextColor = Color.FromArgb("#52778A") };
    private readonly Button _speed = new() { Text = "1×", FontSize = 11, Padding = 0, WidthRequest = 38, HeightRequest = 28, MinimumHeightRequest = 28 };
    private readonly ActivityIndicator _loading = new() { Color = Color.FromArgb("#0088FF"), IsVisible = false, WidthRequest = 24, HeightRequest = 24 };
    private readonly NoteWaveformView _wave = new();
    private readonly InlineNoteVideo? _video;
    private IAudioPlayer? _audio;
    private CancellationTokenSource? _lifetime;
    private IDispatcherTimer? _timer;
    private string? _path;
    private bool _busy;
    private double _rate = 1;
    private string? _playbackError;
    private bool Playing => _video?.IsPlaying == true || _audio?.IsPlaying == true;

    public ChatNoteView(MessageResponse message) : this(message.Type is "video_note" or "videonote", message.Attachments?.FirstOrDefault(), null,
        message.DurationMs ?? 0, message.Waveform ?? []) { }
    public ChatNoteView(RecordedChatNote note) : this(note.Kind == ChatNoteKind.VideoNote, null, note.LocalPath, note.DurationMs, note.Waveform) { }
    private ChatNoteView(bool video, StoredObjectResponse? attachment, string? draftPath, long duration, IReadOnlyList<byte> waveform)
    {
        _isVideo = video; _attachment = attachment; _draftPath = draftPath; _duration = duration;
        WidthRequest = video ? 228 : 246;
        _wave.Samples = waveform;
        _play.Clicked += OnPlay;
        _speed.Clicked += (_, _) =>
        {
            _rate = _rate == 1 ? 1.5 : _rate == 1.5 ? 2 : 1;
            if (_audio?.CanSetSpeed == true) _audio.Speed = _rate;
            _speed.Text = $"{_rate:0.#}×";
        };
        if (video)
        {
            _video = new InlineNoteVideo { WidthRequest = 220, HeightRequest = 220 };
            _video.Ended += (_, _) => UpdatePosition();
            _video.Failed += (_, _) => { _playbackError = "Не удалось воспроизвести. Нажмите ещё раз."; _path = null; _video.Source = null; };
            var circle = new Grid { WidthRequest = 220, HeightRequest = 220, IsClippedToBounds = true, BackgroundColor = Color.FromArgb("#E8EDF2") };
            circle.Add(_video);
            circle.Add(_play); _play.HorizontalOptions = _play.VerticalOptions = LayoutOptions.Center;
            circle.Add(_loading); _loading.HorizontalOptions = _loading.VerticalOptions = LayoutOptions.Center;
            var border = new Border { StrokeThickness = 0, StrokeShape = new Ellipse(), Content = circle, WidthRequest = 220, HeightRequest = 220 };
            _wave.Ring = true; _wave.WidthRequest = _wave.HeightRequest = 228;
            var surface = new Grid { WidthRequest = 228, HeightRequest = 228 };
            surface.Add(border); border.HorizontalOptions = border.VerticalOptions = LayoutOptions.Center;
            surface.Add(_wave); _wave.InputTransparent = true;
            var tap = new TapGestureRecognizer(); tap.Tapped += OnPlay; circle.GestureRecognizers.Add(tap);
            Content = new VerticalStackLayout { Spacing = 4, Children = { surface, _time } };
            _time.HorizontalOptions = LayoutOptions.Center;
        }
        else
        {
            var grid = new Grid { ColumnDefinitions = [new(40), new(GridLength.Star), new(38)], RowDefinitions = [new(32), new(20)], ColumnSpacing = 8, Padding = new Thickness(0, 4) };
            grid.Add(_play); Grid.SetRowSpan(_play, 2); _play.VerticalOptions = LayoutOptions.Center;
            grid.Add(_wave, 1); Grid.SetColumnSpan(_wave, 2);
            grid.Add(_time, 1, 1); grid.Add(_speed, 2, 1);
            grid.Add(_loading); Grid.SetRowSpan(_loading, 2);
            var seek = new TapGestureRecognizer();
            seek.Tapped += (_, e) =>
            {
                if (e.GetPosition(_wave) is not { } point || _wave.Width <= 0) return;
                var target = Math.Clamp(point.X / _wave.Width, 0, 1) * (_audio?.Duration * 1000 ?? _duration);
                _audio?.Seek(target / 1000); UpdatePosition();
            };
            _wave.GestureRecognizers.Add(seek);
            Content = grid;
        }
        SemanticProperties.SetDescription(this, video ? "Видеосообщение" : "Голосовое сообщение");
        SemanticProperties.SetDescription(_play, "Воспроизвести или приостановить сообщение");
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            ChatNotePlayback.ActiveChanged -= OnOtherPlayback;
            _lifetime?.Cancel(); _timer?.Stop(); _timer = null;
            _audio?.Stop(); _audio?.Dispose(); _audio = null;
            if (_video is not null) { _video.IsPlaying = false; _video.Source = null; }
        };
        UpdatePosition();
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = new();
        _loading.IsRunning = _loading.IsVisible = false; _play.IsVisible = true;
        ChatNotePlayback.ActiveChanged -= OnOtherPlayback; ChatNotePlayback.ActiveChanged += OnOtherPlayback;
        _timer?.Stop(); _timer = Dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => UpdatePosition(); _timer.Start();
        if (_isVideo)
        {
            try { await EnsureSourceAsync(_lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { _playbackError = "Не удалось загрузить. Нажмите для повтора."; }
        }
    }

    private async Task EnsureSourceAsync(CancellationToken ct)
    {
        if (_path is null)
        {
            _loading.IsRunning = _loading.IsVisible = true; _play.IsVisible = false;
            try
            {
                _path = _draftPath ?? await AttachmentFile.DownloadAsync(_attachment ?? throw new InvalidDataException("Вложение недоступно."),
                    Handler!.MauiContext!.Services.GetRequiredService<IMessageAttachmentClient>(), ct);
                ct.ThrowIfCancellationRequested();
            }
            finally { if (!ct.IsCancellationRequested) { _loading.IsRunning = _loading.IsVisible = false; _play.IsVisible = true; } }
        }
        if (_video is not null) _video.Source = _path;
        else if (_audio is null)
        {
            _audio = Handler!.MauiContext!.Services.GetRequiredService<IAudioManager>().CreatePlayer(_path);
            _audio.PlaybackEnded += (_, _) => MainThread.BeginInvokeOnMainThread(UpdatePosition);
            if (_audio.CanSetSpeed) _audio.Speed = _rate;
        }
    }

    private async void OnPlay(object? sender, EventArgs e)
    {
        if (_busy || _loading.IsRunning || _lifetime is null) return;
        _busy = true;
        try
        {
            if (Playing) { Pause(); return; }
            _playbackError = null;
            await EnsureSourceAsync(_lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if ((Shell.Current as AppShell)?.Music is { IsPlaying: true } music) music.TogglePause();
            ChatNotePlayback.Activate(this);
            if (_video is not null) _video.IsPlaying = true;
            else if (_audio is not null) { if (_audio.CurrentPosition >= _audio.Duration - 0.1) _audio.Seek(0); _audio.Play(); }
            UpdatePosition();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _playbackError = "Не удалось воспроизвести. Нажмите ещё раз."; _path = null; _audio?.Dispose(); _audio = null; }
        finally { _busy = false; }
    }
    private void OnOtherPlayback(object? owner) { if (!ReferenceEquals(owner, this)) Pause(); }
    private void Pause() { if (_video is not null) _video.IsPlaying = false; _audio?.Pause(); UpdatePosition(); }
    private void UpdatePosition()
    {
        var position = _video?.PositionMs ?? (_audio?.CurrentPosition ?? 0) * 1000;
        var duration = _video?.DurationMs ?? (_audio?.Duration ?? 0) * 1000;
        if (duration <= 0) duration = _duration;
        _wave.Progress = duration > 0 ? position / duration : 0; _wave.Invalidate();
        _time.Text = _playbackError ?? (duration > 0 ? Playing ? $"{PlaybackTime.Format(position)} / {PlaybackTime.Format(duration)}" : PlaybackTime.Format(duration)
            : _isVideo ? "Видеосообщение" : "Голосовое сообщение");
        _play.Source = Playing ? "music_pause_dark.png" : "music_play_dark.png";
    }
}
